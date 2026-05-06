using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
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
    private static readonly SemaphoreSlim _startupLock = new(1, 1);
    private static string? _cachedAccessToken;

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

        var root = await ParseJsonResponseAsync(response);

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
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await PostActivityAsync(client, new
        {
            type = "Follow",
            actor = "https://remote.example/users/bob",
            @object = new { id = "https://test.example/Federation/Actor" }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOutbox_WithoutSignature_ReturnsEmptyOrderedCollection()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("Federation/Outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ParseJsonResponseAsync(response);
        Assert.Equal("OrderedCollection", root.GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("totalItems").GetInt32());
        Assert.False(root.TryGetProperty("first", out _));
    }

    [Fact]
    public async Task PostInbox_WithWrongContentType_Returns415()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("Federation/Inbox", new
        {
            type = "Follow",
            actor = "https://remote.example/users/bob"
        });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task PostInbox_NonFollowWithoutSignature_Returns401()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await PostActivityAsync(client, new
        {
            type = "Accept",
            actor = "https://remote.example/users/someone",
            @object = new { type = "Follow" }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebFinger_WithValidResource_Returns200()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger?resource=acct:jellyfin@test.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ParseJsonResponseAsync(response);
        Assert.Equal("acct:jellyfin@test.example", root.GetProperty("subject").GetString());
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
    public async Task WebFinger_LinksPointToActorEndpoint()
    {
        EnableFederation();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(".well-known/webfinger?resource=acct:jellyfin@test.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ParseJsonResponseAsync(response);
        var selfLink = root.GetProperty("links").EnumerateArray()
            .First(l => l.GetProperty("rel").GetString() == "self");

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

    [Fact]
    public async Task SendFollowRequest_WhenActorUnreachable_Returns502AndNoRequest()
    {
        EnableFederation();
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            "Federation/Admin/Following/Requests?actorUrl=https://unreachable.example/actor", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        await UsingDbAsync(async db =>
        {
            var request = await db.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == "https://unreachable.example/actor"
                    && r.Type == FederationFollowRequestType.Following);
            Assert.Null(request);
        });
    }

    [Fact]
    public async Task PostInbox_WithAcceptFollow_WithoutSignature_Returns401()
    {
        EnableFederation();
        var actorUrl = "https://remote.example/users/charlie";

        await SeedActorWithFollowRequestAsync(
            actorUrl,
            "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0Z3VS5JJcds3xfn/ygWe\nBKg7e1kY5BRYsGOOGIEg0+JQFAV5LB4CEkqFOzK3eO1PNgC6kS2G1GRDqVHJ5zU\nIX1GBm2P4gVBlqN+8JO/LCDA7HPMYu/rMN6F4A5iBXaA+MbG7FEbXzO0pvBEOc8\nfxWTGknOO9MI+I/HUPCAYvIL3bF0dN\n0tnj6DvNfjXqvMKe1R0M0Wfqhi+RPHO7oeqe+DnagDBgFKqD7JQnBVkuP3YQhcJm\nUO1KF3GHzK0LJW7vYCDaoFxUHdG/ZAd+i3EfFJGMsA+i9ECO3rNc+0gGm9RUNJDM\nQIDAQAB\n-----END PUBLIC KEY-----",
            FederationFollowRequestType.Following);

        var client = _factory.CreateClient();
        var response = await PostActivityAsync(client, new
        {
            type = "Accept",
            actor = actorUrl,
            @object = new
            {
                type = "Follow",
                actor = "https://test.example/Federation/Actor",
                @object = new { id = actorUrl }
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await UsingDbAsync(async db =>
        {
            var request = await db.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl
                    && r.Type == FederationFollowRequestType.Following);
            Assert.NotNull(request);
            Assert.False(request.Responded);
        });
    }

    [Fact]
    public async Task PostInbox_WithAcceptFollow_WithValidSignature_ConfirmsFollowing()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/dave";

        await SeedActorWithFollowRequestAsync(
            actorUrl, rsa.ExportSubjectPublicKeyInfoPem(), FederationFollowRequestType.Following);

        var client = _factory.CreateClient();
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

        var response = await client.SendAsync(
            BuildSignedRequest(HttpMethod.Post, "Federation/Inbox", accept, rsa, actorUrl));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await UsingDbAsync(async db =>
        {
            var followRequest = await db.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl
                    && r.Type == FederationFollowRequestType.Following);
            Assert.NotNull(followRequest);
            Assert.True(followRequest.Responded);

            var following = await db.FederationFollowings
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
            Assert.NotNull(following);
        });
    }

    [Fact]
    public async Task PostInbox_WithUndoFollow_WithValidSignature_RemovesFollower()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/eve";

        await SeedActorWithFollowerAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

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

        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            BuildSignedRequest(HttpMethod.Post, "Federation/Inbox", undo, rsa, actorUrl));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await UsingDbAsync(async db =>
        {
            var follower = await db.FederationFollowers
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
            Assert.Null(follower);
        });
    }

    [Fact]
    public async Task PostInbox_UndoFromNonFollower_IsNoOp()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/ghost";

        await SeedActorAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

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

        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            BuildSignedRequest(HttpMethod.Post, "Federation/Inbox", undo, rsa, actorUrl));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await UsingDbAsync(async db =>
        {
            var follower = await db.FederationFollowers
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
            Assert.Null(follower);
        });
    }

    [Fact]
    public async Task FollowFlow_InboundFollow_AdminAccepts_FollowerSeesOutbox()
    {
        EnableFederation();

        using var rsa = RSA.Create(2048);
        var actorUrl = "https://remote.example/users/fullflow";

        await SeedActorAsync(actorUrl, rsa.ExportSubjectPublicKeyInfoPem());

        // Phase 1: Remote actor sends a signed Follow to our inbox
        var follow = new
        {
            type = "Follow",
            actor = actorUrl,
            @object = new { id = "https://test.example/Federation/Actor" }
        };

        var client = _factory.CreateClient();
        var followResponse = await client.SendAsync(
            BuildSignedRequest(HttpMethod.Post, "Federation/Inbox", follow, rsa, actorUrl));
        Assert.Equal(HttpStatusCode.Accepted, followResponse.StatusCode);

        await UsingDbAsync(async db =>
        {
            var pendingRequest = await db.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl
                    && r.Type == FederationFollowRequestType.Follower
                    && !r.Responded);
            Assert.NotNull(pendingRequest);
        });

        // Phase 2: Admin accepts the follow request
        var adminClient = await CreateAuthenticatedClientAsync();
        var vetResponse = await adminClient.PatchAsync(
            $"Federation/Admin/Followers/Requests?actorUrl={Uri.EscapeDataString(actorUrl)}",
            JsonContent.Create(new { Accept = true }));
        Assert.Equal(HttpStatusCode.NoContent, vetResponse.StatusCode);

        await UsingDbAsync(async db =>
        {
            var follower = await db.FederationFollowers
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl);
            Assert.NotNull(follower);

            var request = await db.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl
                    && r.Type == FederationFollowRequestType.Follower);
            Assert.NotNull(request);
            Assert.True(request.Responded);
        });

        // Phase 3: Seed an outbox item — signed follower sees it
        await UsingDbAsync(async db =>
        {
            var outboxItem = new FederationOutboxActivity(
                "{\"type\":\"Create\",\"actor\":\"https://test.example/Federation/Actor\",\"object\":{\"type\":\"Video\",\"name\":\"Test Movie\"}}");
            await db.FederationOutboxActivities.AddAsync(outboxItem);
            await db.SaveChangesAsync();
        });

        var outboxRequest = BuildSignedRequest(HttpMethod.Get, "Federation/Outbox", null, rsa, actorUrl);
        var outboxResponse = await client.SendAsync(outboxRequest);
        Assert.Equal(HttpStatusCode.OK, outboxResponse.StatusCode);

        var outboxRoot = await ParseJsonResponseAsync(outboxResponse);
        Assert.Equal("OrderedCollection", outboxRoot.GetProperty("type").GetString());
        Assert.True(outboxRoot.GetProperty("totalItems").GetInt32() > 0);
        Assert.True(outboxRoot.TryGetProperty("first", out _));

        // Phase 4: Unsigned request still sees empty outbox
        var anonResponse = await _factory.CreateClient().GetAsync("Federation/Outbox");
        Assert.Equal(HttpStatusCode.OK, anonResponse.StatusCode);

        var anonRoot = await ParseJsonResponseAsync(anonResponse);
        Assert.Equal(0, anonRoot.GetProperty("totalItems").GetInt32());
        Assert.False(anonRoot.TryGetProperty("first", out _));
    }

    // =====================================================================
    // Feature-gate tests
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

    // =====================================================================
    // Helpers
    // =====================================================================

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

    private async Task<string> GetAccessTokenAsync()
    {
        if (_cachedAccessToken is not null)
        {
            return _cachedAccessToken;
        }

        await _startupLock.WaitAsync();
        try
        {
            if (_cachedAccessToken is not null)
            {
                return _cachedAccessToken;
            }

            var client = _factory.CreateClient();
            _cachedAccessToken = await AuthHelper.CompleteStartupAsync(client);
            return _cachedAccessToken;
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await GetAccessTokenAsync();
        client.DefaultRequestHeaders.AddAuthHeader(token);
        return client;
    }

    private async Task UsingDbAsync(Func<JellyfinDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var dbProvider = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JellyfinDbContext>>();
        var db = await dbProvider.CreateDbContextAsync();
        await using (db)
        {
            await action(db);
        }
    }

    private async Task SeedActorAsync(string actorUrl, string publicKeyPem)
    {
        await UsingDbAsync(async db =>
        {
            if (!await db.FederationActors.AnyAsync(a => a.Url == actorUrl))
            {
                var actor = new FederationActor(
                    actorUrl, $"{actorUrl}/inbox", $"{actorUrl}/outbox", publicKeyPem);
                await db.FederationActors.AddAsync(actor);
                await db.SaveChangesAsync();
            }
        });
    }

    private async Task SeedActorWithFollowRequestAsync(
        string actorUrl, string publicKeyPem, FederationFollowRequestType direction)
    {
        await UsingDbAsync(async db =>
        {
            var actor = await db.FederationActors.FirstOrDefaultAsync(a => a.Url == actorUrl);
            if (actor is null)
            {
                actor = new FederationActor(
                    actorUrl, $"{actorUrl}/inbox", $"{actorUrl}/outbox", publicKeyPem);
                await db.FederationActors.AddAsync(actor);
                await db.SaveChangesAsync();
            }

            if (!await db.FederationFollowRequests.AnyAsync(
                    r => r.ActorId == actor.Id && r.Type == direction))
            {
                await db.FederationFollowRequests.AddAsync(new FederationFollowRequest(actor.Id, direction));
                await db.SaveChangesAsync();
            }
        });
    }

    private async Task SeedActorWithFollowerAsync(string actorUrl, string publicKeyPem)
    {
        await UsingDbAsync(async db =>
        {
            if (!await db.FederationActors.AnyAsync(a => a.Url == actorUrl))
            {
                var actor = new FederationActor(
                    actorUrl, $"{actorUrl}/inbox", $"{actorUrl}/outbox", publicKeyPem);
                await db.FederationActors.AddAsync(actor);
                await db.SaveChangesAsync();

                await db.FederationFollowers.AddAsync(new FederationFollower(actor.Id));
                await db.SaveChangesAsync();
            }
        });
    }

    private static async Task<JsonElement> ParseJsonResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }

    private static async Task<HttpResponseMessage> PostActivityAsync(HttpClient client, object activity)
    {
        var content = JsonContent.Create(activity);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/activity+json");
        return await client.PostAsync("Federation/Inbox", content);
    }

    private static HttpRequestMessage BuildSignedRequest(
        HttpMethod method, string path, object? body, RSA rsa, string actorUrl)
    {
        byte[]? bodyBytes = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body);
        return BuildSignedRequest(method, path, bodyBytes, rsa, actorUrl);
    }

    private static HttpRequestMessage BuildSignedRequest(
        HttpMethod method, string path, byte[]? body, RSA rsa, string actorUrl)
    {
        var request = new HttpRequestMessage(method, path);

        var components = new List<string> { "@method", "@authority", "@path" };
        var signingLines = new List<string>
        {
            $"\"@method\": {method.Method}",
            "\"@authority\": localhost",
            $"\"@path\": /{path.Split('?')[0]}"
        };

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/activity+json");

            var hash = SHA256.HashData(body);
            var digestValue = $"sha-256=:{Convert.ToBase64String(hash)}:";
            request.Content.Headers.TryAddWithoutValidation("Content-Digest", digestValue);

            components.Add("content-digest");
            components.Add("content-type");
            signingLines.Add($"\"content-digest\": {digestValue}");
            signingLines.Add("\"content-type\": application/activity+json");
        }

        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keyId = $"{actorUrl}#main-key";
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
}
