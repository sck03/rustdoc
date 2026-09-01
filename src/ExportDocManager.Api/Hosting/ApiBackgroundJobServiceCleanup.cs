using ExportDocManager.Utils;

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
                string root = Path.GetFullPath(Path.Combine(_pathProvider.ExportRoot, "Browser"));
                if (!PathBoundaryHelper.IsWithinRoot(fullPath, root) ||
                    string.Equals(fullPath, root, PathBoundaryHelper.PathComparison))
                {
                    return;
                }
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    fullPath,
                    _pathProvider.DataRoot,
                    "受控浏览器任务输出路径无效。");

                string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(directory) ||
                    !PathBoundaryHelper.IsWithinRoot(directory, root) ||
                    string.Equals(directory, root, PathBoundaryHelper.PathComparison))
                {
                    AtomicFileHelper.TryDeleteFile(fullPath);
                    return;
                }

                string parentDirectory = Directory.GetParent(directory)?.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(parentDirectory) ||
                    string.Equals(parentDirectory, root, PathBoundaryHelper.PathComparison))
                {
                    AtomicFileHelper.TryDeleteFile(fullPath);
                    return;
                }

                if (Directory.Exists(directory))
                {
                    // Use the controlled post-order deleter so a concurrently
                    // inserted junction/symlink aborts cleanup instead of
                    // allowing a recursive delete to cross the managed root.
                    AtomicFileHelper.TryDeleteDirectory(directory);
                }
                else
                {
                    AtomicFileHelper.TryDeleteFile(fullPath);
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
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    browserRoot,
                    _pathProvider.DataRoot,
                    "受控浏览器任务输出根目录无效。");
                var referencedDirectories = _jobs.Values
                    .Select(job => job.OutputPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path =>
                    {
                        try { return Path.GetDirectoryName(Path.GetFullPath(path)); }
                        catch { return string.Empty; }
                    })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToHashSet(PathBoundaryHelper.PathComparer);
                DateTimeOffset cutoffUtc = _timeProvider.GetUtcNow().AddDays(-_retentionOptions.RetentionDays);

                foreach (string kindDirectory in Directory.EnumerateDirectories(browserRoot))
                {
                    if (!IsReparsePointFreeBrowserPath(kindDirectory, browserRoot))
                    {
                        continue;
                    }

                    foreach (string jobDirectory in Directory.EnumerateDirectories(kindDirectory))
                    {
                        if (!IsReparsePointFreeBrowserPath(jobDirectory, browserRoot))
                        {
                            continue;
                        }

                        string fullDirectory = Path.GetFullPath(jobDirectory);
                        if (referencedDirectories.Contains(fullDirectory) ||
                            Directory.GetLastWriteTimeUtc(fullDirectory) >= cutoffUtc)
                        {
                            continue;
                        }

                        AtomicFileHelper.TryDeleteDirectory(fullDirectory);
                    }

                    if (!Directory.EnumerateFileSystemEntries(kindDirectory).Any())
                    {
                        AtomicFileHelper.TryDeleteDirectory(kindDirectory);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                // 受管输出清理是尽力而为，不能因为文件占用阻止 API 启动。
            }
        }

        private bool IsReparsePointFreeBrowserPath(string path, string browserRoot)
        {
            if (_pathProvider is null)
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!PathBoundaryHelper.IsWithinRoot(fullPath, browserRoot))
                {
                    return false;
                }

                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    fullPath,
                    _pathProvider.DataRoot,
                    "受控浏览器任务输出路径无效。");
                return true;
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                return false;
            }
        }
    }
}
