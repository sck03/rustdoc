namespace ExportDocManager.Utils
{
    public readonly record struct TextLogCleanupSummary(int DeletedByAge, int DeletedByCount)
    {
        public int TotalDeleted => DeletedByAge + DeletedByCount;
    }

    public static class TextLogCleanupHelper
    {
        private const string FileError = "日志文件不能经过符号链接、目录联接或其他重解析点。";
        private const string DirectoryError = "日志目录不能经过符号链接、目录联接或其他重解析点。";

        public static TextLogCleanupSummary Clean(
            string logsPath,
            int retentionDays,
            int retainedFileCount,
            int maxFileSizeMB = 20,
            DateTimeOffset? utcNow = null)
        {
            string? root = TryResolveExistingDirectory(logsPath, DirectoryError);
            if (root is null) return default;

            var files = GetSafeFiles(root, file => IsTextLogExtension(Path.GetExtension(file)));
            TrimOversizedFiles(files, Math.Clamp(maxFileSizeMB, 1, 1024) * 1024L * 1024L);
            return CleanEntries(files, GetLastWriteTimeUtc, TryDeleteFile, retentionDays, retainedFileCount, utcNow);
        }

        public static TextLogCleanupSummary CleanFiles(
            string directoryPath,
            string searchPattern,
            int retentionDays,
            int retainedFileCount,
            DateTimeOffset? utcNow = null)
        {
            string? root = TryResolveExistingDirectory(directoryPath, DirectoryError);
            if (root is null) return default;

            string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern.Trim();
            var files = GetSafeFiles(root, file => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                pattern.AsSpan(), Path.GetFileName(file).AsSpan(), OperatingSystem.IsWindows()));
            return CleanEntries(files, GetLastWriteTimeUtc, TryDeleteFile, retentionDays, retainedFileCount, utcNow);
        }

        public static TextLogCleanupSummary CleanDirectories(
            string rootPath,
            int retentionDays,
            int retainedDirectoryCount,
            DateTimeOffset? utcNow = null)
        {
            string? root = TryResolveExistingDirectory(rootPath, DirectoryError);
            if (root is null) return default;

            // Validate every immediate child, including empty directories, before selecting one.
            _ = ControlledFileSystemEnumerator.EnumerateImmediateFiles(root, errorMessage: DirectoryError);
            var directories = new DirectoryInfo(root).EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                .Select(directory =>
                {
                    string path = Path.GetFullPath(directory.FullName);
                    PathBoundaryHelper.EnsureNoLinkLikeComponents(path, DirectoryError);
                    EnsureKind(path, ReadAttributes(path, DirectoryError), directory: true, DirectoryError);
                    return path;
                })
                .OrderByDescending(GetLastWriteTimeUtc)
                .ToList();
            return CleanEntries(directories, GetLastWriteTimeUtc, TryDeleteDirectory,
                retentionDays, retainedDirectoryCount, utcNow);
        }

        private static List<string> GetSafeFiles(string root, Func<string, bool> predicate)
        {
            var files = ControlledFileSystemEnumerator.EnumerateImmediateFiles(root, errorMessage: DirectoryError)
                .Where(predicate)
                .ToList();
            files.Sort((left, right) => GetLastWriteTimeUtc(right).CompareTo(GetLastWriteTimeUtc(left)));
            return files;
        }

        private static TextLogCleanupSummary CleanEntries<T>(
            List<T> entries,
            Func<T, DateTimeOffset> getLastWriteTime,
            Func<T, bool> tryDelete,
            int retentionDays,
            int retainedCount,
            DateTimeOffset? utcNow)
        {
            int deletedByAge = 0;
            int deletedByCount = 0;
            if (retentionDays > 0)
            {
                DateTimeOffset cutoff = (utcNow ?? TimeProvider.System.GetUtcNow()).AddDays(-retentionDays);
                foreach (T entry in entries.Where(entry => getLastWriteTime(entry) < cutoff).ToList())
                {
                    if (tryDelete(entry))
                    {
                        deletedByAge++;
                        entries.Remove(entry);
                    }
                }
            }

            if (retainedCount > 0 && entries.Count > retainedCount)
            {
                foreach (T entry in entries.Skip(retainedCount).ToList())
                    if (tryDelete(entry)) deletedByCount++;
            }

            return new TextLogCleanupSummary(deletedByAge, deletedByCount);
        }

        private static bool TryDeleteFile(string path)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(path, FileError);
            if (!TryReadAttributes(path, FileError, out FileAttributes attributes)) return true;
            EnsureKind(path, attributes, directory: false, FileError);
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (IOException)
            {
                // An active logger may keep a file open; retry on the next pass.
                return false;
            }

            PathBoundaryHelper.EnsureNoLinkLikeComponents(path, FileError);
            return !TryReadAttributes(path, FileError, out _);
        }

        private static bool TryDeleteDirectory(string path)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(path, DirectoryError);
            if (!TryReadAttributes(path, DirectoryError, out FileAttributes attributes)) return true;
            EnsureKind(path, attributes, directory: true, DirectoryError);
            _ = ControlledFileSystemEnumerator.EnumerateFiles(path, errorMessage: DirectoryError);
            AtomicFileHelper.TryDeleteDirectory(path);

            PathBoundaryHelper.EnsureNoLinkLikeComponents(path, DirectoryError);
            if (!TryReadAttributes(path, DirectoryError, out FileAttributes remaining)) return true;
            EnsureKind(path, remaining, directory: true, DirectoryError);
            _ = ControlledFileSystemEnumerator.EnumerateFiles(path, errorMessage: DirectoryError);
            return false;
        }

        private static void TrimOversizedFiles(IReadOnlyList<string> files, long maxFileSizeBytes)
        {
            if (maxFileSizeBytes <= 0) return;
            foreach (string path in files)
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(path, FileError);
                if (!TryReadAttributes(path, FileError, out FileAttributes attributes)) continue;
                EnsureKind(path, attributes, directory: false, FileError);
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    if (stream.Length <= maxFileSizeBytes) continue;
                    long targetLength = Math.Min(stream.Length, maxFileSizeBytes);
                    long sourceOffset = stream.Length - targetLength;
                    long destinationOffset = 0;
                    byte[] buffer = new byte[1024 * 1024];
                    while (destinationOffset < targetLength)
                    {
                        int requested = (int)Math.Min(buffer.Length, targetLength - destinationOffset);
                        int read = RandomAccess.Read(stream.SafeFileHandle, buffer.AsSpan(0, requested), sourceOffset + destinationOffset);
                        if (read == 0) break;
                        RandomAccess.Write(stream.SafeFileHandle, buffer.AsSpan(0, read), destinationOffset);
                        destinationOffset += read;
                    }

                    stream.SetLength(destinationOffset);
                    stream.Flush(flushToDisk: true);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // A rotating logger may remove the file between enumeration and trimming.
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (IOException)
                {
                    // An active writer can temporarily prevent trimming.
                }
            }
        }

        private static string? TryResolveExistingDirectory(string? path, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException(errorMessage, exception);
            }

            PathBoundaryHelper.EnsureNoLinkLikeComponents(fullPath, errorMessage);
            if (!TryReadAttributes(fullPath, errorMessage, out FileAttributes attributes)) return null;
            EnsureKind(fullPath, attributes, directory: true, errorMessage);
            return fullPath;
        }

        private static bool TryReadAttributes(string path, string errorMessage, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                attributes = default;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (IOException exception)
            {
                throw new UnauthorizedAccessException(errorMessage, exception);
            }
        }

        private static FileAttributes ReadAttributes(string path, string errorMessage) =>
            TryReadAttributes(path, errorMessage, out FileAttributes attributes)
                ? attributes
                : throw new InvalidDataException($"{errorMessage} 路径在扫描期间消失：{path}");

        private static void EnsureKind(string path, FileAttributes attributes, bool directory, string errorMessage)
        {
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0 || isDirectory != directory)
                throw new InvalidDataException($"{errorMessage} 路径：{path}");
        }

        private static DateTimeOffset GetLastWriteTimeUtc(string path) =>
            GetLastWriteTimeUtcCore(path);

        private static DateTimeOffset GetLastWriteTimeUtcCore(string path)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(path, DirectoryError);
            FileAttributes attributes = ReadAttributes(path, DirectoryError);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{DirectoryError} 路径：{path}");
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }

        private static bool IsTextLogExtension(string extension) =>
            string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }
}
