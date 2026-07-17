using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static string BuildDefaultSingleWindowSubmitPackagePath(
            IAppPathProvider pathProvider,
            SingleWindowBusinessType businessType,
            int invoiceId)
        {
            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "coo" : "acd";
            string fileName = $"{prefix}-{invoiceId}-{DateTime.Now:yyyyMMddHHmmss}.swpkg";
            return Path.Combine(pathProvider.SingleWindowRoot, "Outbox", fileName);
        }

        private static string BuildDefaultSingleWindowReceiptPackagePath(
            IAppPathProvider pathProvider,
            SingleWindowBusinessType businessType,
            string batchReference,
            string invoiceNo)
        {
            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "coo" : "acd";
            string token = BuildSafeSingleWindowFileToken(
                string.IsNullOrWhiteSpace(batchReference) ? invoiceNo : batchReference);
            string fileName = $"receipt-{prefix}-{token}-{DateTime.Now:yyyyMMddHHmmss}.swpkg";
            return Path.Combine(pathProvider.SingleWindowRoot, "Outbox", fileName);
        }

        private static string BuildDefaultSingleWindowImportWorkingRoot(
            IAppPathProvider pathProvider,
            SingleWindowPackageType packageType)
        {
            string directoryName = packageType == SingleWindowPackageType.SubmitPackage
                ? "Inbox"
                : "ReceiptInbox";
            return Path.Combine(pathProvider.SingleWindowRoot, directoryName);
        }

        private static (string UploadRoot, string PackagePath) BuildSingleWindowBrowserUploadPath(
            IAppPathProvider pathProvider,
            string fileName)
        {
            string uploadRoot = Path.Combine(
                pathProvider.CacheRoot,
                "BrowserUploads",
                "SingleWindow",
                Guid.NewGuid().ToString("N"));
            return (uploadRoot, Path.Combine(uploadRoot, fileName));
        }

        internal static string ResolveSingleWindowImportWorkingRoot(
            IAppPathProvider pathProvider,
            SingleWindowPackageType packageType,
            string requestedWorkingDirectory)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string defaultRoot = BuildDefaultSingleWindowImportWorkingRoot(pathProvider, packageType);
            string singleWindowRoot = Path.GetFullPath(pathProvider.SingleWindowRoot);
            string candidate = string.IsNullOrWhiteSpace(requestedWorkingDirectory)
                ? defaultRoot
                : requestedWorkingDirectory.Trim();

            string resolved = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(singleWindowRoot, candidate));

            if (!IsPathWithinRoot(resolved, singleWindowRoot))
            {
                throw new UnauthorizedAccessException("单一窗口交接包只能解压到运行数据根 SingleWindow 目录下。");
            }

            return resolved;
        }

        private static string BuildSafeSingleWindowFileToken(string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? "package" : value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = trimmed
                .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
                .ToArray();
            string safe = new string(chars).Trim('-', '.', ' ');
            return string.IsNullOrWhiteSpace(safe) ? "package" : safe;
        }

        private static bool IsPathWithinRoot(string path, string root)
        {
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(
                       normalizedRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(
                       normalizedRoot + Path.AltDirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
