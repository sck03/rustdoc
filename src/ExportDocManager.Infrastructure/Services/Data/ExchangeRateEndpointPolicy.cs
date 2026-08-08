using System.Net;
using System.Net.Sockets;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Data;

/// <summary>
/// Keeps the configurable exchange-rate source on a public HTTPS endpoint.
/// The service follows redirects itself so every hop receives the same SSRF
/// validation instead of silently forwarding a request to an internal host.
/// </summary>
public static class ExchangeRateEndpointPolicy
{
    public const string DefaultUrl = "https://www.boc.cn/sourcedb/whpj/";
    public const int MaximumRedirects = 5;

    public static Uri Normalize(string value)
    {
        string configured = string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim();
        if (configured.Length > 2048 ||
            !Uri.TryCreate(configured, UriKind.Absolute, out Uri uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.IsFile ||
            uri.IsLoopback ||
            IsDisallowedHostLiteral(uri.Host))
        {
            throw new ServiceValidationException(
                "汇率源网址必须是公开 HTTPS 地址，不能包含账号、查询参数、片段、回环地址或私有网络地址。");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Path), UriKind.Absolute);
    }

    public static async Task ValidatePublicHostAsync(
        Uri endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(resolveHostAsync);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await ResolvePublicAddressesAsync(
                endpoint.DnsSafeHost,
                resolveHostAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Used by the production SocketsHttpHandler to bind the real transport
    /// connection to an address that has already passed the public-network
    /// policy. This closes the DNS-rebinding gap between validation and connect.
    /// </summary>
    public static async ValueTask<Stream> ConnectPublicHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        DnsEndPoint target = context.DnsEndPoint;
        IPAddress[] addresses;
        try
        {
            addresses = await ResolvePublicAddressesAsync(
                    target.Host,
                    static (host, token) => Dns.GetHostAddressesAsync(host, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ServiceValidationException ex)
        {
            throw new HttpRequestException(ex.Message, ex);
        }

        Exception lastError = null;
        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(
                        new IPEndPoint(address, target.Port),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"无法连接汇率源主机“{target.Host}”。",
            lastError);
    }

    public static bool IsDisallowedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None) || address.IsIPv6Multicast ||
            address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
                   bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 198 && bytes[1] is >= 18 and <= 19 ||
                   bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                   bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                   bytes[0] >= 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        // Only global-unicast IPv6 is eligible, and documentation, benchmark,
        // ORCHID and 6to4 ranges remain unsuitable for an outbound data source.
        return (bytes[0] & 0xe0) != 0x20 ||
               bytes[0] == 0x20 && bytes[1] == 0x01 &&
               (
                   bytes[2] == 0x0d && bytes[3] == 0xb8 ||
                   bytes[2] == 0x00 && bytes[3] == 0x02 ||
                   bytes[2] == 0x00 && (bytes[3] & 0xf0) is 0x10 or 0x20
               ) ||
               bytes[0] == 0x20 && bytes[1] == 0x02;
    }

    private static async Task<IPAddress[]> ResolvePublicAddressesAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress literal)
            ? [literal]
            : await resolveHostAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses == null || addresses.Length == 0 || addresses.Any(IsDisallowedAddress))
        {
            throw CreateBlockedHostException(host);
        }

        return addresses
            .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
            .Distinct()
            .ToArray();
    }

    private static bool IsDisallowedHostLiteral(string host) =>
        IPAddress.TryParse(host, out IPAddress address) && IsDisallowedAddress(address);

    private static ServiceValidationException CreateBlockedHostException(string host) =>
        new($"已拒绝汇率源主机“{host}”：解析结果属于回环、私有、链路本地或其它不可访问网络。");
}
