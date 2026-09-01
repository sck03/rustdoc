using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        private static string BuildOutBoxFilePath(
            string outBoxDirectory,
            string originalFileName,
            string batchReference,
            ISet<string>? reservedPaths = null)
        {
            string fullOutBoxDirectory = Path.GetFullPath(outBoxDirectory);
            string safeFileName = NormalizeOutBoxFileName(originalFileName);
            string safeBatchReference = CrossPlatformFileNamePolicy.SanitizeFileNamePart(
                batchReference,
                '-',
                "batch");
            string candidate = Path.Combine(fullOutBoxDirectory, safeFileName);
            if (!IsOutBoxPathTaken(candidate, reservedPaths)) return ValidateOutBoxCandidate(candidate, fullOutBoxDirectory);

            string stem = Path.GetFileNameWithoutExtension(safeFileName);
            candidate = Path.Combine(fullOutBoxDirectory, $"{stem}_{safeBatchReference}.xml");
            int suffix = 2;
            while (IsOutBoxPathTaken(candidate, reservedPaths))
            {
                candidate = Path.Combine(fullOutBoxDirectory, $"{stem}_{safeBatchReference}_{suffix++}.xml");
            }

            return ValidateOutBoxCandidate(candidate, fullOutBoxDirectory);
        }

        private static string NormalizeOutBoxFileName(string originalFileName)
        {
            string normalized = (originalFileName ?? string.Empty).Trim().Replace('\\', '/');
            string fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf('\0') >= 0 ||
                !string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("官方客户端交接报文必须是有效的 XML 文件名。");
            }

            if (CrossPlatformFileNamePolicy.IsSafeFileName(fileName)) return fileName.Normalize();
            string safeStem = CrossPlatformFileNamePolicy.SanitizeFileNamePart(
                Path.GetFileNameWithoutExtension(fileName),
                '_',
                "payload");
            return $"{safeStem}.xml";
        }

        private static bool IsOutBoxPathTaken(string path, ISet<string>? reservedPaths) =>
            File.Exists(path) || Directory.Exists(path) || (reservedPaths?.Contains(path) ?? false);

        private static string ValidateOutBoxCandidate(string candidate, string outBoxDirectory)
        {
            string fullCandidate = Path.GetFullPath(candidate);
            string fullRoot = Path.GetFullPath(outBoxDirectory);
            PathBoundaryHelper.EnsureWithinRoot(fullCandidate, fullRoot, "官方客户端 OutBox 文件路径越界。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(fullCandidate, "官方客户端 OutBox 文件路径无效。");
            return fullCandidate;
        }
    }
}
