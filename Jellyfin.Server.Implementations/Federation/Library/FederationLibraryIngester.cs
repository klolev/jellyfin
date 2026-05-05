using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Federation.Library;

/// <inheritdoc/>
public class FederationLibraryIngester : IFederationLibraryIngester
{
    private const string ExternalIdPrefix = "ap://";
    private const string FederatedRootExternalId = ExternalIdPrefix + "federated-root";

    private readonly ILibraryManager _libraryManager;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly ILogger<FederationLibraryIngester> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationLibraryIngester"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="logger">The logger.</param>
    public FederationLibraryIngester(
        ILibraryManager libraryManager,
        IDbContextFactory<JellyfinDbContext> dbProvider,
        ILogger<FederationLibraryIngester> logger)
    {
        _libraryManager = libraryManager;
        _dbProvider = dbProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task IngestAsync(ActivityStreams.Activity activity, CancellationToken cancellationToken = default)
    {
        // Upstream (FederationService.HandleInboxActivityAsync) already verified activity.Actor
        // is non-null and matches the signing actor. We only need to decide whether we follow them.
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var sourceActor = await dbContext.GetFollowedActorAsync(activity.Actor, cancellationToken).ConfigureAwait(false);
            if (sourceActor is null)
            {
                _logger.LogWarning("Dropping activity from non-followed actor {Actor}", activity.Actor);
                return;
            }

            switch (activity.Type)
            {
                case nameof(Create):
                case nameof(Update):
                    await HandleCreateOrUpdateAsync(activity, sourceActor, dbContext, cancellationToken).ConfigureAwait(false);
                    break;
                case nameof(Delete):
                    await HandleDeleteAsync(activity, sourceActor, dbContext, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    _logger.LogDebug("Ignoring activity type {Type} from {Actor}", activity.Type, activity.Actor);
                    break;
            }
        }
    }

    /// <summary>
    /// Handles both <c>Create</c> and <c>Update</c> activities as idempotent upserts. Federation
    /// is eventually-consistent: peers legitimately replay Creates (retries after delivery
    /// failures) and may deliver Updates out of order with their originating Create. Treating the
    /// two verbs as the same upsert keeps end state correct regardless of delivery sequence,
    /// matching how Mastodon/Pleroma/etc. handle inbox activities.
    /// </summary>
    private async Task HandleCreateOrUpdateAsync(ActivityStreams.Activity activity, FederationActor sourceActor, JellyfinDbContext db, CancellationToken cancellationToken)
    {
        var videoObject = activity.Object;
        if (videoObject is null || videoObject.Type != nameof(ActivityStreams.Video) || string.IsNullOrEmpty(videoObject.Id))
        {
            _logger.LogDebug("Dropping {Type} from {Actor}: object is not a Video", activity.Type, activity.Actor);
            return;
        }

        var mediaType = videoObject.Tag?.FirstOrDefault(t => t.Name == "MediaType")?.Content;
        if (!string.Equals(mediaType, "Movie", StringComparison.Ordinal))
        {
            _logger.LogDebug("Dropping {Type} from {Actor}: MediaType={MediaType}, only Movie is supported", activity.Type, activity.Actor, mediaType);
            return;
        }

        var sourceId = ExtractSourceId(videoObject.Id, sourceActor.Url);
        if (sourceId is null)
        {
            _logger.LogWarning("Dropping {Type} from {Actor}: cannot extract SourceId from {Id}", activity.Type, activity.Actor, videoObject.Id);
            return;
        }

        var expectedExternalId = ExternalIdPrefix + videoObject.Id;

        var existingRow = await db.FederationIngestedItems
            .FirstOrDefaultAsync(r => r.ActorId == sourceActor.Id && r.SourceId.Equals(sourceId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (existingRow is not null && _libraryManager.GetItemById(existingRow.BaseItemId) is Movie existingMovie)
        {
            // Deduped row pointing at a local or other-actor-owned BaseItem — leave the canonical
            // metadata alone so federated peers can't overwrite data they don't own.
            if (!string.Equals(existingMovie.ExternalId, expectedExternalId, StringComparison.Ordinal))
            {
                return;
            }

            ApplyMovieMetadata(existingMovie, videoObject);
            var parent = _libraryManager.GetItemById(existingMovie.ParentId)
                ?? throw new InvalidOperationException(
                    $"Cannot update ingested Movie {existingMovie.Id} ({existingMovie.Name}): its parent folder {existingMovie.ParentId} is missing. "
                    + "The federated folder hierarchy may have been deleted externally. Disable and re-enable federation for this actor to rebuild the folder structure.");
            await _libraryManager.UpdateItemAsync(existingMovie, parent, ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (existingRow is not null)
        {
            // Orphaned row: we have a FederationIngestedItem row but the BaseItem it references is
            // either gone (deleted externally via library scan / admin action) or is no longer a
            // Movie (retyped). Drop the stale row and fall through to the create path so the item
            // gets re-ingested as a fresh Movie.
            db.FederationIngestedItems.Remove(existingRow);
        }

        // Dedup against any existing BaseItem (local or previously-ingested from another actor)
        // that shares a provider id (TMDB, IMDB, etc.). If found, we add a FederationIngestedItem
        // row for this (ActorId, SourceId) pointing at the shared BaseItem — no new Movie created.
        var dedupTarget = FindExistingItemByProviderIds(videoObject);
        if (dedupTarget is not null)
        {
            _logger.LogInformation(
                "Deduped ingested item {Name} from {Actor} against existing BaseItem {Id}",
                videoObject.Name,
                sourceActor.Url,
                dedupTarget.Id);
            await db.FederationIngestedItems
                .AddAsync(new FederationIngestedItem(dedupTarget.Id, sourceActor.Id, sourceId.Value), cancellationToken)
                .ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var actorFolder = EnsureActorFolder(sourceActor);
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            ExternalId = expectedExternalId,
            IsVirtualItem = true,
            ParentId = actorFolder.Id
        };
        ApplyMovieMetadata(movie, videoObject);

        _libraryManager.CreateItem(movie, actorFolder);

        await db.FederationIngestedItems
            .AddAsync(new FederationIngestedItem(movie.Id, sourceActor.Id, sourceId.Value), cancellationToken)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Ingested Movie {Name} from {Actor}", movie.Name, sourceActor.Url);
    }

    private BaseItem? FindExistingItemByProviderIds(ActivityStreams.Object videoObject)
    {
        var providerIds = ExtractProviderIds(videoObject);
        if (providerIds.Count == 0)
        {
            return null;
        }

        var query = new InternalItemsQuery
        {
            HasAnyProviderId = providerIds,
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            Limit = 1
        };

        var matches = _libraryManager.GetItemList(query);
        return matches.Count > 0 ? matches[0] : null;
    }

    private static Dictionary<string, string> ExtractProviderIds(ActivityStreams.Object video)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (video.Tag is null)
        {
            return result;
        }

        foreach (var tag in video.Tag)
        {
            if (tag.Type != "PropertyValue" || string.IsNullOrEmpty(tag.Name) || string.IsNullOrEmpty(tag.Content))
            {
                continue;
            }

            if (string.Equals(tag.Name, "MediaType", StringComparison.Ordinal) || string.Equals(tag.Name, "Year", StringComparison.Ordinal))
            {
                continue;
            }

            result[tag.Name] = tag.Content;
        }

        return result;
    }

    private async Task HandleDeleteAsync(ActivityStreams.Activity activity, FederationActor sourceActor, JellyfinDbContext db, CancellationToken cancellationToken)
    {
        var tombstone = activity.Object;
        if (tombstone is null || tombstone.Type != nameof(ActivityStreams.Tombstone) || string.IsNullOrEmpty(tombstone.Id))
        {
            _logger.LogDebug("Dropping Delete from {Actor}: object is not a Tombstone", activity.Actor);
            return;
        }

        var sourceId = ExtractSourceId(tombstone.Id, sourceActor.Url);
        if (sourceId is null)
        {
            _logger.LogWarning("Dropping Delete from {Actor}: cannot extract SourceId from {Id}", activity.Actor, tombstone.Id);
            return;
        }

        var row = await db.FederationIngestedItems
            .FirstOrDefaultAsync(r => r.ActorId == sourceActor.Id && r.SourceId.Equals(sourceId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            _logger.LogDebug("Delete from {Actor} targets unknown item {Id}", activity.Actor, tombstone.Id);
            return;
        }

        db.FederationIngestedItems.Remove(row);
        _logger.LogInformation("Deleted ingested item from {Actor}", sourceActor.Url);

        // Decide whether to also delete the BaseItem. Two guards, either failing keeps it:
        //   1. Other peers still advertise this BaseItem → they need it.
        //   2. BaseItem is local or reconciled (not ap://) → user's canonical data, off-limits.
        var sharedBaseItemId = row.BaseItemId;
        var removedRowId = row.Id;
        var otherRowsReferenceBaseItem = await db.FederationIngestedItems
            .AnyAsync(r => r.BaseItemId.Equals(sharedBaseItemId) && r.Id != removedRowId, cancellationToken)
            .ConfigureAwait(false);
        if (otherRowsReferenceBaseItem)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = _libraryManager.GetItemById(row.BaseItemId);
        if (existing is null || existing.ExternalId is null || !existing.ExternalId.StartsWith(ExternalIdPrefix, StringComparison.Ordinal))
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _libraryManager.DeleteItem(existing, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Folder EnsureActorFolder(FederationActor sourceActor)
    {
        var root = EnsureFederatedRoot();

        var actorExternalId = ExternalIdPrefix + sourceActor.Url;
        var actorFolder = _libraryManager
            .QueryItems(new InternalItemsQuery { ExternalId = actorExternalId })
            .Items
            .OfType<Folder>()
            .FirstOrDefault();
        if (actorFolder is not null)
        {
            return actorFolder;
        }

        actorFolder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = GetHostname(sourceActor.Url),
            ExternalId = actorExternalId,
            IsVirtualItem = true,
            ParentId = root.Id
        };
        _libraryManager.CreateItem(actorFolder, root);
        return actorFolder;
    }

    private Folder EnsureFederatedRoot()
    {
        var root = _libraryManager
            .QueryItems(new InternalItemsQuery { ExternalId = FederatedRootExternalId })
            .Items
            .OfType<Folder>()
            .FirstOrDefault();
        if (root is not null)
        {
            return root;
        }

        root = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Federated",
            ExternalId = FederatedRootExternalId,
            IsVirtualItem = true
        };
        _libraryManager.CreateItem(root, null);
        return root;
    }

    private static void ApplyMovieMetadata(Movie movie, ActivityStreams.Object video)
    {
        movie.Name = video.Name ?? movie.Name ?? string.Empty;
        movie.Overview = video.Content;
        movie.ProductionYear = ExtractYear(video.Tag);
        movie.RunTimeTicks = ParseIsoDuration(video.Duration);

        movie.ProviderIds.Clear();
        foreach (var (key, value) in ExtractProviderIds(video))
        {
            movie.ProviderIds[key] = value;
        }
    }

    private static int? ExtractYear(ActivityStreams.Object[]? tags)
    {
        var yearTag = tags?.FirstOrDefault(t => t.Name == "Year")?.Content;
        if (string.IsNullOrEmpty(yearTag))
        {
            return null;
        }

        return int.TryParse(yearTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static long? ParseIsoDuration(string? iso)
    {
        if (string.IsNullOrEmpty(iso))
        {
            return null;
        }

        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(iso).Ticks;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Guid? ExtractSourceId(string objectId, string actorUrl)
    {
        var prefix = actorUrl.TrimEnd('/') + "/Items/";
        if (!objectId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var tail = objectId[prefix.Length..];
        return Guid.TryParse(tail, out var parsed) ? parsed : null;
    }

    private static string GetHostname(string actorUrl)
    {
        return Uri.TryCreate(actorUrl, UriKind.Absolute, out var uri) ? uri.Host : actorUrl;
    }
}
