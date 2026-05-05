using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Server.Implementations.Federation.Signing;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Signing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public class HTTPSignatureServiceTests
{
    private readonly RSA _rsa;
    private readonly string _publicKeyPem;
    private readonly string _privateKeyPem;
    private readonly FederationConfiguration _config;
    private readonly Mock<IConfigurationManager> _configManagerMock;
    private readonly Mock<IHTTPSignatureKeyProvider> _keyProviderMock;
    private readonly HTTPSignatureService _sut;

    public HTTPSignatureServiceTests()
    {
        _rsa = RSA.Create(2048);
        _publicKeyPem = _rsa.ExportSubjectPublicKeyInfoPem();
        _privateKeyPem = _rsa.ExportRSAPrivateKeyPem();

        _config = new FederationConfiguration
        {
            PublicKey = _publicKeyPem,
            PrivateKey = _privateKeyPem,
            ActorName = "jellyfin",
            Hostname = "my-instance.com",
            Enabled = true
        };

        _configManagerMock = new Mock<IConfigurationManager>();
        _configManagerMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(_config);

        _keyProviderMock = new Mock<IHTTPSignatureKeyProvider>();

        _sut = new HTTPSignatureService(_keyProviderMock.Object, _configManagerMock.Object);
    }

    [Fact]
    public async Task SignAndValidate_RoundTrip_Succeeds()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Equal("https://my-instance.com/Federation/Actor", result);
    }

    [Fact]
    public async Task Validate_WithWrongKey_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);

        using var wrongRsa = RSA.Create(2048);
        var wrongPublicKey = wrongRsa.ExportSubjectPublicKeyInfoPem();
        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(wrongPublicKey);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithTamperedBody_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        // Build the inbound request with a different body but keep original headers (including Content-Digest)
        var inbound = await ConvertToHttpRequestWithTamperedBody(outbound, "{\"type\":\"Delete\",\"actor\":\"https://evil.example/actor\"}");

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithTamperedContentDigestHeader_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);
        // Tamper with the content-digest header — signature will no longer verify because
        // the signed base includes the original content-digest value.
        inbound.Headers["content-digest"] = new StringValues("sha-256=:tampered=:");

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithTamperedHeader_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);
        inbound.ContentType = "text/plain";

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithUnknownActor_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);

        _keyProviderMock
            .Setup(m => m.GetPublicKey(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithMissingSignatureHeaders_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("remote.example");
        context.Request.Path = "/Federation/Inbox";

        var result = await _sut.ValidateAsync(context.Request);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithExpiredSignature_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        // Manipulate the Signature-Input to set `created` to 10 minutes ago
        var staleCreated = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var inbound = await ConvertToHttpRequestWithAlteredCreated(outbound, staleCreated);

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithFutureSignature_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        // Manipulate the Signature-Input to set `created` to 10 minutes in the future
        var futureCreated = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var inbound = await ConvertToHttpRequestWithAlteredCreated(outbound, futureCreated);

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithMissingCreatedParam_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        // Remove the `created` parameter from Signature-Input
        var inbound = await ConvertToHttpRequestWithRemovedCreated(outbound);

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_WithMissingContentDigestHeader_ReturnsNull()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow", actor = "https://my-instance.com/Federation/Actor" });
        await _sut.SignAsync(outbound);

        var inbound = await ConvertToHttpRequest(outbound);
        // Remove content-digest header entirely
        inbound.Headers.Remove("content-digest");

        _keyProviderMock
            .Setup(m => m.GetPublicKey("https://my-instance.com/Federation/Actor"))
            .ReturnsAsync(_publicKeyPem);

        var result = await _sut.ValidateAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task SignAsync_ProducesValidSignatureAndContentDigest()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        var bodyContent = "{\"type\":\"Follow\"}";
        outbound.Content = new StringContent(bodyContent, Encoding.UTF8, "application/activity+json");

        await _sut.SignAsync(outbound);

        // Verify Signature and Signature-Input headers exist
        Assert.True(outbound.Headers.Contains("Signature"));
        Assert.True(outbound.Headers.Contains("Signature-Input"));

        // Verify Content-Digest was added to content headers
        Assert.True(outbound.Content.Headers.Contains("Content-Digest"));
        var digestHeader = outbound.Content.Headers.GetValues("Content-Digest").First();
        Assert.StartsWith("sha-256=:", digestHeader, StringComparison.Ordinal);
        Assert.EndsWith(":", digestHeader, StringComparison.Ordinal);

        // Verify the digest value is correct
        var bodyBytes = Encoding.UTF8.GetBytes(bodyContent);
        var expectedHash = SHA256.HashData(bodyBytes);
        var expectedDigest = $"sha-256=:{Convert.ToBase64String(expectedHash)}:";
        Assert.Equal(expectedDigest, digestHeader);
    }

    [Fact]
    public async Task SignAsync_IncludesCreatedParam()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Post, "https://remote.example/Federation/Inbox");
        outbound.Content = JsonContent.Create(new { type = "Follow" });

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _sut.SignAsync(outbound);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sigInputHeader = outbound.Headers.GetValues("Signature-Input").First();
        // Extract created=<value> from the header
        var createdMatch = System.Text.RegularExpressions.Regex.Match(sigInputHeader, @"created=(\d+)");
        Assert.True(createdMatch.Success);
        var created = long.Parse(createdMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(created, before, after);
    }

    private static async Task<HttpRequest> ConvertToHttpRequest(HttpRequestMessage outbound)
    {
        var context = new DefaultHttpContext();
        var uri = outbound.RequestUri ?? throw new InvalidOperationException("Request URI must be set.");

        context.Request.Method = outbound.Method.ToString();
        context.Request.Host = new HostString(uri.Authority);
        context.Request.Path = uri.AbsolutePath;
        context.Request.Scheme = uri.Scheme;
        context.Request.QueryString = new QueryString(uri.Query);

        // Copy headers from outbound request
        foreach (var header in outbound.Headers)
        {
            context.Request.Headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        // Copy content headers and body
        if (outbound.Content != null)
        {
            foreach (var header in outbound.Content.Headers)
            {
                context.Request.Headers[header.Key] = new StringValues(header.Value.ToArray());
            }

            var body = await outbound.Content.ReadAsByteArrayAsync();
            context.Request.Body = new MemoryStream(body);
        }

        return context.Request;
    }

    private static async Task<HttpRequest> ConvertToHttpRequestWithTamperedBody(HttpRequestMessage outbound, string tamperedBody)
    {
        var context = new DefaultHttpContext();
        var uri = outbound.RequestUri ?? throw new InvalidOperationException("Request URI must be set.");

        context.Request.Method = outbound.Method.ToString();
        context.Request.Host = new HostString(uri.Authority);
        context.Request.Path = uri.AbsolutePath;
        context.Request.Scheme = uri.Scheme;
        context.Request.QueryString = new QueryString(uri.Query);

        // Copy headers from outbound request (including the original Content-Digest)
        foreach (var header in outbound.Headers)
        {
            context.Request.Headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        if (outbound.Content != null)
        {
            foreach (var header in outbound.Content.Headers)
            {
                context.Request.Headers[header.Key] = new StringValues(header.Value.ToArray());
            }
        }

        // Use a different body — the Content-Digest header won't match
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(tamperedBody));

        return context.Request;
    }

    private static async Task<HttpRequest> ConvertToHttpRequestWithAlteredCreated(HttpRequestMessage outbound, long newCreated)
    {
        var inbound = await ConvertToHttpRequest(outbound);

        // Replace the created timestamp in the Signature-Input header
        var sigInput = inbound.Headers["Signature-Input"].ToString();
        var altered = System.Text.RegularExpressions.Regex.Replace(sigInput, @"created=\d+", $"created={newCreated}");
        inbound.Headers["Signature-Input"] = new StringValues(altered);

        return inbound;
    }

    private static async Task<HttpRequest> ConvertToHttpRequestWithRemovedCreated(HttpRequestMessage outbound)
    {
        var inbound = await ConvertToHttpRequest(outbound);

        // Remove the created parameter from the Signature-Input header
        var sigInput = inbound.Headers["Signature-Input"].ToString();
        var altered = System.Text.RegularExpressions.Regex.Replace(sigInput, @";created=\d+", string.Empty);
        inbound.Headers["Signature-Input"] = new StringValues(altered);

        return inbound;
    }
}
