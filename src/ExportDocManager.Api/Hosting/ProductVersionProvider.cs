using System.Reflection;

namespace ExportDocManager.Api.Hosting
{
    public static class ProductVersionProvider
    {
        public static string ProductVersion => StripBuildMetadata(InformationalVersion);

        public static string InformationalVersion
        {
            get
            {
                var assembly = typeof(ProductVersionProvider).Assembly;
                string informationalVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informationalVersion))
                {
                    return informationalVersion.Trim();
                }

                return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }
        }

        private static string StripBuildMetadata(string version)
        {
            string value = version?.Trim() ?? string.Empty;
            int metadataIndex = value.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex > 0 ? value[..metadataIndex] : value;
        }
    }
}
