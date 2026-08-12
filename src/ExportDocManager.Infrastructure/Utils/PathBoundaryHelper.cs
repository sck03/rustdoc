namespace ExportDocManager.Utils
{
    public static class PathBoundaryHelper
    {
        // Windows path identity is case-insensitive. Unix and macOS can run on
        // case-sensitive volumes, so use ordinal comparison there; accepting a
        // differently-cased sibling as the same managed root would weaken the
        // containment boundary on those volumes.
        public static StringComparison PathComparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static StringComparer PathComparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public static bool IsWithinRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string normalizedPath = Normalize(path);
            string normalizedRoot = Normalize(root);
            if (string.Equals(normalizedPath, normalizedRoot, PathComparison))
            {
                return true;
            }

            if (normalizedRoot.EndsWith(Path.DirectorySeparatorChar) ||
                normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return normalizedPath.StartsWith(normalizedRoot, PathComparison);
            }

            return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison) ||
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

        public static string EnsureNoReparsePointsWithinRoot(
            string path,
            string root,
            string errorMessage)
        {
            string fullRoot = Normalize(root);
            string fullPath = EnsureWithinRoot(path, fullRoot, errorMessage);
            string? current = fullPath;

            while (true)
            {
                FileAttributes? attributes = null;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (FileNotFoundException)
                {
                    // 尚未创建的叶节点由现存父目录决定边界。
                }
                catch (DirectoryNotFoundException)
                {
                    // 尚未创建的叶节点由现存父目录决定边界。
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException(errorMessage, ex);
                }

                if (attributes.HasValue &&
                    (attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException(
                        $"{errorMessage} 路径不能包含符号链接或重解析点：{current}");
                }

                if (string.Equals(current, fullRoot, PathComparison))
                {
                    return fullPath;
                }

                current = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(current) || !IsWithinRoot(current, fullRoot))
                {
                    throw new UnauthorizedAccessException(errorMessage);
                }
            }
        }

        public static string EnsureNoLinkLikeComponents(string path, string errorMessage)
        {
            string fullPath = Normalize(path);
            string current = fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new UnauthorizedAccessException(
                            $"{errorMessage} 路径不能经过符号链接、目录联接或其他重解析点：{current}");
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (IOException ex)
                {
                    throw new UnauthorizedAccessException(errorMessage, ex);
                }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) ||
                    string.Equals(parent, current, PathComparison))
                {
                    break;
                }
                current = parent;
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
            string fullPath = Path.GetFullPath(path);
            int rootLength = Path.GetPathRoot(fullPath)?.Length ?? 0;
            return fullPath.Length > rootLength
                ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }
    }
}
