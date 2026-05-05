using System;
using System.Buffers.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Server.Implementations.Federation.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationStreamTokenServiceTests : IDisposable
{
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly FederationStreamTokenService _sut;
    private readonly int _followerId;

    public FederationStreamTokenServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source=federation-token-test-{Guid.NewGuid()}.db")
            .Options;

        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var initDb = CreateDbContext())
        {
            initDb.Database.EnsureCreated();

            var actor = new FederationActor("https://peer.example/actor", "https://peer.example/inbox", "https://peer.example/outbox", "pubkey");
            initDb.FederationActors.Add(actor);
            initDb.SaveChanges();

            var follower = new FederationFollower(actor.Id);
            initDb.FederationFollowers.Add(follower);
            initDb.SaveChanges();

            _followerId = follower.Id;
        }

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _sut = new FederationStreamTokenService(_dbFactory);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task IssueAsync_PersistsHashNotRawToken()
    {
        var itemId = Guid.NewGuid();
        var raw = await _sut.IssueAsync(_followerId, itemId, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(raw));

        var rawBytes = Base64Url.DecodeFromChars(raw.AsSpan());
        var expectedHash = SHA256.HashData(rawBytes);

        await using var db = CreateDbContext();
        var record = await db.FederationStreamTokens.SingleAsync();
        Assert.Equal(_followerId, record.FollowerId);
        Assert.Equal(itemId, record.ItemId);
        Assert.Equal(expectedHash, record.TokenHash);

        // The raw token string is never persisted (only its hash).
        var allHashes = await db.FederationStreamTokens.Select(t => t.TokenHash).ToListAsync();
        Assert.DoesNotContain(allHashes, h => h.SequenceEqual(rawBytes));
    }

    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsFollowerId()
    {
        var itemId = Guid.NewGuid();
        var raw = await _sut.IssueAsync(_followerId, itemId, TimeSpan.FromHours(1), CancellationToken.None);

        var result = await _sut.ValidateAsync(raw, itemId, CancellationToken.None);

        Assert.Equal(_followerId, result);
    }

    [Fact]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        var unknown = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        var result = await _sut.ValidateAsync(unknown, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsNull()
    {
        var itemId = Guid.NewGuid();
        var raw = await _sut.IssueAsync(_followerId, itemId, TimeSpan.FromMilliseconds(-1), CancellationToken.None);

        var result = await _sut.ValidateAsync(raw, itemId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WrongItemId_ReturnsNull()
    {
        var issuedFor = Guid.NewGuid();
        var queriedAgainst = Guid.NewGuid();
        var raw = await _sut.IssueAsync(_followerId, issuedFor, TimeSpan.FromHours(1), CancellationToken.None);

        var result = await _sut.ValidateAsync(raw, queriedAgainst, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_MalformedToken_ReturnsNull()
    {
        var result = await _sut.ValidateAsync("not-a-valid-base64url-token!!!", Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_EmptyToken_ReturnsNull()
    {
        var result = await _sut.ValidateAsync(string.Empty, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WrongLengthBase64_ReturnsNull()
    {
        // Valid base64url encoding, but wrong length (16 bytes instead of 32).
        var tooShort = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

        var result = await _sut.ValidateAsync(tooShort, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IssueAsync_ReplacesExistingTokenForSameFollowerAndItem()
    {
        var itemId = Guid.NewGuid();
        var oldToken = await _sut.IssueAsync(_followerId, itemId, TimeSpan.FromHours(1), CancellationToken.None);
        var newToken = await _sut.IssueAsync(_followerId, itemId, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.NotEqual(oldToken, newToken);

        Assert.Null(await _sut.ValidateAsync(oldToken, itemId, CancellationToken.None));
        Assert.Equal(_followerId, await _sut.ValidateAsync(newToken, itemId, CancellationToken.None));

        await using var db = CreateDbContext();
        var count = await db.FederationStreamTokens.CountAsync(t => t.FollowerId == _followerId && t.ItemId.Equals(itemId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IssueAsync_DifferentItemsSameFollower_KeepsBothTokens()
    {
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();
        var tokenA = await _sut.IssueAsync(_followerId, itemA, TimeSpan.FromHours(1), CancellationToken.None);
        var tokenB = await _sut.IssueAsync(_followerId, itemB, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(_followerId, await _sut.ValidateAsync(tokenA, itemA, CancellationToken.None));
        Assert.Equal(_followerId, await _sut.ValidateAsync(tokenB, itemB, CancellationToken.None));

        await using var db = CreateDbContext();
        Assert.Equal(2, await db.FederationStreamTokens.CountAsync());
    }

    [Fact]
    public async Task DeleteExpiredAsync_RemovesExpiredKeepsValid()
    {
        var validItem = Guid.NewGuid();
        var expiredItem = Guid.NewGuid();

        await _sut.IssueAsync(_followerId, validItem, TimeSpan.FromHours(1), CancellationToken.None);
        await _sut.IssueAsync(_followerId, expiredItem, TimeSpan.FromMilliseconds(-1), CancellationToken.None);

        var removed = await _sut.DeleteExpiredAsync(CancellationToken.None);
        Assert.Equal(1, removed);

        await using var db = CreateDbContext();
        var remaining = await db.FederationStreamTokens.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(validItem, remaining[0].ItemId);
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }
}
