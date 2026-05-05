using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Server.Implementations.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationLibraryPublisherTests : IAsyncLifetime, IDisposable
{
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<IConfigurationManager> _configManagerMock;
    private readonly FederationConfiguration _config;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly FederationLibraryPublisher _sut;

    public FederationLibraryPublisherTests()
    {
        _libraryManagerMock = new Mock<ILibraryManager>();

        _config = new FederationConfiguration
        {
            Enabled = true,
            Hostname = "test.example",
            ActorName = "jellyfin"
        };

        _configManagerMock = new Mock<IConfigurationManager>();
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(_config);

        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source=federation-publisher-test-{Guid.NewGuid()}.db")
            .Options;

        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        // Ensure DB is created
        using var initDb = CreateDbContext();
        initDb.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _sut = new FederationLibraryPublisher(
            _libraryManagerMock.Object,
            _dbFactory,
            _configManagerMock.Object,
            NullLogger<FederationLibraryPublisher>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _sut.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public async Task ItemAdded_Movie_PublishesToOutbox()
    {
        var movie = CreateMovie("The Matrix", "tt0133093");
        RaiseItemAdded(movie);

        // Allow async handler to complete
        await Task.Delay(100);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("The Matrix", activity.ActivityJson, StringComparison.Ordinal);
        Assert.Contains("tt0133093", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemAdded_Episode_DoesNotPublish()
    {
        // Episodes are out of scope for MVP — the ingester only handles Movies, so the publisher
        // shouldn't broadcast Episodes either (followers would drop them on arrival).
        var episode = CreateEpisode("Pilot");
        RaiseItemAdded(episode);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationOutboxActivities.CountAsync());
    }

    [Fact]
    public async Task ItemAdded_Series_DoesNotPublish()
    {
        var series = new Series { Name = "Breaking Bad", Id = Guid.NewGuid() };
        RaiseItemAdded(series);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ItemAdded_Season_DoesNotPublish()
    {
        var season = new Season { Name = "Season 1", Id = Guid.NewGuid() };
        RaiseItemAdded(season);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ItemAdded_Audio_DoesNotPublish()
    {
        var audio = new Audio { Name = "Some Song", Id = Guid.NewGuid() };
        RaiseItemAdded(audio);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ItemRemoved_Movie_PublishesDeleteToOutbox()
    {
        var movie = CreateMovie("Old Movie", "tt9999999");
        RaiseItemRemoved(movie);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("Delete", activity.ActivityJson, StringComparison.Ordinal);
        Assert.Contains("Tombstone", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemUpdated_MetadataDownload_PublishesToOutbox()
    {
        var movie = CreateMovie("Updated Movie", "tt1111111");
        RaiseItemUpdated(movie, ItemUpdateType.MetadataDownload);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("Updated Movie", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemUpdated_MetadataEdit_PublishesToOutbox()
    {
        var movie = CreateMovie("Edited Movie", "tt2222222");
        RaiseItemUpdated(movie, ItemUpdateType.MetadataEdit);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("Edited Movie", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemUpdated_ImageUpdate_DoesNotPublish()
    {
        var movie = CreateMovie("Image Movie", "tt3333333");
        RaiseItemUpdated(movie, ItemUpdateType.ImageUpdate);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ItemAdded_WhenDisabled_DoesNotPublish()
    {
        _config.Enabled = false;

        var movie = CreateMovie("Disabled Movie", "tt4444444");
        RaiseItemAdded(movie);

        await Task.Delay(100);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ItemAdded_WithFollowers_QueuesDelivery()
    {
        // Seed a follower
        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor("https://remote.example/actor", "https://remote.example/inbox", "https://remote.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();
            await db.FederationFollowers.AddAsync(new FederationFollower(actor.Id));
            await db.SaveChangesAsync();
        }

        var movie = CreateMovie("Queued Movie", "tt5555555");
        RaiseItemAdded(movie);

        await Task.Delay(100);

        await using var dbCheck = CreateDbContext();
        var item = await dbCheck.FederationActorQueueItems.FirstOrDefaultAsync();
        Assert.NotNull(item);
        Assert.Equal(FederationActorQueueItemKind.Deliver, item.Kind);
        Assert.NotNull(item.Body);
        Assert.Contains("Queued Movie", item.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemAdded_PreviouslyIngestedFromPeer_DoesNotPublish()
    {
        var movie = CreateMovie("Remote Movie", "tt6666666");
        // Virtual federated items carry an ap:// ExternalId; that's how the publisher recognises
        // them as ingested and skips re-publication.
        movie.ExternalId = "ap://peer.example/Items/abc";

        // Seed a follower (so there'd be someone to deliver to) and mark this BaseItem as ingested
        await using (var db = CreateDbContext())
        {
            var peer = new FederationActor("https://peer.example/actor", "https://peer.example/inbox", "https://peer.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(peer);
            await db.SaveChangesAsync();
            await db.FederationFollowers.AddAsync(new FederationFollower(peer.Id));
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(movie.Id, peer.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        RaiseItemAdded(movie);
        RaiseItemUpdated(movie, ItemUpdateType.MetadataDownload);
        RaiseItemRemoved(movie);

        await Task.Delay(100);

        await using var dbCheck = CreateDbContext();
        Assert.Equal(0, await dbCheck.FederationOutboxActivities.CountAsync());
        Assert.Equal(0, await dbCheck.FederationActorQueueItems.CountAsync());
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new Jellyfin.Database.Implementations.Locking.NoLockBehavior(NullLogger<Jellyfin.Database.Implementations.Locking.NoLockBehavior>.Instance));
    }

    private static Movie CreateMovie(string name, string imdbId)
    {
        var movie = new Movie
        {
            Name = name,
            Id = Guid.NewGuid(),
            ProductionYear = 1999,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Imdb", imdbId }
            }
        };
        return movie;
    }

    private static Episode CreateEpisode(string name)
    {
        return new Episode
        {
            Name = name,
            Id = Guid.NewGuid()
        };
    }

    private void RaiseItemAdded(BaseItem item)
    {
        _libraryManagerMock.Raise(m => m.ItemAdded += null, _libraryManagerMock.Object, new ItemChangeEventArgs { Item = item });
    }

    private void RaiseItemRemoved(BaseItem item)
    {
        _libraryManagerMock.Raise(m => m.ItemRemoved += null, _libraryManagerMock.Object, new ItemChangeEventArgs { Item = item });
    }

    private void RaiseItemUpdated(BaseItem item, ItemUpdateType updateType)
    {
        _libraryManagerMock.Raise(m => m.ItemUpdated += null, _libraryManagerMock.Object, new ItemChangeEventArgs { Item = item, UpdateReason = updateType });
    }

    // ====================================================================
    // Reconciliation: when a local item is added that shares provider IDs
    // with an existing virtual ap:// federated item, the local one wins —
    // FederationIngestedItem rows re-point to the local, the virtual is
    // deleted. Then (still) a Create activity is published to followers.
    // ====================================================================

    [Fact]
    public async Task ItemAdded_LocalItem_WithMatchingVirtual_ReconcilesAndDeletesVirtual()
    {
        var virtualMovie = CreateMovie("The Matrix", "tt0133093");
        virtualMovie.ExternalId = "ap://peer-a.example/Items/abc";
        var virtualId = virtualMovie.Id;

        var localMovie = CreateMovie("The Matrix", "tt0133093");
        var localId = localMovie.Id;

        // Seed a FederationIngestedItem row pointing at the virtual.
        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor("https://peer-a.example/actor", "https://peer-a.example/inbox", "https://peer-a.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualId, actor.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        // Library mock: provider-id query returns the virtual (+ the local, which the reconciler
        // filters out because c.Id == localItem.Id).
        _libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId != null && q.HasAnyProviderId.Count > 0)))
            .Returns(new List<BaseItem> { virtualMovie, localMovie });

        var deletedItems = new List<BaseItem>();
        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => deletedItems.Add(item));

        RaiseItemAdded(localMovie);
        await Task.Delay(150);

        // Virtual was deleted.
        Assert.Contains(deletedItems, d => d.Id.Equals(virtualId));

        // FederationIngestedItem row re-pointed to local.
        await using var dbCheck = CreateDbContext();
        var row = await dbCheck.FederationIngestedItems.SingleAsync();
        Assert.Equal(localId, row.BaseItemId);

        // Create activity still published — the outbox has exactly one entry.
        Assert.Equal(1, await dbCheck.FederationOutboxActivities.CountAsync());
    }

    [Fact]
    public async Task ItemAdded_LocalItem_NoVirtualMatch_DoesNotDeleteOrRepoint()
    {
        var localMovie = CreateMovie("Fresh Movie", "tt9999999");

        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { localMovie });

        var deletedItems = new List<BaseItem>();
        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => deletedItems.Add(item));

        RaiseItemAdded(localMovie);
        await Task.Delay(150);

        Assert.Empty(deletedItems);

        // Create still published.
        await using var dbCheck = CreateDbContext();
        Assert.Equal(1, await dbCheck.FederationOutboxActivities.CountAsync());
    }

    [Fact]
    public async Task ItemAdded_VirtualItem_DoesNotReconcileSelfOrPublish()
    {
        // A virtual ap:// item was just created by the ingester. The resulting ItemAdded event
        // must not treat itself as a local item to reconcile against.
        var virtualMovie = CreateMovie("Ingested", "tt5555555");
        virtualMovie.ExternalId = "ap://peer.example/Items/xyz";
        virtualMovie.IsVirtualItem = true;

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor("https://peer.example/actor", "https://peer.example/inbox", "https://peer.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualMovie.Id, actor.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        var deletedItems = new List<BaseItem>();
        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => deletedItems.Add(item));

        RaiseItemAdded(virtualMovie);
        await Task.Delay(150);

        // Nothing deleted, nothing published (IsIngestedItemAsync short-circuits the publish path).
        Assert.Empty(deletedItems);
        await using var dbCheck = CreateDbContext();
        Assert.Equal(0, await dbCheck.FederationOutboxActivities.CountAsync());
    }

    [Fact]
    public async Task ItemAdded_LocalItem_MultiplePeersAdvertisedSame_ReconcilesAllVirtuals()
    {
        var virtualA = CreateMovie("The Matrix", "tt0133093");
        virtualA.ExternalId = "ap://peer-a.example/Items/aaa";

        var virtualB = CreateMovie("The Matrix", "tt0133093");
        virtualB.ExternalId = "ap://peer-b.example/Items/bbb";

        var localMovie = CreateMovie("The Matrix", "tt0133093");
        var localId = localMovie.Id;

        await using (var db = CreateDbContext())
        {
            var peerA = new FederationActor("https://peer-a.example/actor", "https://peer-a.example/inbox", "https://peer-a.example/outbox", "pubkey-a");
            var peerB = new FederationActor("https://peer-b.example/actor", "https://peer-b.example/inbox", "https://peer-b.example/outbox", "pubkey-b");
            await db.FederationActors.AddRangeAsync(peerA, peerB);
            await db.SaveChangesAsync();
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualA.Id, peerA.Id, Guid.NewGuid()));
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualB.Id, peerB.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { virtualA, virtualB, localMovie });

        var deletedItems = new List<BaseItem>();
        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => deletedItems.Add(item));

        RaiseItemAdded(localMovie);
        await Task.Delay(150);

        // Both virtuals deleted.
        Assert.Equal(2, deletedItems.Count);
        Assert.Contains(deletedItems, d => d.Id.Equals(virtualA.Id));
        Assert.Contains(deletedItems, d => d.Id.Equals(virtualB.Id));

        // Both rows re-pointed to local — local now appears twice (once per peer).
        await using var dbCheck = CreateDbContext();
        var rows = await dbCheck.FederationIngestedItems.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(localId, r.BaseItemId));
    }

    // ====================================================================
    // Concurrent adds / race conditions with virtual items
    // ====================================================================

    [Fact]
    public async Task ConcurrentAdds_SameProviderIds_BothPublishWithoutCrash()
    {
        // Two local movies added nearly simultaneously with the same IMDB ID. Both should
        // reconcile and publish without throwing. The reconciler should tolerate the library
        // query returning both (or neither) virtuals since by the time the second runs the
        // virtual may already be gone.
        var movieA = CreateMovie("The Matrix", "tt0133093");
        var movieB = CreateMovie("The Matrix", "tt0133093");

        // No virtuals to reconcile — just ensure concurrent ItemAdded doesn't deadlock or crash.
        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        RaiseItemAdded(movieA);
        RaiseItemAdded(movieB);

        await Task.Delay(300);

        await using var db = CreateDbContext();
        var count = await db.FederationOutboxActivities.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ConcurrentAdd_LocalAndVirtual_LocalWins_VirtualNotPublished()
    {
        // Simulates a race: a peer's ingested item creates a virtual, and simultaneously
        // the user's library scan adds the same movie locally. The virtual gets an ItemAdded
        // event too, but it should be suppressed (ap:// prefix detection).
        var virtualMovie = CreateMovie("Inception", "tt1375666");
        virtualMovie.ExternalId = "ap://peer.example/Items/inception";

        var localMovie = CreateMovie("Inception", "tt1375666");
        var localId = localMovie.Id;

        await using (var db = CreateDbContext())
        {
            var peer = new FederationActor("https://peer.example/actor", "https://peer.example/inbox", "https://peer.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(peer);
            await db.SaveChangesAsync();
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualMovie.Id, peer.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        _libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId != null && q.HasAnyProviderId.Count > 0)))
            .Returns(new List<BaseItem> { virtualMovie, localMovie });

        var deletedItems = new List<BaseItem>();
        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => deletedItems.Add(item));

        // Fire both events concurrently — virtual add and local add
        RaiseItemAdded(virtualMovie);
        RaiseItemAdded(localMovie);

        await Task.Delay(300);

        // Virtual's add should not have published (IsVirtualFederatedItem returns true).
        // Local's add should have published exactly once.
        await using var db2 = CreateDbContext();
        var activities = await db2.FederationOutboxActivities.ToListAsync();
        Assert.Single(activities);
        Assert.Contains("Inception", activities[0].ActivityJson, StringComparison.Ordinal);

        // Virtual was reconciled away by the local item add.
        Assert.Contains(deletedItems, d => d.Id.Equals(virtualMovie.Id));

        // FederationIngestedItem re-pointed to local.
        var row = await db2.FederationIngestedItems.SingleAsync();
        Assert.Equal(localId, row.BaseItemId);
    }

    [Fact]
    public async Task ConcurrentAdds_MultipleLocalSameProviderIds_NoExceptionOnDoubleReconcile()
    {
        // Edge case: two local items with the same provider IDs are added concurrently
        // (e.g. a library scan picks up the same file from two paths briefly). Both will
        // try to reconcile against the same virtual. The second one should find the virtual
        // already gone and gracefully continue.
        var virtualMovie = CreateMovie("Dune", "tt1160419");
        virtualMovie.ExternalId = "ap://peer.example/Items/dune";

        var localA = CreateMovie("Dune", "tt1160419");
        var localB = CreateMovie("Dune", "tt1160419");

        await using (var db = CreateDbContext())
        {
            var peer = new FederationActor("https://peer.example/actor", "https://peer.example/inbox", "https://peer.example/outbox", "pubkey");
            await db.FederationActors.AddAsync(peer);
            await db.SaveChangesAsync();
            await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(virtualMovie.Id, peer.Id, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        var callCount = 0;
        _libraryManagerMock
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId != null && q.HasAnyProviderId.Count > 0)))
            .Returns(() =>
            {
                // First call sees the virtual; second call doesn't (already deleted).
                Interlocked.Increment(ref callCount);
                return callCount == 1
                    ? new List<BaseItem> { virtualMovie, localA, localB }
                    : new List<BaseItem> { localA, localB };
            });

        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()));

        // Fire both adds
        RaiseItemAdded(localA);
        RaiseItemAdded(localB);

        await Task.Delay(300);

        // Both should publish without crashing. At least 2 outbox entries.
        await using var db2 = CreateDbContext();
        var count = await db2.FederationOutboxActivities.CountAsync();
        Assert.True(count >= 2, $"Expected at least 2 outbox activities, got {count}");
    }

    [Fact]
    public async Task ItemAdded_LocalWithNoProviderIds_SkipsReconcileStillPublishes()
    {
        // Items without provider IDs can't match any virtual items. Reconcile should be
        // a no-op, but the Create activity should still be published.
        var movie = new Movie
        {
            Name = "Home Video",
            Id = Guid.NewGuid(),
            ProductionYear = 2024,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        // GetItemList should never be called since ProviderIds is empty
        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Throws(new InvalidOperationException("Should not be called"));

        RaiseItemAdded(movie);
        await Task.Delay(150);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("Home Video", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcileFailure_DoesNotPreventPublish()
    {
        // If reconciliation throws (e.g. DB transient error), the Create activity should
        // still be published. The publisher wraps reconcile in its own try/catch.
        var movie = CreateMovie("Resilient Movie", "tt7777777");

        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Throws(new InvalidOperationException("Simulated DB failure"));

        RaiseItemAdded(movie);
        await Task.Delay(150);

        await using var db = CreateDbContext();
        var activity = await db.FederationOutboxActivities.FirstOrDefaultAsync();
        Assert.NotNull(activity);
        Assert.Contains("Resilient Movie", activity.ActivityJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingPublish()
    {
        // After StopAsync, no new events should be processed. We verify by stopping the
        // publisher, then raising an event and checking that nothing was published.
        await _sut.StopAsync(CancellationToken.None);

        var movie = CreateMovie("Late Movie", "tt8888888");
        _libraryManagerMock.Raise(m => m.ItemAdded += null, _libraryManagerMock.Object, new ItemChangeEventArgs { Item = movie });

        await Task.Delay(150);

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationOutboxActivities.CountAsync());

        // Re-start for DisposeAsync
        await _sut.StartAsync(CancellationToken.None);
    }
}
