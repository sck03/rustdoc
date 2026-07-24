using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using System.Net;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiRuntimeOptions
    {
        public const string DefaultListenUrls = "http://127.0.0.1:5188";
        public const string DesktopAccessTokenEnvironmentVariable = "EXPORTDOCMANAGER_DESKTOP_TOKEN";
        public const string ProductEditionEnvironmentVariable = "EXPORTDOCMANAGER_PRODUCT_EDITION";
        public const string NetworkModeEnvironmentVariable = "EXPORTDOCMANAGER_NETWORK_MODE";
        public const string AllowedOriginsEnvironmentVariable = "EXPORTDOCMANAGER_ALLOWED_ORIGINS";
        public const string TrustedProxiesEnvironmentVariable = "EXPORTDOCMANAGER_TRUSTED_PROXIES";
        public const string BootstrapTokenEnvironmentVariable = "EXPORTDOCMANAGER_BOOTSTRAP_TOKEN";
        public const string BootstrapTokenHeaderName = "X-ExportDocManager-Bootstrap-Token";

        public string AppRoot { get; init; } = AppContext.BaseDirectory;

        public string DataRoot { get; init; } = string.Empty;

        public string ListenUrls { get; init; } = DefaultListenUrls;

        public string DesktopAccessToken { get; init; } = string.Empty;

        public string ProductEdition { get; init; } = ProductEditionCatalog.Full;

        public bool NetworkMode { get; init; }

        public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

        /// <summary>
        /// Explicit reverse-proxy addresses that are allowed to supply X-Forwarded-* headers.
        /// The framework's loopback defaults remain in place when this list is empty.
        /// </summary>
        public IReadOnlyList<IPAddress> TrustedProxies { get; init; } = [];

        public string BootstrapToken { get; init; } = string.Empty;

        public static ApiRuntimeOptions Parse(string[] args)
        {
            args ??= [];

            string appRoot = ReadOption(args, "--app-root") ?? AppContext.BaseDirectory;
            string dataRoot = ReadOption(args, "--data-root") ??
                Environment.GetEnvironmentVariable(RuntimeAppPathProvider.DataRootEnvironmentVariable) ??
                string.Empty;
            string urls = ReadOption(args, "--urls") ??
                Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ??
                DefaultListenUrls;
            string desktopAccessToken =
                Environment.GetEnvironmentVariable(DesktopAccessTokenEnvironmentVariable) ??
                string.Empty;
            string productEdition = ReadOption(args, "--product-edition") ??
                Environment.GetEnvironmentVariable(ProductEditionEnvironmentVariable) ??
                ProductEditionCatalog.Full;
            string networkModeValue = ReadOption(args, "--network-mode") ??
                Environment.GetEnvironmentVariable(NetworkModeEnvironmentVariable) ??
                string.Empty;
            string allowedOriginsValue = ReadOption(args, "--allowed-origins") ??
                Environment.GetEnvironmentVariable(AllowedOriginsEnvironmentVariable) ??
                string.Empty;
            string trustedProxiesValue = ReadOption(args, "--trusted-proxies") ??
                Environment.GetEnvironmentVariable(TrustedProxiesEnvironmentVariable) ??
                string.Empty;
            string bootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable) ??
                string.Empty;

            return new ApiRuntimeOptions
            {
                AppRoot = NormalizeRoot(appRoot),
                DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? string.Empty : NormalizeRoot(dataRoot),
                ListenUrls = NormalizeListenUrls(urls),
                DesktopAccessToken = desktopAccessToken.Trim(),
                ProductEdition = ProductEditionCatalog.Normalize(productEdition),
                NetworkMode = ParseBoolean(networkModeValue),
                AllowedOrigins = NormalizeOrigins(allowedOriginsValue),
                TrustedProxies = NormalizeTrustedProxies(trustedProxiesValue),
                BootstrapToken = bootstrapToken.Trim()
            };
        }

        private static string ReadOption(IReadOnlyList<string> args, string optionName)
        {
            for (int index = 0; index < args.Count; index++)
            {
                string current = args[index] ?? string.Empty;
                if (string.Equals(current, optionName, StringComparison.OrdinalIgnoreCase))
                {
                    return index + 1 < args.Count ? args[index + 1] : string.Empty;
                }

                string prefix = optionName + "=";
                if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return current[prefix.Length..];
                }
            }

            return null;
        }

        private static string NormalizeRoot(string path)
        {
            return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeListenUrls(string urls)
        {
            return string.IsNullOrWhiteSpace(urls)
                ? DefaultListenUrls
                : string.Join(';', urls
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private static bool ParseBoolean(string value) =>
            bool.TryParse(value?.Trim(), out bool parsed) && parsed;

        private static IReadOnlyList<string> NormalizeOrigins(string value)
        {
            return (value ?? string.Empty)
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                                  (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                    : throw new InvalidOperationException($"允许的 Web 来源必须是有效的 HTTP/HTTPS 地址: {origin}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<IPAddress> NormalizeTrustedProxies(string value)
        {
            return (value ?? string.Empty)
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(proxy => IPAddress.TryParse(proxy, out var address)
                    ? address
                    : throw new InvalidOperationException(
                        $"可信反向代理必须是有效的 IP 地址（不接受主机名或 CIDR）：{proxy}"))
                .Distinct()
                .ToArray();
        }
    }
}
