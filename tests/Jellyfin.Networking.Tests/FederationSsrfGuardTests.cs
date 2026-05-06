using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jellyfin.Networking.Federation;
using Xunit;

namespace Jellyfin.Networking.Tests;

public class FederationSsrfGuardTests
{
    private static HttpClient CreateGuardedClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = FederationSsrfGuard.OnConnectAsync
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    [Theory]
    [InlineData("http://127.0.0.1/test")]
    [InlineData("http://10.0.0.1/test")]
    [InlineData("http://172.16.0.1/test")]
    [InlineData("http://192.168.1.1/test")]
    [InlineData("http://[::1]/test")]
    public async Task GuardedClient_PrivateAddress_ThrowsHttpRequestException(string url)
    {
        using var client = CreateGuardedClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri(url)));
        Assert.Contains("SSRF", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardedClient_UnresolvableHost_ThrowsHttpRequestException()
    {
        using var client = CreateGuardedClient();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("https://this-host-does-not-exist-jellyfin-test.invalid/test")));
    }

    [Fact]
    public async Task GuardedClient_LinkLocalIPv6_ThrowsHttpRequestException()
    {
        using var client = CreateGuardedClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("http://[fe80::1%25lo0]/test")));
        Assert.Contains("SSRF", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
