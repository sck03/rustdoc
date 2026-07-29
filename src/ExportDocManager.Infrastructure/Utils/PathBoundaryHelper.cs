namespace ExportDocManager.Utils
{
    public static class PathBoundaryHelper
    {
        public static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static bool IsWithinRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string normalizedPath = Normalize(path);
            string normalizedRoot = Normalize(root);
            return string.Equals(normalizedPath, normalizedRoot, PathComparison) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, PathComparison);
        }

        public static string EnsureWithinRoot(string path, string root, string errorMessage)
        {
            string fullPath = Path.GetFullPath(path);
            if (!IsWithinRoot(fullPath, root))
            {
                throw new UnauthorizedAccessException(errorMessage);
            }

            return fullPath;
        }

        public static string ResolveProtocolRelativePath(string root, string relativePath, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidDataException(errorMessage);
            }

            string normalized = relativePath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized))
            {
                throw new InvalidDataException(errorMessage);
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment =>
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal) ||
                    segment.IndexOfAny(['\0', ':']) >= 0))
            {
                throw new InvalidDataException(errorMessage);
            }

            string candidate = Path.GetFullPath(Path.Combine([root, .. segments]));
            if (!IsWithinRoot(candidate, root))
            {
                throw new InvalidDataException(errorMessage);
            }

            return candidate;
        }

        public static string ToProtocolRelativePath(params string[] segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            var normalized = segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => segment.Trim().Trim('/', '\\'))
                .ToArray();
            if (normalized.Length == 0 || normalized.Any(segment =>
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal) ||
                    segment.Contains('/') ||
                    segment.Contains('\\')))
            {
                throw new InvalidDataException("交换包相对路径无效。");
            }

            return string.Join('/', normalized);
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
