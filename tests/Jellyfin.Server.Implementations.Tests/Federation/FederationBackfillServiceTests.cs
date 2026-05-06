using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Server.Implementations.Federation.Peers;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationBackfillServiceTests : IDisposable
{
    private const string PeerUrl = "https://peer.example/actor";
    private const string OutboxUrl = "https://peer.example/outbox";

    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly Mock<IFederationLibraryIngester> _ingesterMock;
    private readonly List<ActivityStreams.Activity> _ingested;
    private readonly FederationBackfillService _sut;
    private readonly FederationActor _actor;

    public FederationBackfillServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source=federation-backfill-test-{Guid.NewGuid()}.db")
            .Options;

        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var initDb = CreateDbContext())
        {
            initDb.Database.EnsureCreated();
            var actor = new FederationActor(PeerUrl, PeerUrl + "/inbox", OutboxUrl, "pubkey");
            initDb.FederationActors.Add(actor);
            initDb.SaveChanges();
            _actor = actor;
        }

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _ingesterMock = new Mock<IFederationLibraryIngester>();
        _ingested = new List<ActivityStreams.Activity>();
        _ingesterMock
            .Setup(i => i.IngestAsync(It.IsAny<ActivityStreams.Activity>(), It.IsAny<CancellationToken>()))
            .Returns<ActivityStreams.Activity, CancellationToken>((a, _) =>
            {
                _ingested.Add(a);
                return Task.CompletedTask;
            });

        _sut = new FederationBackfillService(
            _dbFactory,
            _ingesterMock.Object,
            NullLogger<FederationBackfillService>.Instance);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task BackfillFromAsync_EnqueuesFetchItemForOutboxUrl()
    {
        await _sut.BackfillFromAsync(_actor);

        await using var db = CreateDbContext();
        var items = await db.FederationActorQueueItems.ToListAsync();
        Assert.Single(items);
        Assert.Equal(FederationActorQueueItemKind.Fetch, items[0].Kind);
        Assert.Equal(OutboxUrl, items[0].TargetUrl);
        Assert.Null(items[0].Body);
        Assert.Empty(_ingested);
    }

    [Fact]
    public async Task HandleOutboxResponseAsync_RootWithFirst_EnqueuesFirstPage()
    {
        var body = JsonOf(new
        {
            type = "OrderedCollection",
            first = OutboxUrl + "?page=0"
        });

        await _sut.HandleOutboxResponseAsync(_actor, body, CancellationToken.None);

        await using var db = CreateDbContext();
        var items = await db.FederationActorQueueItems.ToListAsync();
        Assert.Single(items);
        Assert.Equal(OutboxUrl + "?page=0", items[0].TargetUrl);
        Assert.Empty(_ingested);
    }

    [Fact]
    public async Task HandleOutboxResponseAsync_PageWithNext_IngestsAndEnqueuesNext()
    {
        var body = JsonOf(new
        {
            type = "OrderedCollectionPage",
            orderedItems = new[]
            {
                BuildCreateActivityJson("item-a"),
                BuildCreateActivityJson("item-b")
            },
            next = OutboxUrl + "?page=1"
        });

        await _sut.HandleOutboxResponseAsync(_actor, body, CancellationToken.None);

        Assert.Equal(2, _ingested.Count);
        Assert.Equal(new[] { "item-a", "item-b" }, new[] { _ingested[0].Id, _ingested[1].Id });

        await using var db = CreateDbContext();
        var items = await db.FederationActorQueueItems.ToListAsync();
        Assert.Single(items);
        Assert.Equal(OutboxUrl + "?page=1", items[0].TargetUrl);
    }

    [Fact]
    public async Task HandleOutboxResponseAsync_PageWithoutNext_DoesNotEnqueue()
    {
        var body = JsonOf(new
        {
            type = "OrderedCollectionPage",
            orderedItems = new[] { BuildCreateActivityJson("item-a") }
        });

        await _sut.HandleOutboxResponseAsync(_actor, body, CancellationToken.None);

        Assert.Single(_ingested);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationActorQueueItems.CountAsync());
    }

    [Fact]
    public async Task HandleOutboxResponseAsync_MalformedJson_NoIngestNoEnqueue()
    {
        await _sut.HandleOutboxResponseAsync(_actor, "{ this is not valid }", CancellationToken.None);

        Assert.Empty(_ingested);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationActorQueueItems.CountAsync());
    }

    [Fact]
    public async Task HandleOutboxResponseAsync_IngesterThrowsOnOne_ContinuesOthersAndStillEnqueuesNext()
    {
        _ingesterMock.Reset();
        _ingesterMock
            .Setup(i => i.IngestAsync(It.Is<ActivityStreams.Activity>(a => a.Id == "bad"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _ingesterMock
            .Setup(i => i.IngestAsync(It.Is<ActivityStreams.Activity>(a => a.Id == "good"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var body = JsonOf(new
        {
            type = "OrderedCollectionPage",
            orderedItems = new[]
            {
                BuildCreateActivityJson("bad"),
                BuildCreateActivityJson("good")
            },
            next = OutboxUrl + "?page=2"
        });

        await _sut.HandleOutboxResponseAsync(_actor, body, CancellationToken.None);

        _ingesterMock.Verify(i => i.IngestAsync(It.Is<ActivityStreams.Activity>(a => a.Id == "good"), It.IsAny<CancellationToken>()), Times.Once);

        await using var db = CreateDbContext();
        Assert.Single(await db.FederationActorQueueItems.ToListAsync());
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }

    private static string JsonOf(object obj)
        => JsonSerializer.Serialize(obj);

    private static object BuildCreateActivityJson(string id)
    {
        return new
        {
            type = "Create",
            id,
            actor = PeerUrl,
            @object = new
            {
                type = "Video",
                id = OutboxUrl + "/Items/" + id
            }
        };
    }
}
