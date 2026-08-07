using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowDisasterRecoveryManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public static bool HasPendingRestore(IAppPathProvider pathProvider) =>
            File.Exists(GetPendingMarkerPath(pathProvider));

        public static void ApplyPendingRestore(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string markerPath = GetPendingMarkerPath(pathProvider);
            if (!File.Exists(markerPath))
            {
                return;
            }

            PendingDisasterRecoveryRestore marker = ReadPendingMarker(markerPath);
            string controlRoot = GetControlRoot(pathProvider);
            string stagingRoot = ResolveStagingRoot(controlRoot, marker.StagingDirectoryName);
            ValidatePendingRestore(pathProvider, marker, stagingRoot);

            string safetyRoot = GetSafetyBackupRoot(pathProvider, marker.PackageId);
            EnsureSafetyBackup(pathProvider, marker, safetyRoot);

            foreach (var file in marker.Files)
            {
                string sourcePath = ResolveStagedPath(stagingRoot, file.RelativePath);
                string targetPath = ResolveTargetPath(pathProvider, marker.DatabaseFileName, file.RelativePath);
                if (File.Exists(targetPath) &&
                    new FileInfo(targetPath).Length == file.SizeBytes &&
                    string.Equals(ComputeSha256(targetPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    RestrictRecoveredFilePermissions(targetPath);
                    continue;
                }

                AtomicFileHelper.WriteFileAtomic(
                    targetPath,
                    tempPath => File.Copy(sourcePath, tempPath, overwrite: false));
                if (new FileInfo(targetPath).Length != file.SizeBytes ||
                    !string.Equals(ComputeSha256(targetPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"灾难恢复文件写入后校验失败：{file.RelativePath}");
                }
                RestrictRecoveredFilePermissions(targetPath);
            }

            string databasePath = Path.Combine(pathProvider.DatabaseRoot, marker.DatabaseFileName);
            AtomicFileHelper.TryDeleteFile(databasePath + "-wal");
            AtomicFileHelper.TryDeleteFile(databasePath + "-shm");
            RecoveryLicenseReactivationMarker.Require(pathProvider, marker.PackageId);

            File.Delete(markerPath);
            AtomicFileHelper.TryDeleteDirectory(stagingRoot);
        }

        internal static string GetRecoveryRoot(IAppPathProvider pathProvider) =>
            Path.Combine(pathProvider.BackupRoot, SingleWindowDisasterRecoveryLayout.RecoveryDirectoryName);

        internal static string GetControlRoot(IAppPathProvider pathProvider) =>
            Path.Combine(
                pathProvider.SecurityRoot,
                "SingleWindow",
                SingleWindowDisasterRecoveryLayout.ControlDirectoryName);

        internal static string GetPendingMarkerPath(IAppPathProvider pathProvider) =>
            Path.Combine(GetControlRoot(pathProvider), SingleWindowDisasterRecoveryLayout.PendingMarkerFileName);

        internal static string GetSafetyBackupRoot(IAppPathProvider pathProvider, string packageId) =>
            Path.Combine(GetRecoveryRoot(pathProvider), "Safety", packageId);

        internal static void WritePendingMarker(
            IAppPathProvider pathProvider,
            PendingDisasterRecoveryRestore marker)
        {
            string markerPath = GetPendingMarkerPath(pathProvider);
            if (File.Exists(markerPath))
            {
                throw new ResourceConflictException("已有持卡机灾难恢复任务等待重启执行。");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            RestrictDirectoryPermissions(Path.GetDirectoryName(markerPath)!);
            AtomicFileHelper.WriteAllTextAtomic(
                markerPath,
                JsonSerializer.Serialize(marker, JsonOptions));
            RestrictRecoveredFilePermissions(markerPath);
        }

        internal static void ValidatePendingRestore(
            IAppPathProvider pathProvider,
            PendingDisasterRecoveryRestore marker,
            string stagingRoot)
        {
            if (marker.SchemaVersion != SingleWindowDisasterRecoveryLayout.SchemaVersion ||
                !Guid.TryParseExact(marker.PackageId, "N", out _) ||
                !IsSafeStagingDirectoryName(marker.StagingDirectoryName))
            {
                throw new InvalidDataException("待执行的持卡机灾难恢复标记无效。");
            }

            string databaseFileName = DbHelper.NormalizeRuntimeSqliteDatabaseFileName(marker.DatabaseFileName);
            if (!string.Equals(databaseFileName, marker.DatabaseFileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("灾难恢复数据库文件名无效。");
            }
            ValidateFileManifest(marker.Files, databaseFileName);
            foreach (var file in marker.Files)
            {
                string stagedPath = ResolveStagedPath(stagingRoot, file.RelativePath);
                if (!File.Exists(stagedPath) ||
                    new FileInfo(stagedPath).Length != file.SizeBytes ||
                    !string.Equals(ComputeSha256(stagedPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"灾难恢复暂存文件缺失或校验失败：{file.RelativePath}");
                }
            }

            string settingsPath = ResolveStagedPath(stagingRoot, SingleWindowDisasterRecoveryLayout.AppSettingsEntry);
            DatabaseConnectionSettings settings = DbHelper.LoadDatabaseSettings(settingsPath);
            if (DatabaseModeHelper.UsesPostgreSql(settings))
            {
                throw new InvalidDataException("持卡机灾难恢复只支持 SQLite 单机版，恢复包不能配置 PostgreSQL。");
            }
            string configuredDatabase = DbHelper.NormalizeRuntimeSqliteDatabaseFileName(settings.SqliteDatabaseFileName);
            if (!string.Equals(configuredDatabase, databaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("恢复包 appsettings.json 与数据库快照文件名不一致。");
            }

            string masterKeyPath = ResolveStagedPath(stagingRoot, SingleWindowDisasterRecoveryLayout.MasterKeyEntry);
            if (new FileInfo(masterKeyPath).Length != 32)
            {
                throw new InvalidDataException("恢复包本地主密钥长度无效。");
            }
            string stationPath = ResolveStagedPath(stagingRoot, SingleWindowDisasterRecoveryLayout.StationIdentityEntry);
            string stationKey = File.ReadAllText(stationPath).Trim();
            if (!IsValidStationKey(stationKey))
            {
                throw new InvalidDataException("恢复包 station.id 格式无效。");
            }

            string databasePath = ResolveStagedPath(
                stagingRoot,
                SingleWindowDisasterRecoveryLayout.DatabaseEntry(databaseFileName));
            ValidateSqliteSnapshot(databasePath);
        }

        internal static void ValidateFileManifest(
            IReadOnlyCollection<DisasterRecoveryFileManifest> files,
            string databaseFileName)
        {
            string[] expected =
            [
                SingleWindowDisasterRecoveryLayout.DatabaseEntry(databaseFileName),
                SingleWindowDisasterRecoveryLayout.AppSettingsEntry,
                SingleWindowDisasterRecoveryLayout.MasterKeyEntry,
                SingleWindowDisasterRecoveryLayout.StationIdentityEntry
            ];
            if (files == null || files.Count != expected.Length)
            {
                throw new InvalidDataException("灾难恢复包文件清单必须且只能包含四个运行文件。");
            }
            var paths = files.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (expected.Any(path => !paths.Contains(path)) || paths.Count != expected.Length)
            {
                throw new InvalidDataException("灾难恢复包文件清单含有缺失、重复或未授权条目。");
            }
            foreach (var file in files)
            {
                if (file.SizeBytes <= 0 ||
                    file.SizeBytes > DisasterRecoveryPackageCrypto.MaximumPlaintextBytes ||
                    file.Sha256?.Length != 64 ||
                    !file.Sha256.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException($"灾难恢复包文件清单无效：{file.RelativePath}");
                }
            }
        }

        internal static bool IsValidStationKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWS-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static PendingDisasterRecoveryRestore ReadPendingMarker(string markerPath)
        {
            try
            {
                return JsonSerializer.Deserialize<PendingDisasterRecoveryRestore>(
                    File.ReadAllText(markerPath),
                    JsonOptions) ?? throw new InvalidDataException("灾难恢复标记内容为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("灾难恢复标记 JSON 已损坏。", ex);
            }
        }

        private static void EnsureSafetyBackup(
            IAppPathProvider pathProvider,
            PendingDisasterRecoveryRestore marker,
            string safetyRoot)
        {
            string completedPath = Path.Combine(safetyRoot, SingleWindowDisasterRecoveryLayout.SafetyCompleteFileName);
            if (File.Exists(completedPath))
            {
                return;
            }
            if (Directory.Exists(safetyRoot))
            {
                Directory.Delete(safetyRoot, recursive: true);
            }
            Directory.CreateDirectory(safetyRoot);
            RestrictDirectoryPermissions(safetyRoot);
            foreach (var file in marker.Files)
            {
                string currentPath = ResolveTargetPath(pathProvider, marker.DatabaseFileName, file.RelativePath);
                if (!File.Exists(currentPath))
                {
                    continue;
                }
                string safetyPath = ResolveStagedPath(safetyRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(safetyPath)!);
                RestrictDirectoryPermissions(Path.GetDirectoryName(safetyPath)!);
                File.Copy(currentPath, safetyPath, overwrite: false);
                RestrictRecoveredFilePermissions(safetyPath);
            }
            AtomicFileHelper.WriteAllTextAtomic(
                completedPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    marker.PackageId,
                    createdAtUtc = DateTimeOffset.UtcNow
                }, JsonOptions));
            RestrictRecoveredFilePermissions(completedPath);
        }

        private static string ResolveStagingRoot(string controlRoot, string directoryName)
        {
            if (!IsSafeStagingDirectoryName(directoryName))
            {
                throw new InvalidDataException("灾难恢复暂存目录名无效。");
            }
            string fullControlRoot = Path.GetFullPath(controlRoot);
            string path = Path.GetFullPath(Path.Combine(fullControlRoot, directoryName));
            if (!PathBoundaryHelper.IsWithinRoot(path, fullControlRoot))
            {
                throw new InvalidDataException("灾难恢复暂存目录越界。");
            }
            return path;
        }

        private static bool IsSafeStagingDirectoryName(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith("pending-", StringComparison.Ordinal) &&
            Guid.TryParseExact(value["pending-".Length..], "N", out _);

        private static string ResolveStagedPath(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root);
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathBoundaryHelper.IsWithinRoot(path, fullRoot))
            {
                throw new InvalidDataException("灾难恢复文件路径越界。");
            }
            return path;
        }

        private static string ResolveTargetPath(
            IAppPathProvider pathProvider,
            string databaseFileName,
            string relativePath) => relativePath switch
        {
            var path when path.Equals(
                SingleWindowDisasterRecoveryLayout.DatabaseEntry(databaseFileName),
                StringComparison.OrdinalIgnoreCase) => Path.Combine(pathProvider.DatabaseRoot, databaseFileName),
            var path when path.Equals(
                SingleWindowDisasterRecoveryLayout.AppSettingsEntry,
                StringComparison.OrdinalIgnoreCase) => Path.Combine(pathProvider.ConfigRoot, "appsettings.json"),
            var path when path.Equals(
                SingleWindowDisasterRecoveryLayout.MasterKeyEntry,
                StringComparison.OrdinalIgnoreCase) => Path.Combine(pathProvider.SecurityRoot, "local-master-key.bin"),
            var path when path.Equals(
                SingleWindowDisasterRecoveryLayout.StationIdentityEntry,
                StringComparison.OrdinalIgnoreCase) => Path.Combine(pathProvider.SecurityRoot, "SingleWindow", "station.id"),
            _ => throw new InvalidDataException($"灾难恢复文件不在允许清单内：{relativePath}")
        };

        private static void ValidateSqliteSnapshot(string databasePath)
        {
            var builder = new SqliteConnectionStringBuilder(DbHelper.BuildConnectionString(databasePath))
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            string result = SqliteMaintenanceGateway.RunQuickCheck(connection);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"灾难恢复 SQLite 一致性检查失败：{result}");
            }
        }

        internal static void RestrictRecoveredFilePermissions(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        internal static void RestrictDirectoryPermissions(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
