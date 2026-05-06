using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Server.Implementations.Federation.Signing;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Federation.Peers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class LocalKeyProviderTests : IDisposable
{
    private const string KnownActorUrl = "https://known.example/Federation/Actor";
    private const string UnknownActorUrl = "https://unknown.example/Federation/Actor";
    private const string KnownPublicKey = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBg==\n-----END PUBLIC KEY-----";

    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProviderMock;
    private readonly Mock<IDbContextFactory<JellyfinDbContext>> _dbFactoryMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly string _dbPath;

    public LocalKeyProviderTests()
    {
        _dbPath = $"local-key-provider-test-{Guid.NewGuid()}.db";
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _dbProviderMock = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var db = CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.FederationActors.Add(new FederationActor(
                KnownActorUrl,
                "https://known.example/inbox",
                "https://known.example/outbox",
                KnownPublicKey));
            db.SaveChanges();
        }

        _dbFactoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        _dbFactoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    public void Dispose()
    {
        try
        {
            System.IO.File.Delete(_dbPath);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task GetPublicKey_KnownActor_ReturnsCachedKey()
    {
        var sut = CreateSut();

        var key = await sut.GetPublicKey(KnownActorUrl);

        Assert.Equal(KnownPublicKey, key);
    }

    [Theory]
    [InlineData("http://insecure.example/Federation/Actor")]
    [InlineData("ftp://weird.example/Federation/Actor")]
    [InlineData("not-a-url")]
    [InlineData("https://")]
    public async Task GetPublicKey_NonHttpsUrl_ReturnsNull(string url)
    {
        var sut = CreateSut();

        var key = await sut.GetPublicKey(url);

        Assert.Null(key);
    }

    [Fact]
    public async Task GetPublicKey_FetchReturns404_ReturnsNull()
    {
        SetupHttpResponse(HttpStatusCode.NotFound, string.Empty);
        var sut = CreateSut();

        var key = await sut.GetPublicKey(UnknownActorUrl);

        Assert.Null(key);
    }

    [Fact]
    public async Task GetPublicKey_FetchReturnsIncompleteActor_ReturnsNull()
    {
        var incomplete = JsonSerializer.Serialize(new { id = UnknownActorUrl, inbox = "https://unknown.example/inbox" });
        SetupHttpResponse(HttpStatusCode.OK, incomplete);
        var sut = CreateSut();

        var key = await sut.GetPublicKey(UnknownActorUrl);

        Assert.Null(key);
    }

    [Fact]
    public async Task GetPublicKey_FetchReturnsMismatchedId_ReturnsNull()
    {
        var json = SerializeActorJson("https://evil.example/Actor", "https://unknown.example/inbox", "https://unknown.example/outbox", "pem-key");
        SetupHttpResponse(HttpStatusCode.OK, json);
        var sut = CreateSut();

        var key = await sut.GetPublicKey(UnknownActorUrl);

        Assert.Null(key);
    }

    [Fact]
    public async Task GetPublicKey_ValidRemoteActor_StoresAndReturnsKey()
    {
        var remoteKey = "-----BEGIN PUBLIC KEY-----\nREMOTE\n-----END PUBLIC KEY-----";
        var json = SerializeActorJson(UnknownActorUrl, "https://unknown.example/inbox", "https://unknown.example/outbox", remoteKey);
        SetupHttpResponse(HttpStatusCode.OK, json);
        var sut = CreateSut();

        var key = await sut.GetPublicKey(UnknownActorUrl);

        Assert.Equal(remoteKey, key);

        // Verify it's persisted (second call should use fast path)
        var key2 = await sut.GetPublicKey(UnknownActorUrl);
        Assert.Equal(remoteKey, key2);
    }

    private LocalKeyProvider CreateSut()
    {
        return new LocalKeyProvider(
            _dbFactoryMock.Object,
            _httpClientFactoryMock.Object,
            NullLogger<LocalKeyProvider>.Instance);
    }

    private void SetupHttpResponse(HttpStatusCode status, string content)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/activity+json")
            });

        var client = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(NamedClient.Federation))
            .Returns(client);
    }

    private static string SerializeActorJson(string id, string inbox, string outbox, string publicKeyPem)
    {
        return JsonSerializer.Serialize(new
        {
            id,
            inbox,
            outbox,
            publicKey = new { publicKeyPem }
        });
    }

    private JellyfinDbContext CreateDbContext()
        => new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProviderMock,
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
}
