namespace ExportDocManager.Utils
{
    public readonly record struct TextLogCleanupSummary(int DeletedByAge, int DeletedByCount)
    {
        public int TotalDeleted => DeletedByAge + DeletedByCount;
    }

    public static class TextLogCleanupHelper
    {
        public static TextLogCleanupSummary Clean(
            string logsPath,
            int retentionDays,
            int retainedFileCount,
            int maxFileSizeMB = 20)
        {
            long maxFileSizeBytes = Math.Clamp(maxFileSizeMB, 1, 1024) * 1024L * 1024L;
            TrimOversizedFiles(logsPath, maxFileSizeBytes);

            if (string.IsNullOrWhiteSpace(logsPath) || !Directory.Exists(logsPath))
            {
                return default;
            }

            var files = Directory.EnumerateFiles(logsPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsTextLogExtension(Path.GetExtension(path)))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            return CleanEntries(
                files,
                file => file.LastWriteTimeUtc,
                file => TryDeleteFile(file.FullName),
                retentionDays,
                retainedFileCount);
        }

        public static TextLogCleanupSummary CleanFiles(string directoryPath, string searchPattern, int retentionDays, int retainedFileCount)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return default;
            }

            string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern.Trim();
            var files = Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            return CleanEntries(
                files,
                file => file.LastWriteTimeUtc,
                file => TryDeleteFile(file.FullName),
                retentionDays,
                retainedFileCount);
        }

        public static TextLogCleanupSummary CleanDirectories(string rootPath, int retentionDays, int retainedDirectoryCount)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return default;
            }

            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ToList();

            return CleanEntries(
                directories,
                directory => directory.LastWriteTimeUtc,
                directory => TryDeleteDirectory(directory.FullName),
                retentionDays,
                retainedDirectoryCount);
        }

        private static TextLogCleanupSummary CleanEntries<T>(
            List<T> entries,
            Func<T, DateTimeOffset> getLastWriteTime,
            Func<T, bool> tryDelete,
            int retentionDays,
            int retainedCount)
        {
            int deletedByAge = 0;
            int deletedByCount = 0;

            if (retentionDays > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
                foreach (var entry in entries.Where(entry => getLastWriteTime(entry) < cutoff).ToList())
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
                foreach (var entry in entries.Skip(retainedCount).ToList())
                {
                    if (tryDelete(entry))
                    {
                        deletedByCount++;
                    }
                }
            }

            return new TextLogCleanupSummary(deletedByAge, deletedByCount);
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTextLogExtension(string extension) =>
            string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);

        private static void TrimOversizedFiles(string directoryPath, long maxFileSizeBytes)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) ||
                !Directory.Exists(directoryPath) ||
                maxFileSizeBytes <= 0)
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsTextLogExtension(Path.GetExtension(path))))
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.Read);
                    if (stream.Length <= maxFileSizeBytes)
                    {
                        continue;
                    }

                    long targetLength = Math.Min(stream.Length, maxFileSizeBytes);
                    long sourceOffset = stream.Length - targetLength;
                    long destinationOffset = 0;
                    byte[] buffer = new byte[1024 * 1024];
                    while (destinationOffset < targetLength)
                    {
                        int requested = (int)Math.Min(buffer.Length, targetLength - destinationOffset);
                        int read = RandomAccess.Read(
                            stream.SafeFileHandle,
                            buffer.AsSpan(0, requested),
                            sourceOffset + destinationOffset);
                        if (read == 0)
                        {
                            break;
                        }

                        RandomAccess.Write(
                            stream.SafeFileHandle,
                            buffer.AsSpan(0, read),
                            destinationOffset);
                        destinationOffset += read;
                    }

                    stream.SetLength(destinationOffset);
                    stream.Flush(flushToDisk: true);
                }
                catch (IOException)
                {
                    // Log maintenance is best effort; an active writer may
                    // temporarily prevent trimming on some platforms.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
