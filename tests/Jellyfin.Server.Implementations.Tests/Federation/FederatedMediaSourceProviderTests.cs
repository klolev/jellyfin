using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Server.Implementations.Federation.Media;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Model.MediaInfo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederatedMediaSourceProviderTests : IDisposable
{
    private const string PeerAUrl = "https://peer-a.example/actor";
    private const string PeerBUrl = "https://peer-b.example/actor";

    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly Mock<IFederationSigningService> _signingServiceMock;
    private readonly StubHttpMessageHandler _handler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FederatedMediaSourceProvider _sut;
    private readonly int _peerAId;
    private readonly int _peerBId;

    public FederatedMediaSourceProviderTests()
    {
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source=federation-provider-test-{Guid.NewGuid()}.db")
            .Options;

        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var initDb = CreateDbContext())
        {
            initDb.Database.EnsureCreated();

            var peerA = new FederationActor(PeerAUrl, PeerAUrl + "/inbox", PeerAUrl + "/outbox", "pubkey-a");
            var peerB = new FederationActor(PeerBUrl, PeerBUrl + "/inbox", PeerBUrl + "/outbox", "pubkey-b");
            initDb.FederationActors.AddRange(peerA, peerB);
            initDb.SaveChanges();
            _peerAId = peerA.Id;
            _peerBId = peerB.Id;
        }

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _signingServiceMock = new Mock<IFederationSigningService>();
        _signingServiceMock
            .Setup(s => s.SignAsync(It.IsAny<HttpRequestMessage>()))
            .Returns<HttpRequestMessage>(Task.FromResult);

        _handler = new StubHttpMessageHandler();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(_handler, disposeHandler: false));
        _httpClientFactory = httpClientFactoryMock.Object;

        _sut = new FederatedMediaSourceProvider(
            _dbFactory,
            _httpClientFactory,
            _signingServiceMock.Object,
            NullLogger<FederatedMediaSourceProvider>.Instance);
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    [Fact]
    public async Task GetMediaSources_NoRows_ReturnsEmpty()
    {
        var item = new Movie { Id = Guid.NewGuid() };
        var sources = await _sut.GetMediaSources(item, CancellationToken.None);
        Assert.Empty(sources);
    }

    [Fact]
    public async Task GetMediaSources_OneRow_ReturnsOneRemoteSource()
    {
        var itemId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await SeedIngestedRowAsync(itemId, _peerAId, sourceId);

        var item = new Movie { Id = itemId };
        var sources = (await _sut.GetMediaSources(item, CancellationToken.None)).ToList();

        Assert.Single(sources);
        var src = sources[0];
        Assert.True(src.IsRemote);
        Assert.True(src.RequiresOpening);
        Assert.Equal(MediaProtocol.Http, src.Protocol);
        Assert.True(src.SupportsDirectPlay);
        Assert.True(src.SupportsDirectStream);
        Assert.False(src.SupportsTranscoding);
        Assert.Equal("Remote @ peer-a.example", src.Name);
        Assert.NotNull(src.OpenToken);
    }

    [Fact]
    public async Task GetMediaSources_TwoActorsSameItem_ReturnsTwoSources()
    {
        var itemId = Guid.NewGuid();
        await SeedIngestedRowAsync(itemId, _peerAId, Guid.NewGuid());
        await SeedIngestedRowAsync(itemId, _peerBId, Guid.NewGuid());

        var item = new Movie { Id = itemId };
        var sources = (await _sut.GetMediaSources(item, CancellationToken.None)).ToList();

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.Name == "Remote @ peer-a.example");
        Assert.Contains(sources, s => s.Name == "Remote @ peer-b.example");
    }

    [Fact]
    public async Task OpenMediaSource_MalformedToken_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.OpenMediaSource("not-a-token", new List<MediaBrowser.Controller.Library.ILiveStream>(), CancellationToken.None));
    }

    [Fact]
    public async Task OpenMediaSource_UnknownActor_Throws()
    {
        var openToken = FederatedMediaSourceProvider.BuildOpenToken(99999, Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.OpenMediaSource(openToken, new List<MediaBrowser.Controller.Library.ILiveStream>(), CancellationToken.None));
    }

    [Fact]
    public async Task OpenMediaSource_RemoteReturns401_Throws()
    {
        var sourceId = Guid.NewGuid();
        _handler.SetResponse(HttpStatusCode.Unauthorized, responseBody: null);

        var openToken = FederatedMediaSourceProvider.BuildOpenToken(_peerAId, sourceId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.OpenMediaSource(openToken, new List<MediaBrowser.Controller.Library.ILiveStream>(), CancellationToken.None));
    }

    [Fact]
    public async Task OpenMediaSource_HappyPath_ReturnsLiveStreamWithRemotePath()
    {
        var sourceId = Guid.NewGuid();
        var remoteStreamUrl = $"https://peer-a.example/Federation/Media/{sourceId:N}/Stream?token=rawtoken";
        _handler.SetResponse(HttpStatusCode.OK, responseBody: new
        {
            Token = "rawtoken",
            ExpiresAt = DateTime.UtcNow.AddHours(6),
            StreamUrl = remoteStreamUrl
        });

        var openToken = FederatedMediaSourceProvider.BuildOpenToken(_peerAId, sourceId);
        var liveStream = await _sut.OpenMediaSource(openToken, new List<MediaBrowser.Controller.Library.ILiveStream>(), CancellationToken.None);

        Assert.NotNull(liveStream);
        Assert.NotNull(liveStream.MediaSource);
        Assert.Equal(remoteStreamUrl, liveStream.MediaSource.Path);
        Assert.True(liveStream.MediaSource.IsRemote);
        Assert.Equal(MediaProtocol.Http, liveStream.MediaSource.Protocol);

        // Verify the outgoing request was directed at the correct remote endpoint and signed.
        Assert.NotNull(_handler.LastRequest);
        Assert.Equal(HttpMethod.Post, _handler.LastRequest!.Method);
        Assert.Equal($"https://peer-a.example/Federation/Media/{sourceId:N}/Token", _handler.LastRequest.RequestUri!.ToString());
        _signingServiceMock.Verify(s => s.SignAsync(It.IsAny<HttpRequestMessage>()), Times.Once);
    }

    [Fact]
    public void TryParseOpenToken_ParsesValid()
    {
        var sourceId = Guid.NewGuid();
        var token = FederatedMediaSourceProvider.BuildOpenToken(42, sourceId);

        Assert.True(FederatedMediaSourceProvider.TryParseOpenToken(token, out var actorId, out var parsedSourceId));
        Assert.Equal(42, actorId);
        Assert.Equal(sourceId, parsedSourceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodelim")]
    [InlineData("abc:def")]
    [InlineData("42:not-a-guid")]
    public void TryParseOpenToken_RejectsInvalid(string token)
    {
        Assert.False(FederatedMediaSourceProvider.TryParseOpenToken(token, out _, out _));
    }

    private async Task SeedIngestedRowAsync(Guid baseItemId, int actorId, Guid sourceId)
    {
        await using var db = CreateDbContext();
        await db.FederationIngestedItems.AddAsync(new FederationIngestedItem(baseItemId, actorId, sourceId));
        await db.SaveChangesAsync();
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string? _body;

        public HttpRequestMessage? LastRequest { get; private set; }

        public void SetResponse(HttpStatusCode status, object? responseBody)
        {
            _status = status;
            _body = responseBody is null ? null : JsonSerializer.Serialize(responseBody);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_status);
            if (_body is not null)
            {
                response.Content = new StringContent(_body, Encoding.UTF8);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return Task.FromResult(response);
        }
    }
}
