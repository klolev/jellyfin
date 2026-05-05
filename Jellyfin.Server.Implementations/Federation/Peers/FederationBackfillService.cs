using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Federation.Peers;

/// <inheritdoc/>
public class FederationBackfillService : IFederationBackfillService
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IFederationLibraryIngester _ingester;
    private readonly ILogger<FederationBackfillService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationBackfillService"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="ingester">The library ingester.</param>
    /// <param name="logger">The logger.</param>
    public FederationBackfillService(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IFederationLibraryIngester ingester,
        ILogger<FederationBackfillService> logger)
    {
        _dbProvider = dbProvider;
        _ingester = ingester;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task BackfillFromAsync(FederationActor source, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            await dbContext.EnqueueFetchAsync(source.Id, source.OutboxUrl, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Enqueued backfill Fetch for {Actor}", source.Url);
    }

    /// <inheritdoc/>
    public async Task HandleOutboxResponseAsync(FederationActor source, string responseBody, CancellationToken cancellationToken)
    {
        OutboxResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OutboxResponse>(responseBody, ActivityStreamsJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Outbox response from {Actor} was not valid JSON", source.Url);
            return;
        }

        if (parsed is null)
        {
            return;
        }

        // Some servers inline activities on the root OrderedCollection; ingest whatever's here.
        if (parsed.OrderedItems is { Length: > 0 })
        {
            foreach (var activity in parsed.OrderedItems)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await _ingester.IngestAsync(activity, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backfill ingest failed for activity {Id}", activity.Id);
                }
            }
        }

        // Follow pagination: prefer `next` (if we're on a page), else `first` (if we're on the root).
        var nextUrl = !string.IsNullOrEmpty(parsed.Next) ? parsed.Next : parsed.First;
        if (string.IsNullOrEmpty(nextUrl))
        {
            _logger.LogInformation("Backfill from {Actor} reached end of pagination chain", source.Url);
            return;
        }

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            await dbContext.EnqueueFetchAsync(source.Id, nextUrl, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Permissive DTO that handles both OrderedCollection (first/last refs, optional inlined items)
    /// and OrderedCollectionPage (orderedItems + next/prev) shapes.
    /// </summary>
    private sealed class OutboxResponse
    {
        [JsonPropertyName("first")]
        public string? First { get; set; }

        [JsonPropertyName("next")]
        public string? Next { get; set; }

        [JsonPropertyName("orderedItems")]
        public ActivityStreams.Activity[]? OrderedItems { get; set; }
    }
}
