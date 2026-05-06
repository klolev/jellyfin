using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Federation.ActivityStreams;
using MediaBrowser.Model.Federation.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation;

/// <summary>
/// Listens to library changes and publishes them to the federation outbox.
/// </summary>
public sealed class FederationLibraryPublisher : IHostedService, IDisposable
{
    private const string VirtualExternalIdPrefix = "ap://";

    private readonly ILibraryManager _libraryManager;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IConfigurationManager _configManager;
    private readonly ILogger<FederationLibraryPublisher> _logger;
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationLibraryPublisher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public FederationLibraryPublisher(
        ILibraryManager libraryManager,
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IConfigurationManager configManager,
        ILogger<FederationLibraryPublisher> logger)
    {
        _libraryManager = libraryManager;
        _dbProvider = dbProvider;
        _configManager = configManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        await _cts.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _cts.Dispose();
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (!IsFederatableItem(e.Item))
        {
            return;
        }

        var token = _cts.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await HandleLocalItemAddedAsync(e.Item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error publishing federation Create for added item {ItemName}", e.Item.Name);
                }
            },
            token);
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (!IsFederatableItem(e.Item))
        {
            return;
        }

        // Only publish updates for meaningful metadata changes
        if ((e.UpdateReason & (ItemUpdateType.MetadataDownload | ItemUpdateType.MetadataEdit | ItemUpdateType.MetadataImport)) == 0)
        {
            return;
        }

        var token = _cts.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await PublishUpdateAsync(e.Item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error publishing federation Update for {ItemName}", e.Item.Name);
                }
            },
            token);
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (!IsFederatableItem(e.Item))
        {
            return;
        }

        var token = _cts.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await PublishDeleteAsync(e.Item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error publishing federation Delete for {ItemName}", e.Item.Name);
                }
            },
            token);
    }

    /// <summary>
    /// Entry point for a newly-added local library item. First reconciles against any existing
    /// virtual federated items that already advertised the same content (matched by provider IDs
    /// — e.g. TMDB/IMDB), then publishes a Create activity to followers. Reconciliation runs
    /// before publication so followers receive a Create pointing at the canonical (now local)
    /// BaseItem, and their own ingesters dedup accordingly.
    /// </summary>
    private async Task HandleLocalItemAddedAsync(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileWithVirtualAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile local item {ItemName} with virtual federated items", item.Name);
        }

        await PublishCreateAsync(item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// When a new local item shares provider IDs with an existing virtual federated item
    /// (<c>ExternalId</c> prefixed <c>ap://</c>), adopt the local item as the canonical one:
    /// re-point every <see cref="FederationIngestedItem"/> row from the virtual to the local, then
    /// delete the virtual. Local items trump virtuals so the user's library never grows duplicates
    /// when a movie they've added locally was also advertised by a followed peer.
    /// </summary>
    private async Task ReconcileWithVirtualAsync(BaseItem localItem, CancellationToken cancellationToken)
    {
        if (localItem.ProviderIds is null || localItem.ProviderIds.Count == 0)
        {
            return;
        }

        // Skip items that are themselves virtual federated entries (the ingester route). We only
        // want to reconcile genuinely-local items added via a library scan / manual create.
        if (localItem.ExternalId is not null && localItem.ExternalId.StartsWith(VirtualExternalIdPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var query = new InternalItemsQuery
        {
            HasAnyProviderId = localItem.ProviderIds.ToDictionary(kv => kv.Key, kv => kv.Value),
            IncludeItemTypes = new[] { localItem.GetBaseItemKind() }
        };

        var virtuals = _libraryManager.GetItemList(query)
            .Where(c => !c.Id.Equals(localItem.Id)
                && c.ExternalId is not null
                && c.ExternalId.StartsWith(VirtualExternalIdPrefix, StringComparison.Ordinal))
            .ToList();

        if (virtuals.Count == 0)
        {
            return;
        }

        var localItemId = localItem.Id;
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            foreach (var virtualItem in virtuals)
            {
                var virtualId = virtualItem.Id;
                await dbContext.FederationIngestedItems
                    .Where(r => r.BaseItemId.Equals(virtualId))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.BaseItemId, localItemId), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var virtualItem in virtuals)
        {
            _libraryManager.DeleteItem(virtualItem, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
            _logger.LogInformation(
                "Reconciled virtual federated {Kind} {VirtualId} into local {LocalId} via provider-id match",
                virtualItem.GetBaseItemKind(),
                virtualItem.Id,
                localItemId);
        }
    }

    private Task PublishCreateAsync(BaseItem item, CancellationToken cancellationToken)
        => PublishActivityAsync(item, b => b.BuildActivity(), "Create", cancellationToken);

    private Task PublishUpdateAsync(BaseItem item, CancellationToken cancellationToken)
        => PublishActivityAsync(item, b => b.BuildUpdateActivity(), "Update", cancellationToken);

    private Task PublishDeleteAsync(BaseItem item, CancellationToken cancellationToken)
        => PublishActivityAsync(item, b => b.BuildDeleteActivity(), "Delete", cancellationToken);

    private async Task PublishActivityAsync(
        BaseItem item,
        Func<FederationLibraryItemActivityBuilder, object> buildActivity,
        string activityType,
        CancellationToken cancellationToken)
    {
        var config = _configManager.GetFederationConfiguration();
        if (!config.Enabled || string.IsNullOrEmpty(config.Hostname))
        {
            return;
        }

        try
        {
            if (IsVirtualFederatedItem(item))
            {
                return;
            }

            var actorUrl = config.ActorURL;
            var libraryItem = FederationLibraryItemConverter.FromBaseItem(item);
            var builder = new FederationLibraryItemActivityBuilder(libraryItem, actorUrl);
            var activityJson = JsonSerializer.Serialize(buildActivity(builder), ActivityStreamsJsonOptions.Default);

            await PublishToOutboxAsync(activityJson, item.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish federation {ActivityType} for {ItemName}", activityType, item.Name);
        }
    }

    /// <summary>
    /// Virtual items — those created by the ingester — carry an <c>ap://</c> ExternalId so we
    /// can recognise them without a DB lookup. Local items reconciled against a virtual no longer
    /// count as "ingested": their ExternalId is the user's own, even if FederationIngestedItem
    /// rows point at them (those rows now advertise this local as a source to peers).
    /// </summary>
    private static bool IsVirtualFederatedItem(BaseItem item)
        => item.ExternalId is not null && item.ExternalId.StartsWith(VirtualExternalIdPrefix, StringComparison.Ordinal);

    private async Task PublishToOutboxAsync(string activityJson, string itemName, CancellationToken cancellationToken)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Store in the outbox activity log
                await dbContext.FederationOutboxActivities
                    .AddAsync(new FederationOutboxActivity(activityJson), cancellationToken)
                    .ConfigureAwait(false);

                // Queue delivery to every follower's actor queue. We pick up the InboxUrl here so the
                // worker doesn't have to re-resolve it at processing time.
                var followers = await dbContext.FederationFollowers
                    .Include(f => f.Actor)
                    .Select(f => new { f.ActorId, f.Actor.InboxUrl })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var follower in followers)
                {
                    await dbContext.EnqueueDeliverAsync(follower.ActorId, follower.InboxUrl, activityJson, cancellationToken).ConfigureAwait(false);
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Published federation activity for {ItemName}", itemName);
    }

    private static bool IsFederatableItem(BaseItem item)
    {
        // MVP scope: only Movies round-trip through publish + ingest. Episode support would need
        // Show/Season hierarchy reconstruction on the ingester side — left as follow-up.
        return item is Movie;
    }
}
