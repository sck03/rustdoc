using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Infrastructure
{
    internal sealed class SqlitePendingRestoreMarker
    {
        public string TargetDatabasePath { get; set; } = string.Empty;

        public string StagedDatabasePath { get; set; } = string.Empty;

        public string SourceBackupFileName { get; set; } = string.Empty;

        public string SafetyBackupFilePath { get; set; } = string.Empty;

        public string StagedSha256 { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
    }

    public static class SqlitePendingRestoreManager
    {
        private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static void ApplyPendingRestore(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                return;
            }

            // SQLite database names are deliberately constrained to a simple file name under
            // the runtime Database directory.  Keep restore discovery on the same canonical
            // resolver used by the rest of the data-access stack; it does not create the
            // directory, so composing the service graph remains side-effect free.
            string databasePath = DbHelper.ResolveRuntimeSqliteDatabasePath(
                pathProvider,
                databaseSettings.SqliteDatabaseFileName);
            string markerPath = GetMarkerPath(databasePath);
            if (!File.Exists(markerPath))
            {
                return;
            }

            SqlitePendingRestoreMarker marker;
            try
            {
                marker = JsonSerializer.Deserialize<SqlitePendingRestoreMarker>(
                    File.ReadAllText(markerPath),
                    JsonOptions) ?? throw new InvalidDataException("SQLite 待还原任务内容为空。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw new InfrastructureServiceException(
                    $"无法读取 SQLite 待还原任务，请检查运行数据根 Database 目录：{ex.Message}",
                    ex);
            }

            string stagedPath;
            string markerTargetPath;
            try
            {
                stagedPath = Path.GetFullPath(marker.StagedDatabasePath ?? string.Empty);
                markerTargetPath = Path.GetFullPath(marker.TargetDatabasePath ?? string.Empty);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InfrastructureServiceException("SQLite 待还原任务的路径格式无效。", ex);
            }

            string databaseDirectory = Path.GetDirectoryName(databasePath) ?? pathProvider.DatabaseRoot;
            if (!string.Equals(markerTargetPath, databasePath, PathComparison) ||
                !PathBoundaryHelper.IsWithinRoot(stagedPath, databaseDirectory) ||
                !string.Equals(stagedPath, GetStagedRestorePath(databasePath), PathComparison))
            {
                throw new InfrastructureServiceException("SQLite 待还原任务的目标路径或暂存路径无效。");
            }
            if (!File.Exists(stagedPath))
            {
                CompleteAlreadyAppliedRestore(databasePath, markerPath, marker, logger);
                return;
            }

            string actualHash = ComputeSha256(stagedPath);
            if (!string.Equals(actualHash, marker.StagedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InfrastructureServiceException("SQLite 待还原数据库摘要不一致，已拒绝覆盖当前数据库。");
            }

            ValidateSnapshot(stagedPath);
            SqliteConnection.ClearAllPools();
            AtomicFileHelper.ReplaceFile(stagedPath, databasePath);
            AtomicFileHelper.TryDeleteFile(databasePath + "-wal");
            AtomicFileHelper.TryDeleteFile(databasePath + "-shm");
            AtomicFileHelper.TryDeleteFile(markerPath);
            logger?.LogInformation(
                "Applied pending SQLite restore from {BackupFileName}; safety backup={SafetyBackupFilePath}.",
                marker.SourceBackupFileName,
                marker.SafetyBackupFilePath);
        }

        private static void CompleteAlreadyAppliedRestore(
            string databasePath,
            string markerPath,
            SqlitePendingRestoreMarker marker,
            ILogger? logger)
        {
            if (!File.Exists(databasePath) || string.IsNullOrWhiteSpace(marker.StagedSha256))
            {
                throw new InfrastructureServiceException("SQLite 待还原任务缺少暂存数据库文件。");
            }

            string targetHash = ComputeSha256(databasePath);
            if (!string.Equals(targetHash, marker.StagedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InfrastructureServiceException("SQLite 待还原任务缺少暂存数据库文件，且当前数据库不是已排队的还原版本。");
            }

            SqliteConnection.ClearAllPools();
            AtomicFileHelper.TryDeleteFile(databasePath + "-wal");
            AtomicFileHelper.TryDeleteFile(databasePath + "-shm");
            ValidateSnapshot(databasePath);
            AtomicFileHelper.TryDeleteFile(markerPath);
            logger?.LogInformation(
                "Completed pending SQLite restore recovery after the staged database had already replaced the target; source={BackupFileName}; safety backup={SafetyBackupFilePath}.",
                marker.SourceBackupFileName,
                marker.SafetyBackupFilePath);
        }

        internal static string GetMarkerPath(string databasePath) =>
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                $".{Path.GetFileName(databasePath)}.restore-pending.json");

        internal static string GetStagedRestorePath(string databasePath) =>
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                $".{Path.GetFileName(databasePath)}.restore-pending.db");

        private static string ComputeSha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static void ValidateSnapshot(string path)
        {
            var builder = new SqliteConnectionStringBuilder(DbHelper.BuildConnectionString(path))
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            string result = SqliteMaintenanceGateway.RunQuickCheck(connection);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InfrastructureServiceException($"SQLite 待还原数据库一致性检查失败：{result}");
            }
        }
    }
}
