namespace ExportDocManager.Api.Hosting
{
    public static class ApiCorsPolicy
    {
        public const string LocalFrontendPolicyName = "ExportDocManagerLocalFrontend";

        public static bool IsLoopbackOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "tauri.localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAllowedOrigin(string origin, ApiRuntimeOptions runtimeOptions)
        {
            if (IsLoopbackOrigin(origin)) return true;
            if (runtimeOptions?.NetworkMode != true ||
                string.IsNullOrWhiteSpace(origin) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            string normalizedOrigin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return runtimeOptions.AllowedOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase);
        }
    }
}
