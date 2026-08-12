using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 迁移过程的磁盘预算检查。每个阶段只按当前输入、清单和现有快照估算，
/// 避免在小包上硬编码一个过大的全盘预留值，同时在磁盘耗尽前停止操作。
/// </summary>
internal static class ServerMigrationStorageBudget
{
    internal const long SafetyMarginBytes = 64L * 1024L * 1024L;
    internal const long StreamingCheckWindowBytes = 8L * 1024L * 1024L;

    internal static void EnsureAvailable(
        string path,
        long requiredBytes,
        string operation)
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
            var drive = new DriveInfo(volumeRoot);
            if (!drive.IsReady)
            {
                throw new InfrastructureServiceException($"{operation}所在磁盘当前不可用。");
            }

            long availableBytes = drive.AvailableFreeSpace;
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
                "迁移文件大小超出可计算的安全预算。",
                long.MaxValue,
                0,
                ex);
        }
    }

    internal static IncrementalWriteGuard CreateIncrementalWriteGuard(
        string path,
        string operation) => new(path, operation);

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
                throw new ServiceValidationException($"迁移安全备份目录不能是符号链接或重解析点：{current}");
            }

            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ServiceValidationException($"迁移安全备份路径包含符号链接或重解析点：{item.FullName}");
                }

                if (item is DirectoryInfo)
                {
                    pending.Push(item.FullName);
                }
                else if (item is FileInfo file)
                {
                    total = checked(total + file.Length);
                }
            }
        }

        return total;
    }

    internal static long SumManifestBytes(ServerMigrationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        long total = 0;
        foreach (ServerMigrationFileManifest file in manifest.Files ?? [])
        {
            if (file.SizeBytes < 0)
            {
                throw new InvalidDataException($"迁移清单文件大小无效：{file.RelativePath}");
            }
            total = checked(total + file.SizeBytes);
        }
        return total;
    }

    private static string FormatBytes(long bytes) =>
        $"{bytes / (1024d * 1024d * 1024d):0.##} GiB";
}
