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

            Directory.CreateDirectory(targetDirectory);

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
            var targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            ReplaceFileWithRetry(fullSourcePath, fullTargetPath);
        }

        private static void ReplaceFileWithRetry(string fullSourcePath, string fullTargetPath)
        {
            for (var attempt = 0; attempt < ReplaceFileMaxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(fullTargetPath))
                    {
                        File.Replace(fullSourcePath, fullTargetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(fullSourcePath, fullTargetPath);
                    }

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

        public static void WriteFileAtomic(string targetPath, Action<string> writeTempFile)
        {
            ArgumentNullException.ThrowIfNull(writeTempFile);

            WriteFileAtomic<object>(targetPath, tempPath =>
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

            WriteFileAtomic<object>(
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

            return WriteFileAtomicAsync<object>(
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
                ReplaceFile(tempPath, targetPath);
                return result;
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        public static void WriteAllTextAtomic(string targetPath, string content, Encoding encoding = null)
        {
            WriteFileAtomic(
                targetPath,
                tempPath => File.WriteAllText(tempPath, content ?? string.Empty, encoding ?? Encoding.UTF8));
        }

        public static async Task WriteAllTextAtomicAsync(
            string targetPath,
            string content,
            Encoding encoding = null,
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

                    ResetDirectoryAttributes(directoryPath);
                    Directory.Delete(directoryPath, recursive: true);
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

        private static void ResetDirectoryAttributes(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                ResetFileAttributes(filePath);
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
            {
                ResetFileAttributes(childDirectory);
            }

            ResetFileAttributes(directoryPath);
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
