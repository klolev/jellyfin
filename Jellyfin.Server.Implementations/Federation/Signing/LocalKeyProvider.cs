using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Model.Federation.Peers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation.Signing;

/// <summary>
/// Signature-key provider for inbound HTTP signatures. Serves cached actor public keys from the
/// local <see cref="FederationActor"/> table; on an unknown actor, lazily fetches the actor
/// document (with timeout + shape validation) and stores it before returning the key. This is
/// what lets us validate the very first Follow from a previously-unknown peer — no signature
/// bypass needed.
/// </summary>
public class LocalKeyProvider : IHTTPSignatureKeyProvider
{
    private static readonly TimeSpan ActorFetchTimeout = TimeSpan.FromSeconds(10);

    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocalKeyProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalKeyProvider"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public LocalKeyProvider(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<LocalKeyProvider> logger)
    {
        _dbProvider = dbProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> GetPublicKey(string actorUrl)
    {
        // Fast path: already known locally.
        var fastPathContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (fastPathContext.ConfigureAwait(false))
        {
            var known = await fastPathContext.FederationActors
                .FirstOrDefaultAsync(a => a.Url == actorUrl)
                .ConfigureAwait(false);
            if (known is not null)
            {
                return known.PublicKey;
            }
        }

        if (!IsAllowedUrl(actorUrl))
        {
            _logger.LogWarning("Rejecting actor URL {ActorUrl}: must be HTTPS with a non-empty host", actorUrl);
            return null;
        }

        if (!await ResolvesToPublicAddressAsync(actorUrl).ConfigureAwait(false))
        {
            _logger.LogWarning("Rejecting actor URL {ActorUrl}: resolves to a private/loopback address", actorUrl);
            return null;
        }

        // First contact: fetch, validate, store.
        var fetched = await TryFetchActorAsync(actorUrl).ConfigureAwait(false);
        if (fetched is null)
        {
            return null;
        }

        var writeContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (writeContext.ConfigureAwait(false))
        {
            // Another concurrent inbox request may have stored this actor between our fast-path
            // miss and now. Re-check under this context; if present, return the persisted key.
            var existing = await writeContext.FederationActors
                .FirstOrDefaultAsync(a => a.Url == actorUrl)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return existing.PublicKey;
            }

            var actor = new FederationActor(actorUrl, fetched.Inbox, fetched.Outbox, fetched.PublicKey.PublicKeyPem);
            await writeContext.FederationActors.AddAsync(actor).ConfigureAwait(false);
            await writeContext.SaveChangesAsync().ConfigureAwait(false);
            return actor.PublicKey;
        }
    }

    private static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host);
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
        {
            return true;
        }

        return NetworkConstants.IPv4RFC1918PrivateClassA.Contains(address)
            || NetworkConstants.IPv4RFC1918PrivateClassB.Contains(address)
            || NetworkConstants.IPv4RFC1918PrivateClassC.Contains(address)
            || NetworkConstants.IPv4RFC3927LinkLocal.Contains(address)
            || NetworkConstants.IPv6RFC4193UniqueLocal.Contains(address)
            || NetworkConstants.IPv6RFC4291SiteLocal.Contains(address);
    }

    private async Task<bool> ResolvesToPublicAddressAsync(string url)
    {
        var uri = new Uri(url);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return false;
            }

            return !addresses.Any(IsPrivateOrLoopback);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task<ActorResponse?> TryFetchActorAsync(string actorUrl)
    {
        using var cts = new CancellationTokenSource(ActorFetchTimeout);

        try
        {
            using var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Get, actorUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/activity+json"));

            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Actor fetch of {ActorUrl} returned {Status}", actorUrl, (int)response.StatusCode);
                return null;
            }

            var actor = await response.Content.ReadFromJsonAsync<ActorResponse>(cts.Token).ConfigureAwait(false);
            if (actor is null
                || string.IsNullOrEmpty(actor.Inbox)
                || string.IsNullOrEmpty(actor.Outbox)
                || string.IsNullOrEmpty(actor.PublicKey?.PublicKeyPem))
            {
                _logger.LogWarning("Actor {ActorUrl} returned an incomplete document; discarding", actorUrl);
                return null;
            }

            return actor;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("Actor fetch of {ActorUrl} timed out after {Timeout}", actorUrl, ActorFetchTimeout);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Actor fetch of {ActorUrl} failed", actorUrl);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Actor fetch of {ActorUrl} returned invalid JSON", actorUrl);
            return null;
        }
    }
}
