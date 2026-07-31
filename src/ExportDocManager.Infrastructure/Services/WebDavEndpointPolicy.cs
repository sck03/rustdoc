using System.Net;

namespace ExportDocManager.Services.Infrastructure
{
    public static class WebDavEndpointPolicy
    {
        public static bool TryNormalize(string value, out string normalizedUrl, out string errorMessage)
        {
            normalizedUrl = string.Empty;
            errorMessage = string.Empty;
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                errorMessage = "WebDAV 地址必须是有效的 http 或 https 绝对地址。";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                errorMessage = "WebDAV 地址不能包含用户名或密码，请使用独立账号字段。";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
            {
                errorMessage = "WebDAV 地址不能包含查询参数或片段。";
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(uri))
            {
                errorMessage = "非本机 WebDAV 必须使用 HTTPS；HTTP 仅允许 localhost 或回环地址用于本机测试。";
                return false;
            }

            normalizedUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return true;
        }

        private static bool IsLoopbackHost(Uri uri)
        {
            if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
        }
    }
}
