using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowClientProfilePathResolver
    {
        public static string GetBuiltInBusinessRoot(
            string singleWindowRoot,
            string profileKey,
            SingleWindowBusinessType businessType)
        {
            string normalizedProfileKey = profileKey?.Trim() ?? string.Empty;
            if (normalizedProfileKey.Length != 36 ||
                !normalizedProfileKey.StartsWith("SWP-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(normalizedProfileKey[4..], "N", out _))
            {
                throw new ArgumentException("操作档案标识无效。", nameof(profileKey));
            }
            string clientRoot = Path.Combine(
                (singleWindowRoot ?? string.Empty).Trim(),
                "Client",
                "Profiles",
                normalizedProfileKey);
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => Path.Combine(clientRoot, "CustomsCoo"),
                SingleWindowBusinessType.AgentConsignment => Path.Combine(clientRoot, "AgentConsignment"),
                _ => throw new ArgumentOutOfRangeException(nameof(businessType))
            };
        }

        public static string ResolveConfiguredRoot(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CustomsCooClientRootPath ?? string.Empty,
                SingleWindowBusinessType.AgentConsignment => profile.AgentConsignmentClientRootPath ?? string.Empty,
                _ => string.Empty
            };
        }

        public static string NormalizeClientRootPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            string trimmed = rootPath.Trim();
            if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ServiceValidationException("持卡机官方客户端目录必须位于本机磁盘，不能使用网络共享路径。");
            }

            if (!Path.IsPathRooted(trimmed))
            {
                throw new ServiceValidationException("持卡机官方客户端目录必须使用本机绝对路径。");
            }

            string normalized = Path.GetFullPath(trimmed);
            string leafName = Path.GetFileName(
                normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(leafName, "OutBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "SentBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "InBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "FailBox", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Directory.GetParent(normalized)?.FullName ?? normalized;
            }

            return normalized;
        }
    }
}
