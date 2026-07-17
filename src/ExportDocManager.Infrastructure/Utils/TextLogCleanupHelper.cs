namespace ExportDocManager.Utils
{
    public readonly record struct TextLogCleanupSummary(int DeletedByAge, int DeletedByCount)
    {
        public int TotalDeleted => DeletedByAge + DeletedByCount;
    }

    public static class TextLogCleanupHelper
    {
        public static TextLogCleanupSummary Clean(string logsPath, int retentionDays, int retainedFileCount)
        {
            return CleanFiles(logsPath, "*.txt", retentionDays, retainedFileCount);
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
                .OrderByDescending(file => file.LastWriteTime)
                .ToList();

            return CleanEntries(
                files,
                file => file.LastWriteTime,
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
                .OrderByDescending(directory => directory.LastWriteTime)
                .ToList();

            return CleanEntries(
                directories,
                directory => directory.LastWriteTime,
                directory => TryDeleteDirectory(directory.FullName),
                retentionDays,
                retainedDirectoryCount);
        }

        private static TextLogCleanupSummary CleanEntries<T>(
            List<T> entries,
            Func<T, DateTime> getLastWriteTime,
            Func<T, bool> tryDelete,
            int retentionDays,
            int retainedCount)
        {
            int deletedByAge = 0;
            int deletedByCount = 0;

            if (retentionDays > 0)
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
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
    }
}
