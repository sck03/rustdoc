using System.Diagnostics;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Utils
{
    internal static class CrossProcessFileLock
    {
        private const int RetryDelayMilliseconds = 50;

        public static async Task<FileStream> AcquireAsync(
            string lockFilePath,
            CancellationToken cancellationToken = default,
            TimeSpan? waitTimeout = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
            TimeSpan timeout = waitTimeout ?? TimeSpan.FromSeconds(30);
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(waitTimeout));

            string fullPath = Path.GetFullPath(lockFilePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("无法解析运行锁目录。", nameof(lockFilePath));
            }

            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                directory,
                "运行锁目录不能经过符号链接、目录联接或其他重解析点。");
            Directory.CreateDirectory(directory);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullPath,
                "运行锁文件不能经过符号链接、目录联接或其他重解析点。");

            long startedAt = Stopwatch.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(
                        fullPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.Asynchronous);
                }
                catch (IOException exception) when (IsLockContention(exception))
                {
                    if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
                    {
                        throw new ServiceTimeoutException("等待运行文件锁超时，请稍后重试。", exception);
                    }
                    await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static bool IsLockContention(IOException exception)
        {
            int errorCode = exception.HResult & 0xFFFF;
            // FileStream reports Windows sharing/lock violations or the native
            // EWOULDBLOCK errno (11 on Linux, 35 on macOS). Other I/O failures
            // must retain their original classification instead of waiting.
            return errorCode is 32 or 33 ||
                   !OperatingSystem.IsWindows() && errorCode == (OperatingSystem.IsMacOS() ? 35 : 11);
        }
    }
}
