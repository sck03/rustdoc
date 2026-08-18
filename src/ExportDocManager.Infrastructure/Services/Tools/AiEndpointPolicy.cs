using System.Net;
using System.Net.Sockets;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools;

/// <summary>
/// Restricts configurable AI endpoints to public HTTPS services or an explicit
/// loopback HTTP service such as Ollama. The transport connects only to the
/// addresses that passed validation, closing DNS-rebinding and proxy bypasses.
/// </summary>
public static class AiEndpointPolicy
{
    public const string DefaultUrl = "https://api.deepseek.com/v1/chat/completions";

    public static Uri Normalize(string? value)
    {
        string configured = string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim();
        if (configured.Length > 2048 ||
            !Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.IsFile)
        {
            throw InvalidEndpoint();
        }

        bool explicitLoopback = IsExplicitLoopbackHost(uri.Host);
        if (uri.Scheme == Uri.UriSchemeHttp && !explicitLoopback)
        {
            throw new ServiceValidationException("公网 AI 接口必须使用 HTTPS；HTTP 仅允许 localhost 或回环 IP。");
        }

        if (IPAddress.TryParse(uri.Host, out IPAddress? literal) &&
            !IPAddress.IsLoopback(NormalizeAddress(literal)) &&
            ExchangeRateEndpointPolicy.IsDisallowedAddress(literal))
        {
            throw BlockedHost(uri.Host);
        }

        return uri;
    }

    public static async Task ValidateAllowedHostAsync(
        Uri endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(resolveHostAsync);
        _ = await ResolveAllowedAddressesAsync(
                endpoint.DnsSafeHost,
                IsExplicitLoopbackHost(endpoint.Host),
                resolveHostAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<Stream> ConnectAllowedHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool explicitLoopback = IsExplicitLoopbackHost(context.DnsEndPoint.Host);
        IPAddress[] addresses;
        try
        {
            addresses = await ResolveAllowedAddressesAsync(
                    context.DnsEndPoint.Host,
                    explicitLoopback,
                    static (host, token) => Dns.GetHostAddressesAsync(host, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ServiceValidationException ex)
        {
            throw new HttpRequestException(ex.Message, ex);
        }

        Exception? lastError = null;
        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port),
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
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException($"无法连接 AI 接口主机“{context.DnsEndPoint.Host}”。", lastError);
    }

    private static async Task<IPAddress[]> ResolveAllowedAddressesAsync(
        string host,
        bool explicitLoopback,
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPAddress[]? addresses = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await resolveHostAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses == null || addresses.Length == 0)
        {
            throw BlockedHost(host);
        }

        IPAddress[] normalized = addresses.Select(NormalizeAddress).Distinct().ToArray();
        bool allowed = explicitLoopback
            ? normalized.All(IPAddress.IsLoopback)
            : normalized.All(address => !ExchangeRateEndpointPolicy.IsDisallowedAddress(address));
        if (!allowed)
        {
            throw BlockedHost(host);
        }

        return normalized;
    }

    private static bool IsExplicitLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(NormalizeAddress(address));

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static ServiceValidationException InvalidEndpoint() =>
        new("AI 接口地址必须是完整的 HTTP/HTTPS 地址，且不能包含账号信息或 URL 片段。");

    private static ServiceValidationException BlockedHost(string host) =>
        new($"已拒绝 AI 接口主机“{host}”：仅允许公开网络地址，或显式 localhost/回环 IP。");
}
