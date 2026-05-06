using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation.Media;

/// <summary>
/// Advertises federation-ingested BaseItems as remote media sources. For each
/// <see cref="FederationIngestedItem"/> row pointing at the queried BaseItem, one
/// <see cref="MediaSourceInfo"/> is returned with <c>RequiresOpening = true</c>; on open, the
/// provider signs a request to the origin instance's <c>/Federation/Media/{sourceId}/Token</c>
/// endpoint and resolves the returned presigned URL into an <see cref="ILiveStream"/>.
/// </summary>
public sealed class FederatedMediaSourceProvider : IMediaSourceProvider
{
    private const string OpenTokenSeparator = ":";

    private static readonly TimeSpan RemoteTokenRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFederationSigningService _signingService;
    private readonly ILogger<FederatedMediaSourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedMediaSourceProvider"/> class.
    /// </summary>
    /// <param name="dbFactory">Instance of the <see cref="IDbContextFactory{JellyfinDbContext}"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="signingService">Instance of the <see cref="IFederationSigningService"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{FederatedMediaSourceProvider}"/> interface.</param>
    public FederatedMediaSourceProvider(
        IDbContextFactory<JellyfinDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IFederationSigningService signingService,
        ILogger<FederatedMediaSourceProvider> logger)
    {
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _signingService = signingService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        var itemId = item.Id;

        var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = await db.FederationIngestedItems
                .Include(r => r.Actor)
                .Where(r => r.BaseItemId.Equals(itemId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.Select(row => new MediaSourceInfo
            {
                Id = $"federation:{row.ActorId}:{row.SourceId:N}",
                Name = $"Remote @ {GetHostname(row.Actor.Url)}",
                Type = MediaSourceType.Default,
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                RequiresOpening = true,
                OpenToken = BuildOpenToken(row.ActorId, row.SourceId),
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = false
            }).ToList<MediaSourceInfo>();
        }
    }

    /// <inheritdoc/>
    public async Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        if (!TryParseOpenToken(openToken, out var actorId, out var sourceId))
        {
            throw new ArgumentException($"Invalid OpenToken: {openToken}", nameof(openToken));
        }

        FederationActor? actor;
        var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            actor = await db.FederationActors
                .FirstOrDefaultAsync(a => a.Id == actorId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (actor is null)
        {
            throw new InvalidOperationException($"Unknown federation actor {actorId}");
        }

        var hostname = GetHostname(actor.Url);
        var tokenUrl = $"https://{hostname}/Federation/Media/{sourceId:N}/Token";

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/activity+json");

        await _signingService.SignAsync(request).ConfigureAwait(false);

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        httpClient.Timeout = RemoteTokenRequestTimeout;
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Remote token request to {Host} for {SourceId} returned {StatusCode}",
                hostname,
                sourceId,
                (int)response.StatusCode);
            throw new InvalidOperationException($"Remote token request returned {(int)response.StatusCode}");
        }

        var tokenResponse = await response.Content
            .ReadFromJsonAsync<RemoteTokenResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.StreamUrl))
        {
            throw new InvalidOperationException($"Remote token response from {hostname} was empty or missing StreamUrl");
        }

        return new FederatedLiveStream(new MediaSourceInfo
        {
            Id = $"federation:{actorId}:{sourceId:N}",
            Name = $"Remote @ {hostname}",
            Type = MediaSourceType.Default,
            Protocol = MediaProtocol.Http,
            Path = tokenResponse.StreamUrl,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = false
        });
    }

    internal static string BuildOpenToken(int actorId, Guid sourceId)
        => $"{actorId.ToString(CultureInfo.InvariantCulture)}{OpenTokenSeparator}{sourceId:N}";

    internal static bool TryParseOpenToken(string openToken, out int actorId, out Guid sourceId)
    {
        actorId = 0;
        sourceId = Guid.Empty;
        if (string.IsNullOrEmpty(openToken))
        {
            return false;
        }

        var parts = openToken.Split(OpenTokenSeparator, 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out actorId))
        {
            return false;
        }

        return Guid.TryParseExact(parts[1], "N", out sourceId);
    }

    private static string GetHostname(string actorUrl)
        => Uri.TryCreate(actorUrl, UriKind.Absolute, out var uri) ? uri.Host : actorUrl;

    private sealed record RemoteTokenResponse(string Token, DateTime ExpiresAt, string StreamUrl);
}
