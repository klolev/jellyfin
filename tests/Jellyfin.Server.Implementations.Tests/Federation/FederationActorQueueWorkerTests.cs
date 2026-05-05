using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Server.Implementations.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Controller.Federation.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationActorQueueWorkerTests : IDisposable
{
    private const string ActorUrl = "https://peer.example/Federation/Actor";
    private const string ActorInbox = "https://peer.example/Federation/Inbox";
    private const string ActorOutbox = "https://peer.example/Federation/Outbox";
    private const string ActorPublicKey = "-----BEGIN PUBLIC KEY-----\nfake\n-----END PUBLIC KEY-----";

    private readonly Mock<IFederationSigningService> _signingMock;
    private readonly Mock<IConfigurationManager> _configManagerMock;
    private readonly Mock<IFederationBackfillService> _backfillMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly string _dbPath;

    public FederationActorQueueWorkerTests()
    {
        _signingMock = new Mock<IFederationSigningService>();
        _signingMock
            .Setup(s => s.SignAsync(It.IsAny<HttpRequestMessage>()))
            .Returns<HttpRequestMessage>(r => Task.FromResult(r));

        _configManagerMock = new Mock<IConfigurationManager>();
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(new FederationConfiguration { Enabled = true, Hostname = "test.example", ActorName = "jellyfin" });

        _backfillMock = new Mock<IFederationBackfillService>();

        _httpHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        _dbPath = $"federation-queue-worker-test-{Guid.NewGuid()}.db";
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

    [Fact]
    public async Task Worker_WhenFederationDisabled_ExitsImmediately()
    {
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(new FederationConfiguration { Enabled = false });

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // No HTTP calls should have been made
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ProcessQueue_DeliverItem_Success_RemovesItemAndResetsBackoff()
    {
        SetupHttpResponse(HttpStatusCode.Accepted);
        var queueId = await SeedQueueWithDeliverItemAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var db = CreateDbContext();
        var remainingItems = await db.FederationActorQueueItems.CountAsync();
        Assert.Equal(0, remainingItems);

        var queue = await db.FederationActorQueues.FirstAsync();
        Assert.Equal(0, queue.AttemptCount);
    }

    [Fact]
    public async Task ProcessQueue_DeliverItem_Failure_BumpsBackoffAndLeavesItem()
    {
        SetupHttpResponse(HttpStatusCode.InternalServerError);
        var queueId = await SeedQueueWithDeliverItemAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var db = CreateDbContext();
        var remainingItems = await db.FederationActorQueueItems.CountAsync();
        Assert.Equal(1, remainingItems);

        var queue = await db.FederationActorQueues.FirstAsync();
        Assert.Equal(1, queue.AttemptCount);
        Assert.True(queue.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ProcessQueue_DeliverItem_HttpException_BumpsBackoff()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await SeedQueueWithDeliverItemAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var db = CreateDbContext();
        Assert.Equal(1, await db.FederationActorQueueItems.CountAsync());

        var queue = await db.FederationActorQueues.FirstAsync();
        Assert.Equal(1, queue.AttemptCount);
    }

    [Fact]
    public async Task ProcessQueue_MultipleItems_ProcessesInOrder_StopsOnFailure()
    {
        var callCount = 0;
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.Accepted)
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            });

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            var queue = new FederationActorQueue(actor.Id);
            await db.FederationActorQueues.AddAsync(queue);
            await db.SaveChangesAsync();

            // Two items: first will succeed, second will fail
            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Deliver, ActorInbox, "{\"type\":\"Create\",\"id\":\"1\"}"));
            await db.SaveChangesAsync();
            await Task.Delay(10); // ensure different DateCreated
            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Deliver, ActorInbox, "{\"type\":\"Create\",\"id\":\"2\"}"));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var dbCheck = CreateDbContext();
        // First item delivered successfully, second remains
        var remaining = await dbCheck.FederationActorQueueItems.ToListAsync();
        Assert.Single(remaining);
        Assert.Contains("\"id\":\"2\"", remaining[0].Body, StringComparison.Ordinal);

        var queueState = await dbCheck.FederationActorQueues.FirstAsync();
        Assert.Equal(1, queueState.AttemptCount);
    }

    [Fact]
    public async Task ProcessQueue_FetchItem_Success_CallsBackfillAndRemovesItem()
    {
        var fetchUrl = "https://peer.example/Federation/Outbox?page=0&limit=20";
        var responseBody = "{\"type\":\"OrderedCollectionPage\",\"orderedItems\":[]}";

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            var queue = new FederationActorQueue(actor.Id);
            await db.FederationActorQueues.AddAsync(queue);
            await db.SaveChangesAsync();

            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Fetch, fetchUrl, null));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var dbCheck = CreateDbContext();
        Assert.Equal(0, await dbCheck.FederationActorQueueItems.CountAsync());

        _backfillMock.Verify(
            b => b.HandleOutboxResponseAsync(It.IsAny<FederationActor>(), responseBody, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueue_FetchItem_Failure_BumpsBackoff()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden));

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            var queue = new FederationActorQueue(actor.Id);
            await db.FederationActorQueues.AddAsync(queue);
            await db.SaveChangesAsync();

            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Fetch, "https://peer.example/outbox", null));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var dbCheck = CreateDbContext();
        Assert.Equal(1, await dbCheck.FederationActorQueueItems.CountAsync());
        var fetchQueue = await dbCheck.FederationActorQueues.FirstAsync();
        Assert.Equal(1, fetchQueue.AttemptCount);
    }

    [Fact]
    public async Task ProcessQueue_QueueWithFutureNextAttempt_IsSkipped()
    {
        SetupHttpResponse(HttpStatusCode.Accepted);

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            var queue = new FederationActorQueue(actor.Id)
            {
                NextAttemptAt = DateTime.UtcNow.AddHours(1),
                AttemptCount = 3
            };
            await db.FederationActorQueues.AddAsync(queue);
            await db.SaveChangesAsync();

            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Deliver, ActorInbox, "{\"type\":\"Create\"}"));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        // Item should still be there — the queue's NextAttemptAt is in the future
        await using var dbCheck = CreateDbContext();
        Assert.Equal(1, await dbCheck.FederationActorQueueItems.CountAsync());

        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ProcessQueue_EmptyQueue_ResetsAttemptCount()
    {
        SetupHttpResponse(HttpStatusCode.Accepted);

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            // Queue has a prior failure state but no items left
            var emptyQueue = new FederationActorQueue(actor.Id)
            {
                AttemptCount = 5,
                NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
            };
            await db.FederationActorQueues.AddAsync(emptyQueue);
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var dbCheck = CreateDbContext();
        var queue = await dbCheck.FederationActorQueues.FirstAsync();
        Assert.Equal(0, queue.AttemptCount);
    }

    [Fact]
    public async Task ProcessQueue_IndependentActorQueues_FailureInOneDoesNotBlockOther()
    {
        var callCount = 0;
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                callCount++;
                // First actor's inbox fails, second actor's succeeds
                if (req.RequestUri?.Host == "failing-peer.example")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return new HttpResponseMessage(HttpStatusCode.Accepted);
            });

        await using (var db = CreateDbContext())
        {
            var failingActor = new FederationActor(
                "https://failing-peer.example/actor",
                "https://failing-peer.example/inbox",
                "https://failing-peer.example/outbox",
                ActorPublicKey);
            var succeedingActor = new FederationActor(
                "https://good-peer.example/actor",
                "https://good-peer.example/inbox",
                "https://good-peer.example/outbox",
                ActorPublicKey);
            await db.FederationActors.AddRangeAsync(failingActor, succeedingActor);
            await db.SaveChangesAsync();

            var failQueue = new FederationActorQueue(failingActor.Id);
            var goodQueue = new FederationActorQueue(succeedingActor.Id);
            await db.FederationActorQueues.AddRangeAsync(failQueue, goodQueue);
            await db.SaveChangesAsync();

            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(failQueue.Id, FederationActorQueueItemKind.Deliver, "https://failing-peer.example/inbox", "{\"type\":\"Create\"}"));
            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(goodQueue.Id, FederationActorQueueItemKind.Deliver, "https://good-peer.example/inbox", "{\"type\":\"Create\"}"));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        await using var dbCheck = CreateDbContext();
        var items = await dbCheck.FederationActorQueueItems.Include(i => i.Queue).ThenInclude(q => q.Actor).ToListAsync();

        // Good peer's item was delivered and removed
        Assert.DoesNotContain(items, i => i.Queue.Actor.Url == "https://good-peer.example/actor");

        // Failing peer's item remains
        Assert.Contains(items, i => i.Queue.Actor.Url == "https://failing-peer.example/actor");
    }

    [Fact]
    public void ComputeBackoff_ExponentialGrowth_CappedAt24Hours()
    {
        // Use reflection to test the static private method
        var method = typeof(FederationActorQueueWorker).GetMethod(
            "ComputeBackoff",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var backoff1 = (TimeSpan)method.Invoke(null, new object[] { 1 })!;
        var backoff2 = (TimeSpan)method.Invoke(null, new object[] { 2 })!;
        var backoff3 = (TimeSpan)method.Invoke(null, new object[] { 3 })!;
        var backoff20 = (TimeSpan)method.Invoke(null, new object[] { 20 })!;

        // 1m, 2m, 4m, ...
        Assert.Equal(TimeSpan.FromMinutes(1), backoff1);
        Assert.Equal(TimeSpan.FromMinutes(2), backoff2);
        Assert.Equal(TimeSpan.FromMinutes(4), backoff3);

        // Capped at 24h
        Assert.Equal(TimeSpan.FromHours(24), backoff20);
    }

    [Fact]
    public async Task ProcessQueue_DeliverItem_SignsRequest()
    {
        SetupHttpResponse(HttpStatusCode.Accepted);
        await SeedQueueWithDeliverItemAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        _signingMock.Verify(
            s => s.SignAsync(It.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post)),
            Times.Once);
    }

    [Fact]
    public async Task ProcessQueue_FetchItem_SignsRequest()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });

        await using (var db = CreateDbContext())
        {
            var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
            await db.FederationActors.AddAsync(actor);
            await db.SaveChangesAsync();

            var queue = new FederationActorQueue(actor.Id);
            await db.FederationActorQueues.AddAsync(queue);
            await db.SaveChangesAsync();

            await db.FederationActorQueueItems.AddAsync(
                new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Fetch, ActorOutbox, null));
            await db.SaveChangesAsync();
        }

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        _signingMock.Verify(
            s => s.SignAsync(It.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get)),
            Times.Once);
    }

    private FederationActorQueueWorker CreateWorker()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(_httpHandlerMock.Object));

        return new FederationActorQueueWorker(
            _dbFactory,
            httpClientFactory.Object,
            _signingMock.Object,
            _configManagerMock.Object,
            _backfillMock.Object,
            NullLogger<FederationActorQueueWorker>.Instance);
    }

    private async Task<int> SeedQueueWithDeliverItemAsync()
    {
        await using var db = CreateDbContext();
        var actor = new FederationActor(ActorUrl, ActorInbox, ActorOutbox, ActorPublicKey);
        await db.FederationActors.AddAsync(actor);
        await db.SaveChangesAsync();

        var queue = new FederationActorQueue(actor.Id);
        await db.FederationActorQueues.AddAsync(queue);
        await db.SaveChangesAsync();

        await db.FederationActorQueueItems.AddAsync(
            new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Deliver, ActorInbox, "{\"type\":\"Create\",\"actor\":\"https://test.example/Federation/Actor\"}"));
        await db.SaveChangesAsync();

        return queue.Id;
    }

    private void SetupHttpResponse(HttpStatusCode statusCode)
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));
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
