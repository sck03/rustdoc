using System.IO.Compression;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ExportDocManager.DataAccess;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        private const long SupportPackageLogFileByteLimit = 8L * 1024 * 1024;
        private const long SupportPackageTotalLogByteLimit = 32L * 1024 * 1024;
        // Keep optional support-package payloads within the same 4 GiB recovery
        // envelope without coupling maintenance to the SingleWindow crypto type.
        private const long SupportPackageOptionalBackupByteLimit =
            4L * 1024L * 1024L * 1024L + 16L * 1024L * 1024L;

        private async Task WriteJobSnapshotAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            if (_backgroundJobs == null)
            {
                await WriteJsonEntryAsync(archive, "diagnostics/background-jobs.json", Array.Empty<BackgroundJobSnapshot>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var jobs = await _backgroundJobs.QueryAsync(new BackgroundJobQuery
            {
                PageNumber = 1,
                PageSize = 200
            }, cancellationToken).ConfigureAwait(false);
            await WriteJsonEntryAsync(archive, "diagnostics/background-jobs.json", jobs, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteRecentLogsAsync(ZipArchive archive, CancellationToken cancellationToken)
        {
            if (!TryGetManagedDirectory(_pathProvider.LogRoot, out DirectoryInfo? logDirectory))
            {
                await WriteJsonEntryAsync(archive, "logs/log-index.json", Array.Empty<object>(), cancellationToken).ConfigureAwait(false);
                return;
            }

            FileInfo[] allLogs;
            try
            {
                allLogs = logDirectory
                    .EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => IsTextLog(file.Name) && IsRegularSupportPackageFile(file))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await WriteJsonEntryAsync(archive, "logs/log-index.json", Array.Empty<object>(), cancellationToken).ConfigureAwait(false);
                return;
            }
            var priorityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "api-sidecar.stdout.log",
                "api-sidecar.stderr.log",
                "frontend-errors.log",
                "tauri-errors.log",
                "tauri-bootstrap-error.log"
            };
            var logs = allLogs
                .Where(file => priorityNames.Contains(file.Name))
                .Concat(allLogs.OrderByDescending(file => file.LastWriteTimeUtc).Take(20))
                .GroupBy(file => file.Name, PhysicalPathComparison.Comparer)
                .Select(group => group.First())
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            long remainingBytes = SupportPackageTotalLogByteLimit;
            var index = new List<object>();
            foreach (var file in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remainingBytes <= 0)
                {
                    break;
                }

                try
                {
                    PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        file.FullName,
                        _pathProvider.DataRoot,
                        "支持包日志不能包含符号链接或重解析点。");
                    await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    long originalLength = source.Length;
                    long includedBytes = Math.Min(
                        Math.Min(originalLength, SupportPackageLogFileByteLimit),
                        remainingBytes);
                    if (includedBytes <= 0)
                    {
                        continue;
                    }

                    source.Seek(Math.Max(0, originalLength - includedBytes), SeekOrigin.Begin);
                    byte[] sourceTail = new byte[checked((int)includedBytes)];
                    int sourceTailBytes = 0;
                    while (sourceTailBytes < sourceTail.Length)
                    {
                        int read = await source.ReadAsync(
                                sourceTail.AsMemory(sourceTailBytes, sourceTail.Length - sourceTailBytes),
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        sourceTailBytes += read;
                    }

                    byte[] sanitizedTail = DiagnosticLogSanitizer.SanitizeUtf8(
                        sourceTail.AsSpan(0, sourceTailBytes),
                        checked((int)Math.Min(includedBytes, remainingBytes)));
                    var entry = archive.CreateEntry($"logs/{file.Name}", CompressionLevel.Fastest);
                    await using var target = entry.Open();
                    await target.WriteAsync(sanitizedTail, cancellationToken).ConfigureAwait(false);
                    long copiedBytes = sanitizedTail.Length;
                    index.Add(new
                    {
                        file.Name,
                        OriginalLength = originalLength,
                        SourceTailBytes = sourceTailBytes,
                        IncludedBytes = copiedBytes,
                        TailOnly = sourceTailBytes < originalLength,
                        Sanitized = true,
                        file.LastWriteTimeUtc
                    });
                    remainingBytes -= copiedBytes;
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
                {
                    // Logs rotate while a support package is being created. Skip a file that
                    // disappeared or became unreadable and keep the rest of the package useful.
                }
            }

            await WriteJsonEntryAsync(archive, "logs/log-index.json", index, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<long> CopyAtMostAsync(
            Stream source,
            Stream target,
            long maximumBytes,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            long remaining = Math.Max(0, maximumBytes);
            long copied = 0;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                remaining -= read;
                copied += read;
            }

            return copied;
        }

        private object CreateRuntimeDiagnostics()
        {
            using var currentProcess = Process.GetCurrentProcess();
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return new
            {
                CreatedAt = _clock.UtcNow,
                Environment.MachineName,
                Environment.OSVersion.Platform,
                Environment.OSVersion.VersionString,
                Environment.Is64BitOperatingSystem,
                Environment.Is64BitProcess,
                Environment.ProcessorCount,
                Environment.Version,
                ProcessWorkingSet64 = currentProcess.WorkingSet64,
                ProcessPrivateMemorySize64 = currentProcess.PrivateMemorySize64,
                GCTotalAvailableMemoryBytes = gcMemoryInfo.TotalAvailableMemoryBytes,
                WebView2LoaderVersion = ReadFileVersion(Path.Combine(_pathProvider.AppRoot, "WebView2Loader.dll")),
                ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                OSArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
            };
        }

        private object CreateDatabaseDiagnostics()
        {
            return new
            {
                Provider = DatabaseModeHelper.GetCurrentModeText(_databaseSettings),
                SharedDatabaseEnabled = IsSharedDatabaseEnabled,
                PostgreSqlHost = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlHost),
                PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(_databaseSettings.PostgreSqlPort),
                PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase),
                PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername),
                HasPostgreSqlPassword = !string.IsNullOrEmpty(_databaseSettings.PostgreSqlPassword),
                SQLiteDatabaseFileName = DatabaseModeHelper.UsesPostgreSql(_databaseSettings)
                    ? string.Empty
                    : DbHelper.NormalizeSqliteDatabaseFileName(_databaseSettings.SqliteDatabaseFileName)
            };
        }

        private object CreatePathDiagnostics()
        {
            return new
            {
                _pathProvider.AppRoot,
                _pathProvider.DataRoot,
                _pathProvider.DatabaseRoot,
                _pathProvider.BackupRoot,
                PostgreSqlBackupRoot,
                PostgreSqlRestorePlanRoot,
                SupportPackageRoot,
                _pathProvider.LogRoot,
                _pathProvider.TemplateRoot,
                _pathProvider.ResourceRoot,
                _pathProvider.BrowserRoot,
                _pathProvider.ToolRoot,
                _pathProvider.OcrModelRoot,
                _pathProvider.SingleWindowRoot,
                _pathProvider.CacheRoot,
                _pathProvider.SecurityRoot,
                _pathProvider.WebViewRoot
            };
        }

        private async Task WriteOptionalSupportPackageEntriesAsync(
            ZipArchive archive,
            SupportPackageOptions options,
            CancellationToken cancellationToken)
        {
            var optionalIndex = new List<object>();
            if (options.IncludeLatestDatabaseBackup)
            {
                var latestBackup = GetLatestAnyDatabaseBackup();
                if (latestBackup != null)
                {
                    if (latestBackup.Length > SupportPackageOptionalBackupByteLimit)
                    {
                        throw new InvalidDataException(
                            $"最新数据库备份超过支持包可选文件上限 {SupportPackageOptionalBackupByteLimit / (1024 * 1024)} MB。请单独使用数据库备份下载功能。");
                    }

                    PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        latestBackup.FullName,
                        _pathProvider.DataRoot,
                        "支持包数据库备份不能包含符号链接或重解析点。");
                    optionalIndex.Add(new
                    {
                        kind = "latest-database-backup",
                        latestBackup.Name,
                        latestBackup.Length,
                        latestBackup.LastWriteTimeUtc
                    });
                    var entry = archive.CreateEntry($"optional/database-backup/{latestBackup.Name}", CompressionLevel.NoCompression);
                    await using var source = new FileStream(latestBackup.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using var target = entry.Open();
                    try
                    {
                        await BoundedStreamHelper.CopyToAsync(
                            source,
                            target,
                            SupportPackageOptionalBackupByteLimit,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (PayloadLimitExceededException ex)
                    {
                        throw new InvalidDataException(
                            "数据库备份在生成支持包期间增长并超过可选文件上限。请单独使用数据库备份下载功能。",
                            ex);
                    }
                }
            }

            if (options.IncludeSampleFiles)
            {
                optionalIndex.Add(new
                {
                    kind = "sample-files",
                    message = "当前未配置自动样张目录。样张请由用户另行选择后提供，避免默认打包业务附件。"
                });
            }

            await WriteJsonEntryAsync(archive, "optional/index.json", optionalIndex, cancellationToken).ConfigureAwait(false);
        }

        private FileInfo? GetLatestAnyDatabaseBackup()
        {
            var candidates = new List<FileInfo>();
            if (TryGetManagedDirectory(_pathProvider.BackupRoot, out DirectoryInfo? backupDirectory))
            {
                try
                {
                    candidates.AddRange(backupDirectory
                        .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(file => string.Equals(file.Extension, ".zip", StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            try
            {
                if (TryGetManagedDirectory(PostgreSqlBackupRoot, out DirectoryInfo? postgreSqlDirectory))
                {
                    try
                    {
                        candidates.AddRange(postgreSqlDirectory
                            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                            .Where(file => string.Equals(file.Extension, ".dump", StringComparison.OrdinalIgnoreCase)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }

            return candidates
                .Where(IsRegularSupportPackageFile)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }

        private bool TryGetManagedDirectory(string path, [NotNullWhen(true)] out DirectoryInfo? directory)
        {
            directory = null;
            try
            {
                string dataRoot = Path.GetFullPath(_pathProvider.DataRoot);
                string fullPath = PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    path,
                    dataRoot,
                    "支持包读取目录不能包含符号链接或重解析点。");
                if (!Directory.Exists(fullPath))
                {
                    return false;
                }

                fullPath = PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    fullPath,
                    dataRoot,
                    "支持包读取目录不能包含符号链接或重解析点。");
                directory = new DirectoryInfo(fullPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }

        private static bool IsRegularSupportPackageFile(FileInfo file)
        {
            try
            {
                return file.Exists &&
                    (file.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

    }
}
