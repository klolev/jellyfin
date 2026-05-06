using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace Jellyfin.Networking.Federation;

/// <summary>
/// A <see cref="SocketsHttpHandler.ConnectCallback"/> implementation that validates the resolved
/// IP address is not private/loopback before completing the TCP connection. This closes the DNS
/// rebinding window that exists when DNS resolution and connection are separate steps.
/// </summary>
public static class FederationSsrfGuard
{
    /// <summary>
    /// ConnectCallback that resolves DNS, validates all addresses are public, and connects
    /// to the first reachable public address. Throws if all addresses are private or unreachable.
    /// </summary>
    /// <param name="context">The connection context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected network stream.</returns>
    public static async ValueTask<Stream> OnConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new HttpRequestException($"DNS resolution failed for {host}", ex);
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"DNS resolution for {host} returned no addresses");
        }

        // Reject if ANY resolved address is private/loopback. A multi-homed host that
        // advertises both a public and a private address is suspicious.
        var privateAddr = addresses.FirstOrDefault(IsPrivateOrLoopback);
        if (privateAddr is not null)
        {
            throw new HttpRequestException(
                $"Federation SSRF guard: {host} resolves to private/loopback address {privateAddr}; connection rejected");
        }

        // Connect directly to the resolved IP, preserving the hostname for SNI/Host header
        // via the DnsEndPoint on the socket. We try each address in order (prefer IPv6).
        var orderedAddresses = addresses
            .OrderByDescending(a => a.AddressFamily == AddressFamily.InterNetworkV6)
            .ToArray();

        Socket? socket = null;
        foreach (var address in orderedAddresses)
        {
            socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
                socket = null;
            }
        }

        throw new HttpRequestException($"Failed to connect to {host}:{port} — all resolved addresses unreachable");
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
        {
            return true;
        }

        return NetworkConstants.IPv4RFC1918PrivateClassA.Contains(address)
            || NetworkConstants.IPv4RFC1918PrivateClassB.Contains(address)
            || NetworkConstants.IPv4RFC1918PrivateClassC.Contains(address)
            || NetworkConstants.IPv4RFC3927LinkLocal.Contains(address)
            || NetworkConstants.IPv6RFC4193UniqueLocal.Contains(address)
            || NetworkConstants.IPv6RFC4291SiteLocal.Contains(address);
    }
}
