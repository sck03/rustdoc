using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.Data.Sqlite;
using Serilog;

namespace ExportDocManager.Services.Infrastructure
{
    public class BackupService : IBackupService
    {
        internal static readonly SemaphoreSlim SqliteMaintenanceGate = new(1, 1);
        private static readonly JsonSerializerOptions RestoreMarkerJsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly string _backupDirectory;
        private readonly string _databasePath;
        private readonly string _databaseFileName;
        private readonly bool _usesSqlite;
        private readonly IAppPathProvider _pathProvider;
        private readonly Regex _managedBackupNamePattern;

        public BackupService(
            DatabaseConnectionSettings databaseSettings,
            string backupDirectory = null,
            string databasePath = null)
            : this(databaseSettings, new RuntimeAppPathProvider(), backupDirectory, databasePath)
        {
        }

        public BackupService(
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            string backupDirectory = null,
            string databasePath = null)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(pathProvider);
            _pathProvider = pathProvider;
            _usesSqlite = !DatabaseModeHelper.UsesPostgreSql(databaseSettings);

            if (_usesSqlite)
            {
                _databasePath = string.IsNullOrWhiteSpace(databasePath)
                    ? DbHelper.ResolveRuntimeSqliteDatabasePath(pathProvider, databaseSettings.SqliteDatabaseFileName)
                    : Path.GetFullPath(databasePath);
                _databaseFileName = Path.GetFileName(_databasePath);
                _managedBackupNamePattern = BuildManagedBackupNamePattern(_databaseFileName);
            }
            else
            {
                _databasePath = string.Empty;
                _databaseFileName = string.Empty;
            }

            _backupDirectory = string.IsNullOrWhiteSpace(backupDirectory)
                ? pathProvider.BackupRoot
                : backupDirectory;

            Directory.CreateDirectory(_backupDirectory);
        }

        public async Task<DatabaseBackupResult> BackupDatabaseAsync(CancellationToken cancellationToken = default)
        {
            await SqliteMaintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_usesSqlite)
                {
                    Log.Information("Skipping local database backup because the current provider is PostgreSQL.");
                    return new DatabaseBackupResult(
                        Success: false,
                        Skipped: true,
                        Message: "当前使用 PostgreSQL，共享数据库请使用 PostgreSQL 维护中心备份。",
                        FilePath: string.Empty);
                }

                if (!File.Exists(_databasePath))
                {
                    Log.Warning("Database file not found at {Path}, skipping backup.", _databasePath);
                    return new DatabaseBackupResult(
                        Success: false,
                        Skipped: true,
                        Message: "当前 SQLite 数据库文件不存在，未创建备份。",
                        FilePath: string.Empty);
                }

                string backupPath = await CreateConsistentBackupCoreAsync(
                    namePrefix: string.Empty,
                    cancellationToken).ConfigureAwait(false);

