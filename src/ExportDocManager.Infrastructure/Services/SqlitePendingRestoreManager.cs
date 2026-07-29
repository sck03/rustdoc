using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Utils;
using Microsoft.Data.Sqlite;
using Serilog;

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
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static void ApplyPendingRestore(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                return;
            }

            string configuredDatabaseFileName = DbHelper.NormalizeSqliteDatabaseFileName(
                databaseSettings.SqliteDatabaseFileName);
            string databasePath;
            if (Path.IsPathRooted(configuredDatabaseFileName))
            {
                databasePath = DbHelper.ResolveRuntimeSqliteDatabasePath(
                    pathProvider,
                    configuredDatabaseFileName);
            }
            else
            {
                string normalizedFileName = DbHelper.NormalizeRuntimeSqliteDatabaseFileName(
                    configuredDatabaseFileName);
                string configuredDatabaseDirectory = Path.Combine(pathProvider.DataRoot, "Database");
                databasePath = Path.GetFullPath(Path.Combine(configuredDatabaseDirectory, normalizedFileName));

                // Merely composing the API service graph must not create the database directory.
                // Resolve against the canonical path provider only when a restore marker exists.
                if (!File.Exists(GetMarkerPath(databasePath)))
                {
                    return;
                }

                databasePath = DbHelper.ResolveRuntimeSqliteDatabasePath(pathProvider, normalizedFileName);
            }
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
                    JsonOptions) ?? throw new InvalidDataException("SQLite 待还原任务内容为空。" );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw new InvalidOperationException(
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
                throw new InvalidOperationException("SQLite 待还原任务的路径格式无效。", ex);
            }

            string databaseDirectory = Path.GetDirectoryName(databasePath) ?? pathProvider.DatabaseRoot;
            if (!string.Equals(markerTargetPath, databasePath, PathComparison) ||
                !PathBoundaryHelper.IsWithinRoot(stagedPath, databaseDirectory) ||
                !string.Equals(stagedPath, GetStagedRestorePath(databasePath), PathComparison))
            {
                throw new InvalidOperationException("SQLite 待还原任务的目标路径或暂存路径无效。" );
            }
            if (!File.Exists(stagedPath))
            {
                CompleteAlreadyAppliedRestore(databasePath, markerPath, marker);
                return;
            }

            string actualHash = ComputeSha256(stagedPath);
            if (!string.Equals(actualHash, marker.StagedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite 待还原数据库摘要不一致，已拒绝覆盖当前数据库。" );
            }

            ValidateSnapshot(stagedPath);
            SqliteConnection.ClearAllPools();
            AtomicFileHelper.ReplaceFile(stagedPath, databasePath);
            AtomicFileHelper.TryDeleteFile(databasePath + "-wal");
            AtomicFileHelper.TryDeleteFile(databasePath + "-shm");
            AtomicFileHelper.TryDeleteFile(markerPath);
            Log.Information(
                "Applied pending SQLite restore from {BackupFileName}; safety backup={SafetyBackupFilePath}.",
                marker.SourceBackupFileName,
                marker.SafetyBackupFilePath);
        }

        private static void CompleteAlreadyAppliedRestore(
            string databasePath,
            string markerPath,
            SqlitePendingRestoreMarker marker)
        {
            if (!File.Exists(databasePath) || string.IsNullOrWhiteSpace(marker.StagedSha256))
            {
                throw new InvalidOperationException("SQLite 待还原任务缺少暂存数据库文件。" );
            }

            string targetHash = ComputeSha256(databasePath);
            if (!string.Equals(targetHash, marker.StagedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite 待还原任务缺少暂存数据库文件，且当前数据库不是已排队的还原版本。" );
            }

            SqliteConnection.ClearAllPools();
            AtomicFileHelper.TryDeleteFile(databasePath + "-wal");
            AtomicFileHelper.TryDeleteFile(databasePath + "-shm");
            ValidateSnapshot(databasePath);
            AtomicFileHelper.TryDeleteFile(markerPath);
            Log.Information(
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
                throw new InvalidOperationException($"SQLite 待还原数据库一致性检查失败：{result}" );
            }
        }
    }
}
