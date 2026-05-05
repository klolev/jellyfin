using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Media;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Server.Integration.Tests;

/// <summary>
/// Integration tests for the federation media mirror endpoints.
/// </summary>
public class FederationMediaTests : IClassFixture<JellyfinApplicationFactory>
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly JellyfinApplicationFactory _factory;

    public FederationMediaTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task IssueToken_Unsigned_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.TryAddWithoutValidation("Content-Type", "application/activity+json");
        var response = await client.PostAsync($"Federation/Media/{Guid.NewGuid():N}/Token", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueToken_SignedByNonFollower_Returns401()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/notfollower";

        // Seed an actor but no FederationFollower row — signature will verify but the follower
        // lookup should fail, producing 401.
        await SeedActorAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

        var request = BuildSignedTokenRequest(Guid.NewGuid(), actorUrl, rsa);
        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueToken_SignedByFollower_NonExistentItem_Returns404()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/hank";

        await SeedFollowerAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

        var request = BuildSignedTokenRequest(Guid.NewGuid(), actorUrl, rsa);
        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStream_MissingToken_Returns400()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"Federation/Media/{Guid.NewGuid():N}/Stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStream_InvalidToken_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"Federation/Media/{Guid.NewGuid():N}/Stream?token=notarealtoken");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMasterPlaylist_InvalidToken_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"Federation/Media/{Guid.NewGuid():N}/master.m3u8?token=notarealtoken");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubtitle_InvalidToken_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"Federation/Media/{Guid.NewGuid():N}/Subtitles/0/Stream.vtt?token=notarealtoken");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueToken_SignedFollower_ReturnsTokenAndStreamUrl()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/isabel";
        await SeedFollowerAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

        var movie = SeedLocalMovie("Integration Test Movie");

        var request = BuildSignedTokenRequest(movie.Id, actorUrl, rsa);
        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<TokenResponseSnapshot>(body, CaseInsensitiveJsonOptions);
        Assert.NotNull(parsed);
        Assert.False(string.IsNullOrEmpty(parsed!.Token));

        Assert.Contains($"/Federation/Media/{movie.Id:N}/Stream?token=", parsed.StreamUrl, StringComparison.Ordinal);
        Assert.Contains(parsed.Token!, parsed.StreamUrl, StringComparison.Ordinal);

        var rawToken = parsed.Token!;

        // Token validates end-to-end via the service.
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IFederationStreamTokenService>();
        var followerId = await tokenService.ValidateAsync(rawToken!, movie.Id, default);
        Assert.NotNull(followerId);
    }

    [Fact]
    public async Task GetStream_ValidTokenForLocalFile_Returns200WithFileBytes()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/jack";
        await SeedFollowerAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

        // Back the movie with a real temp file so Static=true can passthrough-stream it.
        var mediaBytes = Encoding.UTF8.GetBytes("fake mp4 content for integration test");
        var mediaPath = Path.Combine(Path.GetTempPath(), $"federation-test-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(mediaPath, mediaBytes);

        var movie = SeedLocalMovie("Streamable Movie", path: mediaPath, container: "mp4");

        try
        {
            // Issue a token via the service directly — simpler than round-tripping through
            // the signed POST path, which is exercised by IssueToken_SignedFollower tests.
            int followerId;
            using (var scope = _factory.Services.CreateScope())
            {
                var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
                await using var db = await dbProvider.CreateDbContextAsync();
                followerId = (await db.FederationFollowers.Include(f => f.Actor).FirstAsync(f => f.Actor.Url == actorUrl)).Id;
            }

            string rawToken;
            using (var scope = _factory.Services.CreateScope())
            {
                var tokenService = scope.ServiceProvider.GetRequiredService<IFederationStreamTokenService>();
                rawToken = await tokenService.IssueAsync(followerId, movie.Id, TimeSpan.FromMinutes(5), default);
            }

            var client = _factory.CreateClient();
            var response = await client.GetAsync($"Federation/Media/{movie.Id:N}/Stream?token={rawToken}&static=true");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var received = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(mediaBytes, received);
        }
        finally
        {
            if (File.Exists(mediaPath))
            {
                File.Delete(mediaPath);
            }
        }
    }

    private Movie SeedLocalMovie(string name, string? path = null, string? container = null)
    {
        using var scope = _factory.Services.CreateScope();
        var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();

        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            Container = container,
            IsVirtualItem = path is null
        };

        libraryManager.CreateItem(movie, null);
        return movie;
    }

    private async Task SeedActorAsync(string actorUrl, string publicKeyPem)
    {
        using var scope = _factory.Services.CreateScope();
        var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
        var dbContext = await dbProvider.CreateDbContextAsync();
        await using (dbContext)
        {
            if (!await dbContext.FederationActors.AnyAsync(a => a.Url == actorUrl))
            {
                var actor = new Jellyfin.Database.Implementations.Entities.Federation.FederationActor(
                    actorUrl,
                    $"{actorUrl}/inbox",
                    $"{actorUrl}/outbox",
                    publicKeyPem);
                await dbContext.FederationActors.AddAsync(actor);
                await dbContext.SaveChangesAsync();
            }
        }
    }

    private async Task SeedFollowerAsync(string actorUrl, string publicKeyPem)
    {
        using var scope = _factory.Services.CreateScope();
        var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
        var dbContext = await dbProvider.CreateDbContextAsync();
        await using (dbContext)
        {
            var actor = await dbContext.FederationActors.FirstOrDefaultAsync(a => a.Url == actorUrl);
            if (actor is null)
            {
                actor = new Jellyfin.Database.Implementations.Entities.Federation.FederationActor(
                    actorUrl,
                    $"{actorUrl}/inbox",
                    $"{actorUrl}/outbox",
                    publicKeyPem);
                await dbContext.FederationActors.AddAsync(actor);
                await dbContext.SaveChangesAsync();
            }

            if (!await dbContext.FederationFollowers.AnyAsync(f => f.ActorId == actor.Id))
            {
                await dbContext.FederationFollowers.AddAsync(
                    new Jellyfin.Database.Implementations.Entities.Federation.FederationFollower(actor.Id));
                await dbContext.SaveChangesAsync();
            }
        }
    }

    private static HttpRequestMessage BuildSignedTokenRequest(Guid itemId, string actorUrl, RSA rsa)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { });
        var request = new HttpRequestMessage(HttpMethod.Post, $"Federation/Media/{itemId:N}/Token");
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/activity+json");

        var hash = SHA256.HashData(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Digest", $"sha-256=:{Convert.ToBase64String(hash)}:");

        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keyId = $"{actorUrl}#main-key";
        var components = new[] { "@method", "@authority", "@path", "content-digest", "content-type" };

        var signingLines = new List<string>
        {
            "\"@method\": POST",
            "\"@authority\": localhost",
            $"\"@path\": /Federation/Media/{itemId:N}/Token",
            $"\"content-digest\": sha-256=:{Convert.ToBase64String(hash)}:",
            "\"content-type\": application/activity+json"
        };

        var componentList = string.Join(" ", components.Select(c => $"\"{c}\""));
        var signatureParams = $"({componentList});created={created};keyid=\"{keyId}\"";
        signingLines.Add($"\"@signature-params\": {signatureParams}");

        var signingBase = string.Join('\n', signingLines);
        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(signingBase),
            HashAlgorithmName.SHA512,
            RSASignaturePadding.Pss);

        request.Headers.TryAddWithoutValidation("Signature-Input", $"sig1={signatureParams}");
        request.Headers.TryAddWithoutValidation("Signature", $"sig1=:{Convert.ToBase64String(signatureBytes)}:");

        return request;
    }

    private void EnableFederation()
    {
        using var scope = _factory.Services.CreateScope();
        var configManager = scope.ServiceProvider.GetRequiredService<IConfigurationManager>();
        var config = configManager.GetFederationConfiguration();
        config.Enabled = true;
        config.Hostname = "test.example";
        config.ActorName = "jellyfin";
        configManager.SaveConfiguration(FederationConfigurationStore.StoreKey, config);
    }

    private sealed class TokenResponseSnapshot
    {
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public string StreamUrl { get; set; } = string.Empty;
    }
}
