using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Server.Implementations.Federation.Peers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Controller.Federation.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationPeerServiceTests : IDisposable
{
    private const string PeerUrl = "https://peer.example/Federation/Actor";
    private const string PeerInbox = "https://peer.example/Federation/Inbox";
    private const string PeerOutbox = "https://peer.example/Federation/Outbox";
    private const string PeerPublicKey = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhki\n-----END PUBLIC KEY-----";

    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IFederationSigningService> _signingMock;
    private readonly Mock<IConfigurationManager> _configManagerMock;
    private readonly Mock<IFederationBackfillService> _backfillMock;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly FederationPeerService _sut;
    private readonly string _dbPath;

    public FederationPeerServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _signingMock = new Mock<IFederationSigningService>();
        _signingMock
            .Setup(s => s.SignAsync(It.IsAny<HttpRequestMessage>()))
            .Returns<HttpRequestMessage>(r => Task.FromResult(r));

        _configManagerMock = new Mock<IConfigurationManager>();
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(new FederationConfiguration { Enabled = true, Hostname = "test.example", ActorName = "jellyfin" });

        _backfillMock = new Mock<IFederationBackfillService>();

        _dbPath = $"federation-peer-test-{Guid.NewGuid()}.db";
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var initDb = CreateDbContext())
        {
            initDb.Database.EnsureCreated();
        }

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _sut = new FederationPeerService(
            _dbFactory,
            _httpClientFactoryMock.Object,
            _signingMock.Object,
            _configManagerMock.Object,
            _backfillMock.Object,
            NullLogger<FederationPeerService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            System.IO.File.Delete(_dbPath);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    // --- HandleFollowRequestAsync ---

    [Fact]
    public async Task HandleFollowRequest_CreatesRequest()
    {
        await SeedActorAsync(PeerUrl);

        await _sut.HandleFollowRequestAsync(PeerUrl);

        await using var db = CreateDbContext();
        var request = await db.FederationFollowRequests
            .Include(r => r.Actor)
            .FirstOrDefaultAsync(r => r.Actor.Url == PeerUrl && r.Type == FederationFollowRequestType.Follower);
        Assert.NotNull(request);
        Assert.False(request.Responded);
    }

    [Fact]
    public async Task HandleFollowRequest_Duplicate_DoesNotCreateSecondRequest()
    {
        await SeedActorAsync(PeerUrl);

        await _sut.HandleFollowRequestAsync(PeerUrl);
        await _sut.HandleFollowRequestAsync(PeerUrl);

        await using var db = CreateDbContext();
        var count = await db.FederationFollowRequests
            .Include(r => r.Actor)
            .CountAsync(r => r.Actor.Url == PeerUrl && r.Type == FederationFollowRequestType.Follower);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HandleFollowRequest_UnknownActor_DoesNothing()
    {
        await _sut.HandleFollowRequestAsync("https://unknown.example/actor");

        await using var db = CreateDbContext();
        var count = await db.FederationFollowRequests.CountAsync();
        Assert.Equal(0, count);
    }

    // --- VetFollowerAsync ---

    [Fact]
    public async Task VetFollower_Accept_CreatesFollowerAndQueuesAccept()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Follower);

        var result = await _sut.VetFollowerAsync(PeerUrl, accept: true);

        Assert.Equal(VetFollowRequestResult.Success, result);

        await using var db = CreateDbContext();
        var follower = await db.FederationFollowers
            .Include(f => f.Actor)
            .FirstOrDefaultAsync(f => f.Actor.Url == PeerUrl);
        Assert.NotNull(follower);

        var queueItem = await db.FederationActorQueueItems.FirstOrDefaultAsync();
        Assert.NotNull(queueItem);
        Assert.Contains("Accept", queueItem.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VetFollower_Reject_QueuesRejectAndDoesNotCreateFollower()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Follower);

        var result = await _sut.VetFollowerAsync(PeerUrl, accept: false);

        Assert.Equal(VetFollowRequestResult.Success, result);

        await using var db = CreateDbContext();
        var follower = await db.FederationFollowers
            .Include(f => f.Actor)
            .FirstOrDefaultAsync(f => f.Actor.Url == PeerUrl);
        Assert.Null(follower);

        var queueItem = await db.FederationActorQueueItems.FirstOrDefaultAsync();
        Assert.NotNull(queueItem);
        Assert.Contains("Reject", queueItem.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VetFollower_NoPendingRequest_ReturnsNoPendingRequest()
    {
        var result = await _sut.VetFollowerAsync(PeerUrl, accept: true);

        Assert.Equal(VetFollowRequestResult.NoPendingRequest, result);
    }

    [Fact]
    public async Task VetFollower_AlreadyResponded_ReturnsNoPendingRequest()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Follower, responded: true);

        var result = await _sut.VetFollowerAsync(PeerUrl, accept: true);

        Assert.Equal(VetFollowRequestResult.NoPendingRequest, result);
    }

    // --- HandleFollowingAcceptedAsync ---

    [Fact]
    public async Task HandleFollowingAccepted_TriggersBackfill()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        var backfillCalled = new TaskCompletionSource<FederationActor>(TaskCreationOptions.RunContinuationsAsynchronously);
        _backfillMock
            .Setup(b => b.BackfillFromAsync(It.IsAny<FederationActor>(), It.IsAny<CancellationToken>()))
            .Returns<FederationActor, CancellationToken>((actor, _) =>
            {
                backfillCalled.TrySetResult(actor);
                return Task.CompletedTask;
            });

        await _sut.HandleFollowingAcceptedAsync(PeerUrl);

        var completed = await Task.WhenAny(backfillCalled.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(backfillCalled.Task, completed);
        var actorCalledWith = await backfillCalled.Task;
        Assert.Equal(PeerUrl, actorCalledWith.Url);
    }

    [Fact]
    public async Task HandleFollowingAccepted_CreatesFollowing()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        await _sut.HandleFollowingAcceptedAsync(PeerUrl);

        await using var db = CreateDbContext();
        var following = await db.FederationFollowings
            .Include(f => f.Actor)
            .FirstOrDefaultAsync(f => f.Actor.Url == PeerUrl);
        Assert.NotNull(following);
    }

    [Fact]
    public async Task HandleFollowingAccepted_MarksRequestAsResponded()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        await _sut.HandleFollowingAcceptedAsync(PeerUrl);

        await using var db = CreateDbContext();
        var request = await db.FederationFollowRequests
            .FirstOrDefaultAsync(r => r.ActorId == actorId && r.Type == FederationFollowRequestType.Following);
        Assert.NotNull(request);
        Assert.True(request.Responded);
    }

    [Fact]
    public async Task HandleFollowingAccepted_NoPendingRequest_DoesNotTriggerBackfill()
    {
        await _sut.HandleFollowingAcceptedAsync("https://unknown.example/actor");

        await Task.Delay(100);
        _backfillMock.Verify(b => b.BackfillFromAsync(It.IsAny<FederationActor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- HandleFollowingRejectedAsync ---

    [Fact]
    public async Task HandleFollowingRejected_MarksRequestAsResponded()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        await _sut.HandleFollowingRejectedAsync(PeerUrl);

        await using var db = CreateDbContext();
        var request = await db.FederationFollowRequests
            .FirstOrDefaultAsync(r => r.ActorId == actorId && r.Type == FederationFollowRequestType.Following);
        Assert.NotNull(request);
        Assert.True(request.Responded);
    }

    [Fact]
    public async Task HandleFollowingRejected_DoesNotCreateFollowing()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        await _sut.HandleFollowingRejectedAsync(PeerUrl);

        await using var db = CreateDbContext();
        var following = await db.FederationFollowings.AnyAsync();
        Assert.False(following);
    }

    [Fact]
    public async Task HandleFollowingRejected_NoPendingRequest_DoesNothing()
    {
        await _sut.HandleFollowingRejectedAsync("https://unknown.example/actor");

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationFollowRequests.CountAsync());
    }

    // --- RemoveFollowerAsync ---

    [Fact]
    public async Task RemoveFollower_ExistingFollower_ReturnsTrue()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowerAsync(actorId);

        var result = await _sut.RemoveFollowerAsync(PeerUrl);

        Assert.True(result);

        await using var db = CreateDbContext();
        var exists = await db.FederationFollowers.AnyAsync();
        Assert.False(exists);
    }

    [Fact]
    public async Task RemoveFollower_NotAFollower_ReturnsFalse()
    {
        var result = await _sut.RemoveFollowerAsync(PeerUrl);

        Assert.False(result);
    }

    // --- RemoveFollowingAsync ---

    [Fact]
    public async Task RemoveFollowing_ExistingFollowing_ReturnsTrueAndQueuesUndo()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowingAsync(actorId);

        var result = await _sut.RemoveFollowingAsync(PeerUrl);

        Assert.True(result);

        await using var db = CreateDbContext();
        var exists = await db.FederationFollowings.AnyAsync();
        Assert.False(exists);

        var queueItem = await db.FederationActorQueueItems.FirstOrDefaultAsync();
        Assert.NotNull(queueItem);
        Assert.Contains("Undo", queueItem.Body, StringComparison.Ordinal);
        Assert.Contains("Follow", queueItem.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveFollowing_NotFollowing_ReturnsFalse()
    {
        var result = await _sut.RemoveFollowingAsync(PeerUrl);

        Assert.False(result);
    }

    // --- GetFollowersAsync / GetFollowingAsync ---

    [Fact]
    public async Task GetFollowers_ReturnsFollowers()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowerAsync(actorId);

        var followers = await _sut.GetFollowersAsync();

        Assert.Single(followers);
        Assert.Equal(PeerUrl, followers[0].Url);
    }

    [Fact]
    public async Task GetFollowing_ReturnsFollowing()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowingAsync(actorId);

        var following = await _sut.GetFollowingAsync();

        Assert.Single(following);
        Assert.Equal(PeerUrl, following[0].Url);
    }

    // --- GetFollowerRequestsAsync / GetFollowingRequestsAsync ---

    [Fact]
    public async Task GetFollowerRequests_ReturnsPendingOnly()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Follower);

        // Second actor with a responded request — should not appear
        var actorId2 = await SeedActorAsync("https://other.example/Federation/Actor");
        await SeedFollowRequestAsync(actorId2, FederationFollowRequestType.Follower, responded: true);

        var requests = await _sut.GetFollowerRequestsAsync();

        Assert.Single(requests);
        Assert.Equal(PeerUrl, requests[0].ActorUrl);
    }

    [Fact]
    public async Task GetFollowingRequests_ReturnsPendingOnly()
    {
        var actorId = await SeedActorAsync(PeerUrl);
        await SeedFollowRequestAsync(actorId, FederationFollowRequestType.Following);

        var requests = await _sut.GetFollowingRequestsAsync();

        Assert.Single(requests);
        Assert.Equal(PeerUrl, requests[0].ActorUrl);
    }

    // --- Helpers ---

    private async Task<int> SeedActorAsync(string url)
    {
        await using var db = CreateDbContext();
        var actor = new FederationActor(url, PeerInbox, PeerOutbox, PeerPublicKey);
        await db.FederationActors.AddAsync(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    private async Task SeedFollowRequestAsync(int actorId, FederationFollowRequestType type, bool responded = false)
    {
        await using var db = CreateDbContext();
        var request = new FederationFollowRequest(actorId, type);
        if (responded)
        {
            request.Responded = true;
        }

        await db.FederationFollowRequests.AddAsync(request);
        await db.SaveChangesAsync();
    }

    private async Task SeedFollowerAsync(int actorId)
    {
        await using var db = CreateDbContext();
        await db.FederationFollowers.AddAsync(new FederationFollower(actorId));
        await db.SaveChangesAsync();
    }

    private async Task SeedFollowingAsync(int actorId)
    {
        await using var db = CreateDbContext();
        await db.FederationFollowings.AddAsync(new FederationFollowing(actorId));
        await db.SaveChangesAsync();
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new Jellyfin.Database.Implementations.Locking.NoLockBehavior(NullLogger<Jellyfin.Database.Implementations.Locking.NoLockBehavior>.Instance));
    }
}
