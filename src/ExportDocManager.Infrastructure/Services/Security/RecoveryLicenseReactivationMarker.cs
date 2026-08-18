using System.Text.Json;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Security
{
    internal static class RecoveryLicenseReactivationMarker
    {
        private const string FileName = "license-reactivation-required.json";

        public static bool Exists(IAppPathProvider pathProvider) =>
            File.Exists(GetPath(pathProvider));

        public static void Require(
            IAppPathProvider pathProvider,
            string packageId,
            DateTimeOffset requiredAtUtc)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            var payload = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    reason = "single-window-disaster-recovery",
                    packageId = packageId ?? string.Empty,
                    requiredAtUtc
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            AtomicFileHelper.WriteAllTextAtomic(GetPath(pathProvider), payload);
        }

        public static void Clear(IAppPathProvider pathProvider)
        {
            string path = GetPath(pathProvider);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetPath(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            return Path.Combine(pathProvider.SecurityRoot, FileName);
        }
    }
}
