using System.Net;
using System.Net.Sockets;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.BrowserRuntime;

public static class BrowserCdpEndpointPolicy
{
    public const string EndpointEnvironmentVariable =
        "EXPORTDOCMANAGER_BROWSER_CDP_ENDPOINT";

    public static bool TryResolve(out Uri endpoint)
    {
        string configured = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable)?.Trim()
            ?? string.Empty;
        if (configured.Length == 0)
        {
            endpoint = null;
            return false;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri parsed) ||
            parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            parsed.AbsolutePath is not ("" or "/"))
        {
            throw new ServiceValidationException(
                $"{EndpointEnvironmentVariable} 必须是没有账号、查询或路径的 HTTP(S) CDP 地址。");
        }

        if (parsed.Scheme == Uri.UriSchemeHttp && !IsTrustedHttpHost(parsed.Host))
        {
            throw new ServiceValidationException(
                $"{EndpointEnvironmentVariable} 仅允许 HTTPS；容器内单标签服务名、回环或私有地址可显式使用 HTTP。");
        }

        endpoint = new Uri(parsed.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        return true;
    }

    private static bool IsTrustedHttpHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.IndexOf('.') < 0 && host.IndexOf(':') < 0)
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out IPAddress address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 169 && bytes[1] == 254;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
        }

        return false;
    }
}
