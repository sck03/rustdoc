using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// Guards runtime file operations before they exhaust the volume that owns the configured data root.
/// </summary>
internal static class RuntimeStorageBudget
{
    internal const long SafetyMarginBytes = 64L * 1024L * 1024L;
    internal const long StreamingCheckWindowBytes = 8L * 1024L * 1024L;

    internal static void EnsureAvailable(
        string path,
        long requiredBytes,
        string operation,
        Func<string, long>? getAvailableBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBytes));
        }

        string volumeRoot = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new InfrastructureServiceException($"无法解析{operation}所在磁盘。");
        try
        {
            long availableBytes = getAvailableBytes?.Invoke(volumeRoot) ?? GetAvailableBytes(volumeRoot, operation);
            if (availableBytes < 0)
            {
                throw new InfrastructureServiceException($"{operation}所在磁盘返回了无效的可用空间。");
            }
            if (availableBytes < requiredBytes)
            {
                throw new InsufficientStorageException(
                    $"{operation}需要至少 {FormatBytes(requiredBytes)} 可用空间，当前仅剩 {FormatBytes(availableBytes)}。请清理运行目录所在磁盘后重试。",
                    requiredBytes,
                    availableBytes);
            }
        }
        catch (InsufficientStorageException)
        {
            throw;
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InfrastructureServiceException($"无法读取{operation}所在磁盘的可用空间。", ex);
        }
    }

    internal static long WithSafetyMargin(params long[] sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        long total = SafetyMarginBytes;
        try
        {
            foreach (long size in sizes)
            {
                if (size < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizes));
                }
                total = checked(total + size);
            }
            return total;
        }
        catch (OverflowException ex)
        {
            throw new InsufficientStorageException(
                "运行文件大小超出可计算的安全预算。",
                long.MaxValue,
                0,
                ex);
        }
    }

    internal static IncrementalWriteGuard CreateIncrementalWriteGuard(
        string path,
        string operation) => new(path, operation);

    internal static long SumDirectoryBytes(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        string fullRoot = Path.GetFullPath(root);
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            var directory = new DirectoryInfo(current);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ServiceValidationException($"运行目录不能是符号链接或重解析点：{current}");
            }

            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ServiceValidationException($"运行目录包含符号链接或重解析点：{item.FullName}");
                }

                if (item is DirectoryInfo child)
                {
                    pending.Push(child.FullName);
                }
                else if (item is FileInfo file)
                {
                    total = checked(total + file.Length);
                }
            }
        }

        return total;
    }

    internal sealed class IncrementalWriteGuard
    {
        private readonly Action<long> _ensureAvailable;
        private long _nextCheckAtBytes;

        internal IncrementalWriteGuard(
            string path,
            string operation,
            Action<long>? ensureAvailable = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            _ensureAvailable = ensureAvailable
                ?? (requiredBytes => EnsureAvailable(path, requiredBytes, operation));
        }

        internal void EnsureCanWrite(long bytesWritten, int nextWriteBytes)
        {
            if (bytesWritten < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesWritten));
            }
            if (nextWriteBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nextWriteBytes));
            }
            if (bytesWritten < _nextCheckAtBytes)
            {
                return;
            }

            _ensureAvailable(WithSafetyMargin(StreamingCheckWindowBytes, nextWriteBytes));
            _nextCheckAtBytes = checked(bytesWritten + StreamingCheckWindowBytes);
        }
    }

    private static long GetAvailableBytes(string volumeRoot, string operation)
    {
        var drive = new DriveInfo(volumeRoot);
        if (!drive.IsReady)
        {
            throw new InfrastructureServiceException($"{operation}所在磁盘当前不可用。");
        }
        return drive.AvailableFreeSpace;
    }

    private static string FormatBytes(long bytes) =>
        $"{bytes / (1024d * 1024d * 1024d):0.##} GiB";
}
