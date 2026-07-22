namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobService
    {
        private void TryDeleteControlledBrowserOutput(string outputPath)
        {
            if (_pathProvider == null || string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string root = Path.GetFullPath(Path.Combine(_pathProvider.ExportRoot, "Browser"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // 清理是尽力而为，文件占用不能阻止任务历史维护。
            }
        }

        internal void CleanupControlledOutputForJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId.Trim(), out var job))
            {
                return;
            }

            TryDeleteControlledBrowserOutput(job.OutputPath);
        }

        internal void CleanupControlledOutputPath(string outputPath)
        {
            TryDeleteControlledBrowserOutput(outputPath);
        }

        private void PruneOrphanControlledBrowserOutputs()
        {
            if (_pathProvider == null)
            {
                return;
            }

            string browserRoot = Path.Combine(_pathProvider.ExportRoot, "Browser");
            if (!Directory.Exists(browserRoot))
            {
                return;
            }

            try
            {
                var referencedDirectories = _jobs.Values
                    .Select(job => job.OutputPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path =>
                    {
                        try { return Path.GetDirectoryName(Path.GetFullPath(path)); }
                        catch { return string.Empty; }
                    })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-_retentionOptions.RetentionDays);

                foreach (string kindDirectory in Directory.EnumerateDirectories(browserRoot))
                {
                    foreach (string jobDirectory in Directory.EnumerateDirectories(kindDirectory))
                    {
                        string fullDirectory = Path.GetFullPath(jobDirectory);
                        if (referencedDirectories.Contains(fullDirectory) ||
                            Directory.GetLastWriteTimeUtc(fullDirectory) >= cutoffUtc)
                        {
                            continue;
                        }

                        Directory.Delete(fullDirectory, recursive: true);
                    }

                    if (!Directory.EnumerateFileSystemEntries(kindDirectory).Any())
                    {
                        Directory.Delete(kindDirectory);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                // 受管输出清理是尽力而为，不能因为文件占用阻止 API 启动。
            }
        }
    }
}
