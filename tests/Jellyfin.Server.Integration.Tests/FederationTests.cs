using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Server.Integration.Tests;

/// <summary>
/// Integration tests for federation endpoints.
/// </summary>
public class FederationTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;

    public FederationTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetActor_WhenDisabled_Returns404()
    {
        DisableFederation();
        var client = _factory.CreateClient();
        var response = await client.GetAsync("Federation/Actor");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActor_WhenEnabled_ReturnsActorDocument()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("Federation/Actor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.Equal("Service", root.GetProperty("type").GetString());
        Assert.Equal("https://test.example/Federation/Actor", root.GetProperty("id").GetString());
        Assert.Equal("jellyfin", root.GetProperty("preferredUsername").GetString());
        Assert.Equal("https://test.example/Federation/Inbox", root.GetProperty("inbox").GetString());
        Assert.Equal("https://test.example/Federation/Outbox", root.GetProperty("outbox").GetString());
        Assert.True(root.TryGetProperty("publicKey", out var publicKey));
        Assert.False(string.IsNullOrEmpty(publicKey.GetProperty("publicKeyPem").GetString()));
    }

    [Fact]
    public async Task GetActor_ContentType_IsActivityJson()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("Federation/Actor");
        Assert.Equal("application/activity+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostInbox_WithUnsignedFollow_Returns401()
    {
        // Follow activities are now signature-gated like every other inbound activity — the
        // LocalKeyProvider lazily fetches unknown actors on first contact so there's no need for
        // a Follow-bypass. An unsigned Follow gets 401.
        EnableFederation();
        var client = _factory.CreateClient();

        var follow = new
        {
            type = "Follow",
            actor = "https://remote.example/users/bob",
            @object = new { id = "https://test.example/Federation/Actor" }
        };

        var content = JsonContent.Create(follow);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        var response = await client.PostAsync("Federation/Inbox", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOutbox_WithoutSignature_ReturnsEmptyOrderedCollection()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("Federation/Outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("OrderedCollection", root.GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("totalItems").GetInt32());
        // Empty collection must not advertise a `first` page — follower-gated content stays hidden.
        Assert.False(root.TryGetProperty("first", out _));
    }

    [Fact]
    public async Task PostInbox_WithWrongContentType_Returns415()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var follow = new
        {
            type = "Follow",
            actor = "https://remote.example/users/bob"
        };

        var response = await client.PostAsJsonAsync("Federation/Inbox", follow);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task PostInbox_NonFollowWithoutSignature_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var accept = new
        {
            type = "Accept",
            actor = "https://remote.example/users/someone",
            @object = new { type = "Follow" }
        };

        var content = JsonContent.Create(accept);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        var response = await client.PostAsync("Federation/Inbox", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebFinger_WithValidResource_Returns200()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger?resource=acct:jellyfin@test.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        Assert.Equal("acct:jellyfin@test.example", doc.RootElement.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task WebFinger_WithInvalidResource_Returns404()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger?resource=acct:nobody@test.example");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WebFinger_WithMissingResource_Returns400()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendFollowRequest_WhenActorUnreachable_Returns502AndNoRequest()
    {
        EnableFederation();
        var client = _factory.CreateClient();
        var accessToken = await AuthHelper.CompleteStartupAsync(client);
        client.DefaultRequestHeaders.AddAuthHeader(accessToken);

        var response = await client.PostAsync("Federation/Admin/Following/Requests?actorUrl=https://unreachable.example/actor", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        // Verify no follow request was persisted since actor fetch failed
        using var scope = _factory.Services.CreateScope();
        var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
        var dbContext = await dbProvider.CreateDbContextAsync();
        await using (dbContext)
        {
            var request = await dbContext.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == "https://unreachable.example/actor"
                    && r.Type == Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following);
            Assert.Null(request);
        }
    }

    [Fact]
    public async Task PostInbox_WithAcceptFollow_WithoutSignature_Returns401()
    {
        EnableFederation();

        // Seed: create an actor and a pending following request
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                // Only seed if not already present
                var charlieUrl = "https://remote.example/users/charlie";
                var charlie = await dbContext.FederationActors.FirstOrDefaultAsync(a => a.Url == charlieUrl);
                if (charlie is null)
                {
                    charlie = new Jellyfin.Database.Implementations.Entities.Federation.FederationActor(
                        charlieUrl,
                        charlieUrl + "/inbox",
                        charlieUrl + "/outbox",
                        "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0Z3VS5JJcds3xfn/ygWe\nBKg7e1kY5BRYsGOOGIEg0+JQFAV5LB4CEkqFOzK3eO1PNgC6kS2G1GRDqVHJ5zU\nIX1GBm2P4gVBlqN+8JO/LCDA7HPMYu/rMN6F4A5iBXaA+MbG7FEbXzO0pvBEOc8\nfxWTGknOO9MI+I/HUPCAYvIL3bF0dN\n0tnj6DvNfjXqvMKe1R0M0Wfqhi+RPHO7oeqe+DnagDBgFKqD7JQnBVkuP3YQhcJm\nUO1KF3GHzK0LJW7vYCDaoFxUHdG/ZAd+i3EfFJGMsA+i9ECO3rNc+0gGm9RUNJDM\nQIDAQAB\n-----END PUBLIC KEY-----");
                    await dbContext.FederationActors.AddAsync(charlie);
                    await dbContext.SaveChangesAsync();
                }

                if (!await dbContext.FederationFollowRequests.AnyAsync(r => r.ActorId == charlie.Id && r.Type == Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following))
                {
                    var followRequest = new Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequest(
                        charlie.Id,
                        Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following);
                    await dbContext.FederationFollowRequests.AddAsync(followRequest);
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        // Send Accept{Follow} without HTTP signature — should be rejected
        var client = _factory.CreateClient();
        var accept = new
        {
            type = "Accept",
            actor = "https://remote.example/users/charlie",
            @object = new
            {
                type = "Follow",
                actor = "https://test.example/Federation/Actor",
                @object = new { id = "https://remote.example/users/charlie" }
            }
        };

        var content = JsonContent.Create(accept);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        var response = await client.PostAsync("Federation/Inbox", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Verify: follow request is NOT marked responded
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                var request = await dbContext.FederationFollowRequests
                    .Include(r => r.Actor)
                    .FirstOrDefaultAsync(r => r.Actor.Url == "https://remote.example/users/charlie"
                        && r.Type == Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following);
                Assert.NotNull(request);
                Assert.False(request.Responded);
            }
        }
    }

    [Fact]
    public async Task PostInbox_WithAcceptFollow_WithValidSignature_ConfirmsFollowing()
    {
        EnableFederation();

        // Generate a keypair for the remote actor
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var actorUrl = "https://remote.example/users/dave";

        // Seed: actor with the public key and a pending following request
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                var daveActor = await dbContext.FederationActors.FirstOrDefaultAsync(a => a.Url == actorUrl);
                if (daveActor is null)
                {
                    daveActor = new Jellyfin.Database.Implementations.Entities.Federation.FederationActor(
                        actorUrl,
                        $"{actorUrl}/inbox",
                        $"{actorUrl}/outbox",
                        publicKeyPem);
                    await dbContext.FederationActors.AddAsync(daveActor);
                    await dbContext.SaveChangesAsync();
                }

                if (!await dbContext.FederationFollowRequests.AnyAsync(r => r.ActorId == daveActor.Id && r.Type == Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following))
                {
                    var followRequest = new Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequest(
                        daveActor.Id,
                        Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following);
                    await dbContext.FederationFollowRequests.AddAsync(followRequest);
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        // Build and sign the Accept{Follow} request
        var accept = new
        {
            type = "Accept",
            actor = actorUrl,
            @object = new
            {
                type = "Follow",
                actor = "https://test.example/Federation/Actor",
                @object = new { id = actorUrl }
            }
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(accept);
        var request = new HttpRequestMessage(HttpMethod.Post, "Federation/Inbox");
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/activity+json");

        // Compute content-digest
        var hash = SHA256.HashData(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Digest", $"sha-256=:{Convert.ToBase64String(hash)}:");

        // Build RFC 9421 signature
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keyId = $"{actorUrl}#main-key";
        var components = new[] { "@method", "@authority", "@path", "content-digest", "content-type" };

        var signingLines = new List<string>
        {
            "\"@method\": POST",
            "\"@authority\": localhost",
            "\"@path\": /Federation/Inbox",
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

        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify: follow request is marked responded and a following entry exists
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                var followRequest = await dbContext.FederationFollowRequests
                    .Include(r => r.Actor)
                    .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl
                        && r.Type == Jellyfin.Database.Implementations.Entities.Federation.FederationFollowRequestType.Following);
                Assert.NotNull(followRequest);
                Assert.True(followRequest.Responded);

                var following = await dbContext.FederationFollowings
                    .Include(f => f.Actor)
                    .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
                Assert.NotNull(following);
            }
        }
    }

    [Fact]
    public async Task PostInbox_WithUndoFollow_WithValidSignature_RemovesFollower()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var actorUrl = "https://remote.example/users/eve";

        // Seed: actor + follower entry (they already follow us)
        using (var scope = _factory.Services.CreateScope())
        {
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

                    var follower = new Jellyfin.Database.Implementations.Entities.Federation.FederationFollower(actor.Id);
                    await dbContext.FederationFollowers.AddAsync(follower);
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        // Build and sign Undo{Follow}
        var undo = new
        {
            type = "Undo",
            actor = actorUrl,
            @object = new
            {
                type = "Follow",
                actor = actorUrl,
                @object = new { id = "https://test.example/Federation/Actor" }
            }
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(undo);
        var request = new HttpRequestMessage(HttpMethod.Post, "Federation/Inbox");
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
            "\"@path\": /Federation/Inbox",
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

        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify: follower removed
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                var follower = await dbContext.FederationFollowers
                    .Include(f => f.Actor)
                    .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
                Assert.Null(follower);
            }
        }
    }

    [Fact]
    public async Task PostInbox_UndoFromNonFollower_IsNoOp()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var actorUrl = "https://remote.example/users/ghost";

        // Seed: actor exists but is NOT a follower
        using (var scope = _factory.Services.CreateScope())
        {
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

        // Build and sign Undo{Follow}
        var undo = new
        {
            type = "Undo",
            actor = actorUrl,
            @object = new
            {
                type = "Follow",
                actor = actorUrl,
                @object = new { id = "https://test.example/Federation/Actor" }
            }
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(undo);
        var request = new HttpRequestMessage(HttpMethod.Post, "Federation/Inbox");
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
            "\"@path\": /Federation/Inbox",
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

        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Should still return 202 (no error exposed), just a no-op
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify no follower was created or deleted
        using (var scope = _factory.Services.CreateScope())
        {
            var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
            var dbContext = await dbProvider.CreateDbContextAsync();
            await using (dbContext)
            {
                var follower = await dbContext.FederationFollowers
                    .Include(f => f.Actor)
                    .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
                Assert.Null(follower);
            }
        }
    }

    [Fact]
    public async Task WebFinger_LinksPointToActorEndpoint()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger?resource=acct:jellyfin@test.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var links = doc.RootElement.GetProperty("links");
        var selfLink = links.EnumerateArray().First(l => l.GetProperty("rel").GetString() == "self");

        Assert.Equal("application/activity+json", selfLink.GetProperty("type").GetString());
        Assert.Equal("https://test.example/Federation/Actor", selfLink.GetProperty("href").GetString());
    }

    [Fact]
    public async Task AdminEndpoints_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("Federation/Admin/Followers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private void DisableFederation()
    {
        using var scope = _factory.Services.CreateScope();
        var configManager = scope.ServiceProvider.GetRequiredService<IConfigurationManager>();
        var config = configManager.GetFederationConfiguration();
        config.Enabled = false;
        configManager.SaveConfiguration(FederationConfigurationStore.StoreKey, config);
    }

    // =====================================================================
    // Feature-gate tests: every federation-gated endpoint must return 404
    // when Enabled=false, to hide the surface from servers that never
    // shipped the feature. The gate is a policy-level invariant — these
    // tests pin it so a future controller addition doesn't silently leak.
    // =====================================================================

    [Theory]
    [InlineData("GET", "Federation/Actor")]
    [InlineData("POST", "Federation/Inbox")]
    [InlineData("GET", "Federation/Outbox")]
    [InlineData("GET", "Federation/Admin/Followers")]
    [InlineData("GET", "Federation/Admin/Followers/Requests")]
    [InlineData("GET", "Federation/Admin/Following")]
    [InlineData("GET", "Federation/Admin/Following/Requests")]
    [InlineData("GET", ".well-known/webfinger?resource=acct:jellyfin@test.example")]
    public async Task FederationGatedEndpoint_WhenDisabled_Returns404(string method, string path)
    {
        DisableFederation();
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent(string.Empty);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "Federation/Media/00000000000000000000000000000000/Token")]
    [InlineData("GET", "Federation/Media/00000000000000000000000000000000/Stream?token=ignored")]
    [InlineData("GET", "Federation/Media/00000000000000000000000000000000/master.m3u8?token=ignored")]
    [InlineData("GET", "Federation/Media/00000000000000000000000000000000/Subtitles/0/Stream.vtt?token=ignored")]
    public async Task FederationMediaEndpoint_WhenDisabled_Returns404(string method, string path)
    {
        DisableFederation();
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent(string.Empty);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
