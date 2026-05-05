using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Server.Implementations.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationServiceTests : IDisposable
{
    private const string SignerUrl = "https://peer.example/Federation/Actor";

    private readonly Mock<IFederationPeerService> _peerServiceMock;
    private readonly Mock<IFederationLibraryIngester> _ingesterMock;
    private readonly Mock<IConfigurationManager> _configManagerMock;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly FederationService _sut;
    private readonly string _dbPath;

    public FederationServiceTests()
    {
        _peerServiceMock = new Mock<IFederationPeerService>();
        _ingesterMock = new Mock<IFederationLibraryIngester>();
        _configManagerMock = new Mock<IConfigurationManager>();
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(new FederationConfiguration { Enabled = true, Hostname = "test.example", ActorName = "jellyfin" });

        _dbPath = $"federation-service-test-{Guid.NewGuid()}.db";
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

        _sut = new FederationService(
            _dbFactory,
            _peerServiceMock.Object,
            _ingesterMock.Object,
            _configManagerMock.Object,
            NullLogger<FederationService>.Instance);
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

    // --- Actor mismatch rejection ---

    [Fact]
    public async Task HandleInbox_ActorMismatch_DropsActivity()
    {
        var activity = new Follow
        {
            Actor = "https://evil.example/actor",
            Object = new ActivityStreams.Object { Id = "https://test.example/Federation/Actor" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.HandleFollowRequestAsync(It.IsAny<string>()), Times.Never);
    }

    // --- Follow ---

    [Fact]
    public async Task HandleInbox_Follow_DelegatesToPeerService()
    {
        var activity = new Follow
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Id = "https://test.example/Federation/Actor" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.HandleFollowRequestAsync(SignerUrl), Times.Once);
    }

    // --- Accept ---

    [Fact]
    public async Task HandleInbox_Accept_DelegatesToPeerService()
    {
        var activity = new Accept
        {
            Actor = SignerUrl,
            Object = new Follow { Actor = "https://test.example/Federation/Actor", Object = new ActivityStreams.Object { Id = SignerUrl } }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.HandleFollowingAcceptedAsync(SignerUrl), Times.Once);
    }

    // --- Reject ---

    [Fact]
    public async Task HandleInbox_Reject_DelegatesToPeerService()
    {
        var activity = new Reject
        {
            Actor = SignerUrl,
            Object = new Follow { Actor = "https://test.example/Federation/Actor", Object = new ActivityStreams.Object { Id = SignerUrl } }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.HandleFollowingRejectedAsync(SignerUrl), Times.Once);
    }

    // --- Undo{Follow} ---

    [Fact]
    public async Task HandleInbox_UndoFollow_RemovesFollower()
    {
        var activity = new Undo
        {
            Actor = SignerUrl,
            Object = new Follow
            {
                Actor = SignerUrl,
                Object = new ActivityStreams.Object { Id = "https://test.example/Federation/Actor" }
            }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.RemoveFollowerAsync(SignerUrl), Times.Once);
    }

    [Fact]
    public async Task HandleInbox_UndoFollow_InnerActorMismatch_DropsActivity()
    {
        var activity = new Undo
        {
            Actor = SignerUrl,
            Object = new Follow
            {
                Actor = "https://other.example/actor",
                Object = new ActivityStreams.Object { Id = "https://test.example/Federation/Actor" }
            }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.RemoveFollowerAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleInbox_UndoNonFollow_Ignored()
    {
        var activity = new Undo
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Type = "Like", Id = "https://peer.example/like/1" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _peerServiceMock.Verify(p => p.RemoveFollowerAsync(It.IsAny<string>()), Times.Never);
    }

    // --- Create/Update/Delete ---

    [Fact]
    public async Task HandleInbox_Create_DelegatesToIngester()
    {
        var activity = new Create
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Type = "Video", Id = $"{SignerUrl}/Items/{Guid.NewGuid():N}" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _ingesterMock.Verify(i => i.IngestAsync(activity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleInbox_Update_DelegatesToIngester()
    {
        var activity = new Update
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Type = "Video", Id = $"{SignerUrl}/Items/{Guid.NewGuid():N}" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _ingesterMock.Verify(i => i.IngestAsync(activity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleInbox_Delete_DelegatesToIngester()
    {
        var activity = new Delete
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Type = "Tombstone", Id = $"{SignerUrl}/Items/{Guid.NewGuid():N}" }
        };

        await _sut.HandleInboxActivityAsync(activity, SignerUrl);

        _ingesterMock.Verify(i => i.IngestAsync(activity, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Non-Activity object ---

    [Fact]
    public async Task HandleInbox_NonActivity_DoesNothing()
    {
        var obj = new ActivityStreams.Object { Id = "https://peer.example/thing/1" };

        await _sut.HandleInboxActivityAsync(obj, SignerUrl);

        _peerServiceMock.VerifyNoOtherCalls();
        _ingesterMock.VerifyNoOtherCalls();
    }

    // --- Handler exception propagates ---

    [Fact]
    public async Task HandleInbox_HandlerThrows_ExceptionPropagates()
    {
        _peerServiceMock
            .Setup(p => p.HandleFollowRequestAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        var activity = new Follow
        {
            Actor = SignerUrl,
            Object = new ActivityStreams.Object { Id = "https://test.example/Federation/Actor" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HandleInboxActivityAsync(activity, SignerUrl));
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
