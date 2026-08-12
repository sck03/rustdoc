using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    internal static class ManagedDataPathResolver
    {
        public static string NormalizeStoredPath(string? storedPath, string requiredTopLevelDirectory)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                throw new InvalidDataException("受管运行数据路径不能为空。");
            }
            if (string.IsNullOrWhiteSpace(requiredTopLevelDirectory))
            {
                throw new ArgumentException("受管运行数据目录不能为空。", nameof(requiredTopLevelDirectory));
            }

            string normalized = storedPath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized) ||
                normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            {
                throw new InvalidDataException("受管运行数据路径必须是相对于运行数据根的路径。");
            }

            string[] segments = normalized.Split('/');
            if (segments.Length < 2 ||
                segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment is "." or ".." ||
                    segment.IndexOfAny(['\0', ':']) >= 0) ||
                !string.Equals(
                    segments[0],
                    requiredTopLevelDirectory.Trim().Trim('/', '\\'),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"受管运行数据路径必须位于 {requiredTopLevelDirectory.Trim().Trim('/', '\\')}/ 目录。");
            }
            return string.Join('/', segments);
        }

        public static string ResolveStoredPath(
            IAppPathProvider pathProvider,
             string? storedPath,
            string allowedRoot,
            string requiredTopLevelDirectory)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string normalized = NormalizeStoredPath(storedPath, requiredTopLevelDirectory);
            string dataRoot = Path.GetFullPath(pathProvider.DataRoot);
            string fullAllowedRoot = Path.GetFullPath(allowedRoot);
            if (!PathBoundaryHelper.IsWithinRoot(fullAllowedRoot, dataRoot))
            {
                throw new InvalidOperationException("受管运行数据允许目录不在运行数据根内。");
            }
            string fullPath = PathBoundaryHelper.ResolveProtocolRelativePath(
                dataRoot,
                normalized,
                "受管运行数据路径无效。");
            if (!PathBoundaryHelper.IsWithinRoot(fullPath, fullAllowedRoot))
            {
                throw new UnauthorizedAccessException("受管运行数据路径超出允许目录。");
            }
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                fullPath,
                dataRoot,
                "受管运行数据路径不能包含符号链接或重解析点。");
            return fullPath;
        }

        public static string ToStoredPath(
            IAppPathProvider pathProvider,
            string fullPath,
            string allowedRoot,
            string requiredTopLevelDirectory)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string dataRoot = Path.GetFullPath(pathProvider.DataRoot);
            string candidate = Path.GetFullPath(fullPath);
            string fullAllowedRoot = Path.GetFullPath(allowedRoot);
            if (!PathBoundaryHelper.IsWithinRoot(fullAllowedRoot, dataRoot) ||
                !PathBoundaryHelper.IsWithinRoot(candidate, fullAllowedRoot))
            {
                throw new UnauthorizedAccessException("文件不在允许的运行数据目录内。");
            }
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                candidate,
                dataRoot,
                "受管运行数据路径不能包含符号链接或重解析点。");
            return NormalizeStoredPath(
                Path.GetRelativePath(dataRoot, candidate).Replace('\\', '/'),
                requiredTopLevelDirectory);
        }

    }
}