                Log.Information("Database backed up successfully to {Path}", backupPath);
                return new DatabaseBackupResult(
                    Success: true,
                    Skipped: false,
                    Message: $"数据库一致性备份已创建：{Path.GetFileName(backupPath)}",
                    FilePath: backupPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to backup database.");
                return new DatabaseBackupResult(
                    Success: false,
                    Skipped: false,
                    Message: $"数据库备份失败：{ex.Message}",
                    FilePath: string.Empty);
            }
            finally
            {
                SqliteMaintenanceGate.Release();
            }
        }

        public async Task<DatabaseBackupImportResult> ImportBackupAsync(
            string sourceFilePath,
            string preferredFileName = null,
            CancellationToken cancellationToken = default)
        {
            if (!_usesSqlite)
            {
                throw new NotSupportedException("当前数据库类型为 PostgreSQL，不能导入 SQLite 数据库备份。");
            }

            string sourcePath = Path.GetFullPath(sourceFilePath ?? string.Empty);
            if (!File.Exists(sourcePath))
            {
                throw new ResourceNotFoundException("找不到待导入的数据库备份文件。", new FileNotFoundException(sourcePath));
            }

            await SqliteMaintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string snapshotPath = Path.Combine(
                _backupDirectory,
                $".{BuildBackupNameToken(_databaseFileName)}.{Guid.NewGuid():N}.import.db");
            string targetPath = string.Empty;
            bool importSucceeded = false;
            try
            {
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateZipBackupStructure(sourcePath, _databaseFileName);
                }
                await ExtractDatabaseSnapshotAsync(sourcePath, snapshotPath, cancellationToken).ConfigureAwait(false);
                await ValidateSqliteSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);

                targetPath = Path.Combine(_backupDirectory, BuildImportedBackupFileName(preferredFileName));
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await FileCopyHelper.CopyAsync(
                        sourcePath,
                        targetPath,
                        overwrite: false,
                        sourceFileShare: FileShare.Read,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ZipArchiveHelper.CreateFromFilesAsync(
                        new[] { (SourcePath: snapshotPath, EntryName: _databaseFileName) },
                        targetPath,
                        cancellationToken).ConfigureAwait(false);
                }
                if (!File.Exists(targetPath) || new FileInfo(targetPath).Length <= 0)
                {
                    throw new InfrastructureServiceException("导入的数据库备份未成功写入。 ");
                }

                RuntimeFilePermissionHelper.RestrictFile(targetPath);
                importSucceeded = true;
                return new DatabaseBackupImportResult(
                    Success: true,
                    Message: $"数据库备份已验证并导入：{Path.GetFileName(targetPath)}",
                    FilePath: targetPath,
                    SizeBytes: new FileInfo(targetPath).Length);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(snapshotPath);
                if (!importSucceeded && !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                {
                    AtomicFileHelper.TryDeleteFile(targetPath);
                }
                SqliteMaintenanceGate.Release();
            }
        }

        public void CleanOldBackups(int daysToKeep)
        {
            if (!_usesSqlite || daysToKeep <= 0 || !Directory.Exists(_backupDirectory))
            {
                return;
            }

            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

                foreach (var file in GetCandidateBackupFiles())
                {
                    if (file.LastWriteTimeUtc < cutoffDate)
                    {
                        file.Delete();
                        Log.Information("Deleted old backup: {FileName}", file.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clean old backups.");
                throw new InfrastructureServiceException("清理旧数据库备份失败，请检查备份目录权限和磁盘状态。", ex);
            }
        }

        public List<string> GetAvailableBackups()
        {
            try
            {
                if (!Directory.Exists(_backupDirectory))
                {
                    return new List<string>();
                }

                return GetCandidateBackupFiles()
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(file => file.FullName)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get available backups.");
                throw new InfrastructureServiceException("读取数据库备份列表失败，请检查备份目录权限和磁盘状态。", ex);
            }
        }

        public async Task<DatabaseRestoreScheduleResult> ScheduleRestoreAsync(
            string backupFilePath,
            CancellationToken cancellationToken = default)
        {
            if (!_usesSqlite)
            {
                throw new NotSupportedException("当前数据库类型为 PostgreSQL，暂不支持通过本地 SQLite 备份文件还原。");
            }

            if (!File.Exists(backupFilePath))
            {
                throw new FileNotFoundException("Backup file not found.", backupFilePath);
            }

            await SqliteMaintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string stagedRestorePath = SqlitePendingRestoreManager.GetStagedRestorePath(_databasePath);
            string markerPath = SqlitePendingRestoreManager.GetMarkerPath(_databasePath);
            try
            {
                if (File.Exists(markerPath))
                {
                    throw new ResourceConflictException(
                        "已有 SQLite 数据库还原任务等待下次启动执行。请先重启程序完成该任务，再安排新的还原。");
                }

                if (SingleWindowDisasterRecoveryManager.HasPendingRestore(_pathProvider))
                {
                    throw new ResourceConflictException("已有持卡机灾难恢复任务等待下次启动执行，请先重启完成恢复。");
                }

                string safetyBackupPath = File.Exists(_databasePath)
                    ? await CreateConsistentBackupCoreAsync("pre-restore", cancellationToken).ConfigureAwait(false)
                    : string.Empty;

                await ExtractDatabaseSnapshotAsync(
                    backupFilePath,
                    stagedRestorePath,
                    cancellationToken).ConfigureAwait(false);
                await ValidateSqliteSnapshotAsync(stagedRestorePath, cancellationToken).ConfigureAwait(false);

                var marker = new SqlitePendingRestoreMarker
                {
                    TargetDatabasePath = Path.GetFullPath(_databasePath),
                    StagedDatabasePath = Path.GetFullPath(stagedRestorePath),
                    SourceBackupFileName = Path.GetFileName(backupFilePath),
                    SafetyBackupFilePath = safetyBackupPath,
                    StagedSha256 = await ComputeSha256Async(stagedRestorePath, cancellationToken).ConfigureAwait(false),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await AtomicFileHelper.WriteAllTextAtomicAsync(
                    markerPath,
                    JsonSerializer.Serialize(marker, RestoreMarkerJsonOptions),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                Log.Information(
                    "Database restore scheduled from {Path}; it will be applied before the next database connection is opened.",
                    backupFilePath);
                return new DatabaseRestoreScheduleResult(
                    Success: true,
                    Message: "数据库还原任务已安全排队。请立即重启桌面程序；程序会在建立数据库连接前离线还原，并清理旧 WAL/SHM 文件。",
                    BackupFilePath: backupFilePath,
                    SafetyBackupFilePath: safetyBackupPath);
            }
            catch (Exception ex)
            {
                if (!File.Exists(markerPath))
                {
                    AtomicFileHelper.TryDeleteFile(stagedRestorePath);
                }
                Log.Error(ex, "Failed to schedule database restore.");
                throw;
            }
            finally
            {
                SqliteMaintenanceGate.Release();
            }
        }

        private async Task<string> CreateConsistentBackupCoreAsync(
            string namePrefix,
            CancellationToken cancellationToken)
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff");
            string prefix = string.IsNullOrWhiteSpace(namePrefix) ? string.Empty : $"{namePrefix.Trim()}_";
            string backupFileName = $"{timestamp}_{prefix}{BuildBackupNameToken(_databaseFileName)}_{Guid.NewGuid():N}.zip";
            string backupPath = Path.Combine(_backupDirectory, backupFileName);
            string snapshotPath = Path.Combine(
                _backupDirectory,
                $".{BuildBackupNameToken(_databaseFileName)}.{Guid.NewGuid():N}.snapshot.db");

            try
            {
                await CreateSqliteOnlineSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                await ZipArchiveHelper.CreateFromFilesAsync(
                    new[] { (SourcePath: snapshotPath, EntryName: _databaseFileName) },
                    backupPath,
                    cancellationToken).ConfigureAwait(false);
                if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                {
                    throw new IOException("备份压缩包未成功写入。" );
                }

                return backupPath;
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(snapshotPath);
            }
        }

        private async Task CreateSqliteOnlineSnapshotAsync(
            string snapshotPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceBuilder = new SqliteConnectionStringBuilder(DbHelper.BuildConnectionString(_databasePath))
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            var destinationBuilder = new SqliteConnectionStringBuilder(DbHelper.BuildConnectionString(snapshotPath))
            {
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            await using var source = new SqliteConnection(sourceBuilder.ToString());
            await using var destination = new SqliteConnection(destinationBuilder.ToString());
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            await Task.Run(
                    () => source.BackupDatabase(destination),
                    cancellationToken)
                .ConfigureAwait(false);
            await ValidateOpenSqliteConnectionAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        private async Task ExtractDatabaseSnapshotAsync(
            string backupFilePath,
            string stagedRestorePath,
            CancellationToken cancellationToken)
        {
            await AtomicFileHelper.WriteFileAtomicAsync(
                stagedRestorePath,
                async (tempPath, ct) =>
                {
                    if (backupFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using var archive = ZipFile.OpenRead(backupFilePath);
                        var entries = archive.Entries
                            .Where(entry => entry.Name.Equals(_databaseFileName, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        if (entries.Length != 1)
                        {
                            throw new InvalidDataException(
                                $"备份压缩包必须且只能包含一个当前数据库文件 '{_databaseFileName}'。");
                        }

                        var entry = entries[0];
                        if (entry.Length <= 0 || entry.Length > 4L * 1024L * 1024L * 1024L)
                        {
                            throw new InvalidDataException("备份数据库文件大小无效。" );
                        }

                        await using var entryStream = entry.Open();
                        await using var outputStream = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await entryStream.CopyToAsync(outputStream, ct).ConfigureAwait(false);
                        await outputStream.FlushAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await FileCopyHelper.CopyAsync(
                            backupFilePath,
                            tempPath,
                            overwrite: true,
                            sourceFileShare: FileShare.Read,
                            cancellationToken: ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateZipBackupStructure(string archivePath, string databaseFileName)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var fileEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToArray();
            if (fileEntries.Length != 1 ||
                !string.Equals(fileEntries[0].FullName, databaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceValidationException(
                    $"数据库备份压缩包必须且只能包含根目录数据库文件 '{databaseFileName}'。 ");
            }

            if (fileEntries[0].Length <= 0 || fileEntries[0].Length > 4L * 1024L * 1024L * 1024L)
            {
                throw new PayloadLimitExceededException(4L * 1024L * 1024L * 1024L);
            }
        }

        private static async Task ValidateSqliteSnapshotAsync(
            string databasePath,
            CancellationToken cancellationToken)
        {
            var builder = new SqliteConnectionStringBuilder(DbHelper.BuildConnectionString(databasePath))
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ValidateOpenSqliteConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        private static async Task ValidateOpenSqliteConnectionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            string result = await SqliteMaintenanceGateway
                .RunQuickCheckAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite 一致性检查失败：{result}" );
            }
        }

        private static async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private IEnumerable<FileInfo> GetCandidateBackupFiles()
        {
            if (!_usesSqlite || string.IsNullOrWhiteSpace(_databaseFileName))
            {
                return Enumerable.Empty<FileInfo>();
            }

            var directoryInfo = new DirectoryInfo(_backupDirectory);
            if (!directoryInfo.Exists)
            {
                return Enumerable.Empty<FileInfo>();
            }

            return directoryInfo
                .EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly)
                .Where(file => _managedBackupNamePattern?.IsMatch(file.Name) == true)
                .ToList();
        }

        private static Regex BuildManagedBackupNamePattern(string databaseFileName) =>
            new(
                $"^\\d{{8}}_\\d{{6}}_\\d{{3}}_(?:(?:pre-restore|imported)_)?{Regex.Escape(BuildBackupNameToken(databaseFileName))}_[0-9a-f]{{32}}\\.zip$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        private string BuildImportedBackupFileName(string preferredFileName)
        {
            string preferred = (preferredFileName ?? string.Empty).Trim();
            if (_managedBackupNamePattern?.IsMatch(preferred) == true)
            {
                string preferredPath = Path.Combine(_backupDirectory, preferred);
                if (!File.Exists(preferredPath))
                {
                    return preferred;
                }
            }

            return $"{DateTimeOffset.Now:yyyyMMdd_HHmmss_fff}_imported_{BuildBackupNameToken(_databaseFileName)}_{Guid.NewGuid():N}.zip";
        }

        private static string BuildBackupNameToken(string databaseFileName)
        {
            string rawName = Path.GetFileNameWithoutExtension(databaseFileName);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "data";
            }

            var buffer = rawName
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
                .ToArray();
            string normalized = new string(buffer).Trim('_');
            return string.IsNullOrWhiteSpace(normalized) ? "data" : normalized;
        }

    }
}
