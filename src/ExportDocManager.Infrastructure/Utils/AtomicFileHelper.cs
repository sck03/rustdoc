using System.Text;
using System.Threading;

namespace ExportDocManager.Utils
{
    public static class AtomicFileHelper
    {
        private const int ReplaceFileMaxAttempts = 5;
        private const int ReplaceFileRetryDelayMilliseconds = 50;

        public static string GetSiblingTempFilePath(string targetPath)
        {
            var fullTargetPath = Path.GetFullPath(targetPath);
            var targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("无法解析目标文件所在目录。", nameof(targetPath));
            }

            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                targetDirectory,
                "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");
            Directory.CreateDirectory(targetDirectory);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                targetDirectory,
                "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");

            var targetFileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullTargetPath);
            var targetExtension = Path.GetExtension(fullTargetPath);
            return Path.Combine(targetDirectory, $".{targetFileNameWithoutExtension}.{Guid.NewGuid():N}.tmp{targetExtension}");
        }

        public static void ReplaceFile(string sourcePath, string targetPath)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            ArgumentNullException.ThrowIfNull(targetPath);

            var fullSourcePath = Path.GetFullPath(sourcePath);
            var fullTargetPath = Path.GetFullPath(targetPath);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullSourcePath,
                "原子文件源路径不能经过符号链接、目录联接或其他重解析点。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullTargetPath,
                "原子文件目标路径不能经过符号链接、目录联接或其他重解析点。");
            var targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(targetDirectory);
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");
            }

            ReplaceFileWithRetry(fullSourcePath, fullTargetPath);
        }

        /// <summary>
        /// Replaces a file without blocking a request thread while a desktop process has
        /// the destination briefly open.  The source and destination must be siblings so
        /// the rename remains a single-volume operation.
        /// </summary>
        public static async Task ReplaceFileAsync(
            string sourcePath,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullTargetPath = Path.GetFullPath(targetPath);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullSourcePath,
                "原子文件源路径不能经过符号链接、目录联接或其他重解析点。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullTargetPath,
                "原子文件目标路径不能经过符号链接、目录联接或其他重解析点。");
            string? targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(targetDirectory);
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "原子文件目标目录不能经过符号链接、目录联接或其他重解析点。");
            }

            for (int attempt = 0; attempt < ReplaceFileMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ReplaceFileOnce(fullSourcePath, fullTargetPath);
                    return;
                }
                catch (IOException) when (attempt < ReplaceFileMaxAttempts - 1)
                {
                    await Task.Delay(ReplaceFileRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < ReplaceFileMaxAttempts - 1)
                {
                    await Task.Delay(ReplaceFileRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            // The final attempt deliberately propagates its original exception.  A caller
            // must never observe a successful atomic write when the rename did not happen.
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceFileOnce(fullSourcePath, fullTargetPath);
        }

        private static void ReplaceFileWithRetry(string fullSourcePath, string fullTargetPath)
        {
            for (var attempt = 0; attempt < ReplaceFileMaxAttempts; attempt++)
            {
                try
                {
                    ReplaceFileOnce(fullSourcePath, fullTargetPath);
                    return;
                }
                catch (IOException) when (attempt < ReplaceFileMaxAttempts - 1)
                {
                    Thread.Sleep(ReplaceFileRetryDelayMilliseconds);
                }
                catch (UnauthorizedAccessException) when (attempt < ReplaceFileMaxAttempts - 1)
                {
                    Thread.Sleep(ReplaceFileRetryDelayMilliseconds);
                }
            }
        }

        private static void ReplaceFileOnce(string fullSourcePath, string fullTargetPath)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullSourcePath,
                "原子文件源路径不能经过符号链接、目录联接或其他重解析点。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullTargetPath,
                "原子文件目标路径不能经过符号链接、目录联接或其他重解析点。");
            if (PhysicalPathComparison.Comparer.Equals(fullSourcePath, fullTargetPath))
            {
                throw new IOException("原子替换的源文件和目标文件不能相同。");
            }

            if (File.Exists(fullTargetPath))
            {
                File.Replace(
                    fullSourcePath,
                    fullTargetPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(fullSourcePath, fullTargetPath);
            }
        }

        public static void WriteFileAtomic(string targetPath, Action<string> writeTempFile)
        {
            ArgumentNullException.ThrowIfNull(writeTempFile);

            WriteFileAtomic<object?>(targetPath, tempPath =>
            {
                writeTempFile(tempPath);
                return null;
            });
        }

        public static void WriteFileAtomic(
            string targetPath,
            Action<string, CancellationToken> writeTempFile,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writeTempFile);

            WriteFileAtomic<object?>(
                targetPath,
                (tempPath, ct) =>
                {
                    writeTempFile(tempPath, ct);
                    return null;
                },
                cancellationToken);
        }

        public static T WriteFileAtomic<T>(string targetPath, Func<string, T> writeTempFile)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            ArgumentNullException.ThrowIfNull(writeTempFile);

            var tempPath = GetSiblingTempFilePath(targetPath);
            try
            {
                var result = writeTempFile(tempPath);
                ReplaceFile(tempPath, targetPath);
                return result;
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        public static T WriteFileAtomic<T>(
            string targetPath,
            Func<string, CancellationToken, T> writeTempFile,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            ArgumentNullException.ThrowIfNull(writeTempFile);

            var tempPath = GetSiblingTempFilePath(targetPath);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = writeTempFile(tempPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ReplaceFile(tempPath, targetPath);
                return result;
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        public static Task WriteFileAtomicAsync(
            string targetPath,
            Func<string, CancellationToken, Task> writeTempFileAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writeTempFileAsync);

            return WriteFileAtomicAsync<object?>(
                targetPath,
                async (tempPath, ct) =>
                {
                    await writeTempFileAsync(tempPath, ct);
                    return null;
                },
                cancellationToken);
        }

        public static async Task<T> WriteFileAtomicAsync<T>(
            string targetPath,
            Func<string, CancellationToken, Task<T>> writeTempFileAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            ArgumentNullException.ThrowIfNull(writeTempFileAsync);

            var tempPath = GetSiblingTempFilePath(targetPath);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await writeTempFileAsync(tempPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await ReplaceFileAsync(tempPath, targetPath, cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        public static void WriteAllTextAtomic(string targetPath, string content, Encoding? encoding = null)
        {
            WriteFileAtomic(
                targetPath,
                tempPath => File.WriteAllText(tempPath, content ?? string.Empty, encoding ?? Encoding.UTF8));
        }

        public static async Task WriteAllTextAtomicAsync(
            string targetPath,
            string content,
            Encoding? encoding = null,
            CancellationToken cancellationToken = default)
        {
            await WriteFileAtomicAsync(
                targetPath,
                (tempPath, ct) =>
                    File.WriteAllTextAsync(tempPath, content ?? string.Empty, encoding ?? Encoding.UTF8, ct),
                cancellationToken);
        }

        public static void TryDeleteFile(string filePath)
        {
            TryDeleteFileInternal(filePath, maxAttempts: 3, retryDelayMilliseconds: 50);
        }

        public static void TryDeleteDirectory(string directoryPath)
        {
            TryDeleteDirectoryInternal(directoryPath, maxAttempts: 3, retryDelayMilliseconds: 50);
        }

        /// <summary>
        /// Best-effort asynchronous cleanup for request/background-task finally blocks.
        /// The boolean result is intentionally available to callers that need to log or
        /// surface a degraded cleanup state; the legacy void helpers remain for simple
        /// fire-and-forget cleanup sites.
        /// </summary>
        public static Task<bool> TryDeleteFileAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            TryDeleteFileAsyncCore(filePath, cancellationToken);

        public static Task<bool> TryDeleteDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default) =>
            TryDeleteDirectoryAsyncCore(directoryPath, cancellationToken);

        private static async Task<bool> TryDeleteFileAsyncCore(
            string filePath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return true;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(filePath)) return true;
                    ResetFileAttributes(filePath);
                    File.Delete(filePath);
                    return !File.Exists(filePath);
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < 2)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }

            return !File.Exists(filePath);
        }

        private static async Task<bool> TryDeleteDirectoryAsyncCore(
            string directoryPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)) return true;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.Exists(directoryPath)) return true;
                    DeleteDirectoryTree(directoryPath);
                    return !Directory.Exists(directoryPath);
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < 2)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }

            return !Directory.Exists(directoryPath);
        }

        private static void TryDeleteFileInternal(string filePath, int maxAttempts, int retryDelayMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            maxAttempts = Math.Max(1, maxAttempts);
            retryDelayMilliseconds = Math.Max(0, retryDelayMilliseconds);

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return;
                    }

                    ResetFileAttributes(filePath);
                    File.Delete(filePath);
                    return;
                }
                catch when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
                catch
                {
                    return;
                }
            }
        }

        private static void TryDeleteDirectoryInternal(string directoryPath, int maxAttempts, int retryDelayMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            maxAttempts = Math.Max(1, maxAttempts);
            retryDelayMilliseconds = Math.Max(0, retryDelayMilliseconds);

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        return;
                    }

                    DeleteDirectoryTree(directoryPath);
                    return;
                }
                catch when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
                catch
                {
                    return;
                }
            }
        }

        private static void DeleteDirectoryTree(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            // Do not use SearchOption.AllDirectories here.  It can follow a
            // junction/symlink that is created between enumeration and delete,
            // allowing cleanup of a temporary directory to reach outside its
            // declared root.  An explicit post-order walk lets us reject every
            // reparse point before touching or descending into it.
            var pending = new Stack<(string Path, bool Exit)>();
            pending.Push((Path.GetFullPath(directoryPath), Exit: false));
            while (pending.Count > 0)
            {
                var (currentPath, exit) = pending.Pop();
                FileAttributes attributes = File.GetAttributes(currentPath);
                EnsureNotReparsePoint(currentPath, attributes);

                if (exit)
                {
                    ResetFileAttributes(currentPath);
                    // A non-recursive delete is important here: even if a
                    // reparse point is swapped in after enumeration, the OS
                    // removes only that directory entry and never traverses
                    // the target of a junction/symlink.
                    Directory.Delete(currentPath, recursive: false);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    ResetFileAttributes(currentPath);
                    File.Delete(currentPath);
                    continue;
                }

                pending.Push((currentPath, Exit: true));
                var directory = new DirectoryInfo(currentPath);
                IEnumerable<FileSystemInfo> entries = directory.EnumerateFileSystemInfos(
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = false,
                        ReturnSpecialDirectories = false,
                        AttributesToSkip = 0
                    });

                foreach (FileSystemInfo entry in entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Reverse())
                {
                    string childPath = Path.GetFullPath(entry.FullName);
                    FileAttributes childAttributes = File.GetAttributes(childPath);
                    EnsureNotReparsePoint(childPath, childAttributes);
                    if ((childAttributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push((childPath, Exit: false));
                    }
                    else
                    {
                        ResetFileAttributes(childPath);
                        File.Delete(childPath);
                    }
                }
            }
        }

        private static void EnsureNotReparsePoint(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"清理目录不能包含符号链接、目录联接或其他重解析点：{path}");
            }
        }

        private static void ResetFileAttributes(string path)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
            catch
            {
            }
        }
    }
}
