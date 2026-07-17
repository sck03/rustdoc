using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowClientProfilePathResolver
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static string GetBuiltInBusinessRoot(string singleWindowRoot, SingleWindowBusinessType? businessType)
        {
            string defaultClientRoot = Path.Combine((singleWindowRoot ?? string.Empty).Trim(), "Client");
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => Path.Combine(defaultClientRoot, "Cooimp"),
                SingleWindowBusinessType.AgentConsignment => Path.Combine(defaultClientRoot, "Acd"),
                _ => Path.Combine(defaultClientRoot, "Others")
            };
        }

        public static ResolvedClientPath ResolveConfiguredImportRoot(
            SwClientProfile profile,
            SingleWindowBusinessType? businessType)
        {
            return Resolve(profile, businessType, ClientPathKind.Import, includeFallback: false);
        }

        public static ResolvedClientPath ResolveConfiguredReceiptRoot(
            SwClientProfile profile,
            SingleWindowBusinessType? businessType)
        {
            return Resolve(profile, businessType, ClientPathKind.Receipt, includeFallback: false);
        }

        public static ResolvedClientPath ResolveEffectiveImportRoot(
            SwClientProfile profile,
            SingleWindowBusinessType? businessType,
            string singleWindowRoot)
        {
            return Resolve(profile, businessType, ClientPathKind.Import, includeFallback: true, singleWindowRoot);
        }

        public static ResolvedClientPath ResolveEffectiveReceiptRoot(
            SwClientProfile profile,
            SingleWindowBusinessType? businessType,
            string singleWindowRoot)
        {
            return Resolve(profile, businessType, ClientPathKind.Receipt, includeFallback: true, singleWindowRoot);
        }

        public static void UpdateBusinessOverride(
            SwClientProfile profile,
            SingleWindowBusinessType businessType,
            string importRootPath = "",
            string receiptRootPath = "")
        {
            ArgumentNullException.ThrowIfNull(profile);

            var container = Deserialize(profile.BusinessDirectoryOverridesJson);
            string businessKey = businessType.ToString();
            var item = container.Businesses
                .FirstOrDefault(candidate => string.Equals(candidate.BusinessType, businessKey, StringComparison.OrdinalIgnoreCase));

            item ??= new SingleWindowClientBusinessDirectoryOverride
            {
                BusinessType = businessKey
            };

            item.ImportRootPath = MergePath(item.ImportRootPath, importRootPath);
            item.ReceiptRootPath = MergePath(item.ReceiptRootPath, receiptRootPath);

            container.Businesses.RemoveAll(candidate =>
                string.Equals(candidate.BusinessType, businessKey, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(item.ImportRootPath) ||
                !string.IsNullOrWhiteSpace(item.ReceiptRootPath))
            {
                container.Businesses.Add(item);
            }

            profile.BusinessDirectoryOverridesJson = Serialize(container);
        }

        private static ResolvedClientPath Resolve(
            SwClientProfile profile,
            SingleWindowBusinessType? businessType,
            ClientPathKind pathKind,
            bool includeFallback,
            string singleWindowRoot = "")
        {
            if (businessType.HasValue)
            {
                var overrideItem = GetBusinessOverride(profile, businessType.Value);
                string overridePath = pathKind == ClientPathKind.Import
                    ? overrideItem?.ImportRootPath
                    : overrideItem?.ReceiptRootPath;

                if (!string.IsNullOrWhiteSpace(overridePath))
                {
                    return new ResolvedClientPath(overridePath.Trim());
                }

                return includeFallback
                    ? new ResolvedClientPath(GetBuiltInBusinessRoot(singleWindowRoot, businessType))
                    : ResolvedClientPath.Empty;
            }

            if (profile == null)
            {
                return includeFallback
                    ? new ResolvedClientPath(GetBuiltInBusinessRoot(singleWindowRoot, null))
                    : ResolvedClientPath.Empty;
            }

            string rootPath = pathKind == ClientPathKind.Import
                ? profile.ImportRootPath
                : profile.ReceiptRootPath;

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                return new ResolvedClientPath(rootPath.Trim());
            }

            return includeFallback
                ? new ResolvedClientPath(GetBuiltInBusinessRoot(singleWindowRoot, null))
                : ResolvedClientPath.Empty;
        }

        private static SingleWindowClientBusinessDirectoryOverride GetBusinessOverride(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            var container = Deserialize(profile?.BusinessDirectoryOverridesJson);
            return container.Businesses.FirstOrDefault(candidate =>
                string.Equals(candidate.BusinessType, businessType.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private static SingleWindowClientBusinessDirectoryOverrideContainer Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SingleWindowClientBusinessDirectoryOverrideContainer();
            }

            try
            {
                return JsonSerializer.Deserialize<SingleWindowClientBusinessDirectoryOverrideContainer>(json, _jsonOptions)
                    ?? new SingleWindowClientBusinessDirectoryOverrideContainer();
            }
            catch
            {
                return new SingleWindowClientBusinessDirectoryOverrideContainer();
            }
        }

        private static string Serialize(SingleWindowClientBusinessDirectoryOverrideContainer container)
        {
            if (container == null || container.Businesses.Count == 0)
            {
                return string.Empty;
            }

            return JsonSerializer.Serialize(container, _jsonOptions);
        }

        private static string MergePath(string currentValue, string newValue)
        {
            return string.IsNullOrWhiteSpace(newValue)
                ? currentValue?.Trim() ?? string.Empty
                : newValue.Trim();
        }

        private enum ClientPathKind
        {
            Import,
            Receipt
        }

        public sealed class ResolvedClientPath
        {
            public static readonly ResolvedClientPath Empty = new(string.Empty);

            public ResolvedClientPath(string path)
            {
                Path = path ?? string.Empty;
            }

            public string Path { get; }
        }

        private sealed class SingleWindowClientBusinessDirectoryOverrideContainer
        {
            public List<SingleWindowClientBusinessDirectoryOverride> Businesses { get; set; } = [];
        }

        private sealed class SingleWindowClientBusinessDirectoryOverride
        {
            public string BusinessType { get; set; } = string.Empty;

            public string ImportRootPath { get; set; } = string.Empty;

            public string ReceiptRootPath { get; set; } = string.Empty;
        }
    }
}
