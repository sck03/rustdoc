using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowDisasterRecoveryService : ISingleWindowDisasterRecoveryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        private static readonly ZipExtractionLimits RecoveryExtractionLimits = new(
            MaximumEntries: 5,
            MaximumEntryBytes: 4L * 1024L * 1024L * 1024L,
            MaximumTotalBytes: DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
            MaximumCompressionRatio: 2_000d,
            MaximumPathDepth: 4);

        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly IAppPathProvider _pathProvider;
        private readonly ISingleWindowStationIdentityService _stationIdentityService;
        private readonly bool _usesSqlite;
        private readonly string _databaseFileName;
        private readonly string _databasePath;
        private readonly IBusinessClock _clock;

        public SingleWindowDisasterRecoveryService(
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            ISingleWindowStationIdentityService stationIdentityService,
            IBusinessClock? clock = null)
        {
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _stationIdentityService = stationIdentityService ?? throw new ArgumentNullException(nameof(stationIdentityService));
            _clock = clock ?? BusinessClock.CreateSystem();
            _usesSqlite = !DatabaseModeHelper.UsesPostgreSql(databaseSettings);
            _databasePath = _usesSqlite
                ? DbHelper.ResolveRuntimeSqliteDatabasePath(pathProvider, databaseSettings.SqliteDatabaseFileName)
                : string.Empty;
            _databaseFileName = _usesSqlite
                ? DbHelper.NormalizeRuntimeSqliteDatabaseFileName(Path.GetFileName(_databasePath))
                : string.Empty;
        }

        public SingleWindowDisasterRecoveryStatus GetStatus()
        {
            string recoveryRoot = SingleWindowDisasterRecoveryManager.GetRecoveryRoot(_pathProvider);
            bool pending = SingleWindowDisasterRecoveryManager.HasPendingRestore(_pathProvider);
            return new SingleWindowDisasterRecoveryStatus(
                Supported: _usesSqlite,
                UsesSqlite: _usesSqlite,
                PendingRestore: pending,
                RecoveryRoot: recoveryRoot,
                Message: !_usesSqlite
                    ? "持卡机灾难恢复只适用于 SQLite 单机版；PostgreSQL 团队库请使用数据库服务器灾备。"
                    : pending
                        ? "灾难恢复已排队，请立即重启桌面程序。"
                        : "可创建独立加密恢复包，供本机损坏或更换持卡机时恢复。",
                StoragePolicy: SingleWindowDisasterRecoveryLayout.StoragePolicy);
        }

        public async Task<SingleWindowDisasterRecoveryPackageResult> CreatePackageAsync(
            string password,
            CancellationToken cancellationToken = default)
        {
            EnsureSupported();
            DisasterRecoveryPackageCrypto.ValidatePassword(password);
            if (SingleWindowDisasterRecoveryManager.HasPendingRestore(_pathProvider))
            {
                throw new ResourceConflictException("灾难恢复任务已排队，完成重启恢复前不能创建新的恢复包。");
            }
            if (!File.Exists(_databasePath))
            {
                throw new FileNotFoundException("当前 SQLite 数据库不存在，无法创建持卡机灾难恢复包。", _databasePath);
            }

            string appSettingsPath = Path.Combine(_pathProvider.ConfigRoot, "appsettings.json");
            if (!File.Exists(appSettingsPath))
            {
                throw new ServiceValidationException("Config/appsettings.json 尚未保存，请先在系统设置中保存配置。 ");
            }
            EnsureLocalMasterKeyFile();
            string masterKeyPath = Path.Combine(_pathProvider.SecurityRoot, LocalSecretProtector.MasterKeyFileName);
            string stationKey = await _stationIdentityService
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);
            string stationPath = Path.Combine(_pathProvider.SecurityRoot, "SingleWindow", "station.id");

            await BackupService.SqliteMaintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "DisasterRecovery",
                Guid.NewGuid().ToString("N"));
            string recoveryRoot = SingleWindowDisasterRecoveryManager.GetRecoveryRoot(_pathProvider);
            Directory.CreateDirectory(workingRoot);
            Directory.CreateDirectory(recoveryRoot);
            SingleWindowDisasterRecoveryManager.RestrictDirectoryPermissions(workingRoot);
            SingleWindowDisasterRecoveryManager.RestrictDirectoryPermissions(recoveryRoot);
            string packageId = Guid.NewGuid().ToString("N");
            string snapshotPath = Path.Combine(workingRoot, _databaseFileName);
            string manifestPath = Path.Combine(workingRoot, "manifest.json");
            string innerZipPath = Path.Combine(workingRoot, "payload.zip");
            string fileName = $"holding-station-recovery-{_clock.Now:yyyyMMdd-HHmmss}-{packageId[..8]}{SingleWindowDisasterRecoveryLayout.PackageExtension}";
            string packagePath = Path.Combine(recoveryRoot, fileName);
            try
            {
                await CreateSqliteSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                var sourceFiles = new[]
                {
                    (Path: snapshotPath, Entry: SingleWindowDisasterRecoveryLayout.DatabaseEntry(_databaseFileName)),
                    (Path: appSettingsPath, Entry: SingleWindowDisasterRecoveryLayout.AppSettingsEntry),
                    (Path: masterKeyPath, Entry: SingleWindowDisasterRecoveryLayout.MasterKeyEntry),
                    (Path: stationPath, Entry: SingleWindowDisasterRecoveryLayout.StationIdentityEntry)
                };
                var manifest = new DisasterRecoveryPackageManifest
                {
                    SchemaVersion = SingleWindowDisasterRecoveryLayout.SchemaVersion,
                    PackageId = packageId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DatabaseFileName = _databaseFileName,
                    StationKey = stationKey,
                    LicensePolicy = "许可证、试用锚点和机器绑定信息不进入恢复包；恢复完成后必须按当前机器码重新激活。",
                    Files = []
                };
                foreach (var source in sourceFiles)
                {
                    manifest.Files.Add(new DisasterRecoveryFileManifest
                    {
                        RelativePath = source.Entry,
                        SizeBytes = new FileInfo(source.Path).Length,
                        Sha256 = await ComputeSha256Async(source.Path, cancellationToken).ConfigureAwait(false)
                    });
                }
                await File.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);

                await ZipArchiveHelper.CreateFromFilesAsync(
                    sourceFiles
                        .Select(source => (source.Path, source.Entry))
                        .Append((manifestPath, SingleWindowDisasterRecoveryLayout.ManifestEntry)),
                    innerZipPath,
                    cancellationToken).ConfigureAwait(false);
                await AtomicFileHelper.WriteFileAtomicAsync(
                    packagePath,
                    (tempPath, ct) => DisasterRecoveryPackageCrypto.EncryptAsync(
                        innerZipPath,
                        tempPath,
                        password,
                        ct),
                    cancellationToken).ConfigureAwait(false);
                SingleWindowDisasterRecoveryManager.RestrictRecoveredFilePermissions(packagePath);

                var packageInfo = new FileInfo(packagePath);
                return new SingleWindowDisasterRecoveryPackageResult(
                    Success: true,
                    Message: "持卡机加密灾难恢复包已创建。请将包和密码分开保管，并至少复制一份到脱机介质。",
                    FileName: packageInfo.Name,
                    FilePath: packageInfo.FullName,
                    SizeBytes: packageInfo.Length,
                    StoragePolicy: SingleWindowDisasterRecoveryLayout.StoragePolicy);
            }
            finally
            {
                BackupService.SqliteMaintenanceGate.Release();
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }

        public async Task<SingleWindowDisasterRecoveryRestoreResult> ScheduleRestoreAsync(
            string packagePath,
            string password,
            CancellationToken cancellationToken = default)
        {
            EnsureSupported();
            DisasterRecoveryPackageCrypto.ValidatePassword(password);
            string fullPackagePath = Path.GetFullPath(packagePath ?? string.Empty);
            if (!File.Exists(fullPackagePath) ||
                !Path.GetExtension(fullPackagePath).Equals(
                    SingleWindowDisasterRecoveryLayout.PackageExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("请选择有效的 .edmrecovery 持卡机灾难恢复包。");
            }
            if (SingleWindowDisasterRecoveryManager.HasPendingRestore(_pathProvider))
            {
                throw new ResourceConflictException("已有持卡机灾难恢复任务等待重启执行。");
            }
            string normalRestoreMarker = SqlitePendingRestoreManager.GetMarkerPath(_databasePath);
            if (File.Exists(normalRestoreMarker))
            {
                throw new ResourceConflictException("已有普通 SQLite 数据库还原任务等待重启，不能同时安排灾难恢复。");
            }

            await BackupService.SqliteMaintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "DisasterRecovery",
                Guid.NewGuid().ToString("N"));
            string innerZipPath = Path.Combine(workingRoot, "payload.zip");
            string extractedRoot = Path.Combine(workingRoot, "extracted");
            Directory.CreateDirectory(workingRoot);
            SingleWindowDisasterRecoveryManager.RestrictDirectoryPermissions(workingRoot);
            string stagingRoot = string.Empty;
            try
            {
                await DisasterRecoveryPackageCrypto.DecryptAsync(
                    fullPackagePath,
                    innerZipPath,
                    password,
                    cancellationToken).ConfigureAwait(false);
                ValidateArchiveEntries(innerZipPath);
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(
                    innerZipPath,
                    extractedRoot,
                    cancellationToken,
                    limits: RecoveryExtractionLimits).ConfigureAwait(false);

                DisasterRecoveryPackageManifest manifest = await ReadAndValidateManifestAsync(
                    extractedRoot,
                    cancellationToken).ConfigureAwait(false);
                string stagingDirectoryName = $"pending-{manifest.PackageId}";
                stagingRoot = Path.Combine(
                    SingleWindowDisasterRecoveryManager.GetControlRoot(_pathProvider),
                    stagingDirectoryName);
                if (Directory.Exists(stagingRoot))
                {
                    throw new ResourceConflictException("同一恢复包已存在暂存数据，请先处理上一次恢复任务。");
                }
                Directory.CreateDirectory(stagingRoot);
                SingleWindowDisasterRecoveryManager.RestrictDirectoryPermissions(stagingRoot);
                foreach (var file in manifest.Files)
                {
                    string source = ResolveExtractedPath(extractedRoot, file.RelativePath);
                    string target = ResolveExtractedPath(stagingRoot, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    SingleWindowDisasterRecoveryManager.RestrictDirectoryPermissions(Path.GetDirectoryName(target)!);
                    await FileCopyHelper.CopyAsync(
                        source,
                        target,
                        overwrite: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    SingleWindowDisasterRecoveryManager.RestrictRecoveredFilePermissions(target);
                }

                var marker = new PendingDisasterRecoveryRestore
                {
                    SchemaVersion = SingleWindowDisasterRecoveryLayout.SchemaVersion,
                    PackageId = manifest.PackageId,
                    PackageFileName = Path.GetFileName(fullPackagePath),
                    ScheduledAtUtc = DateTimeOffset.UtcNow,
                    StagingDirectoryName = stagingDirectoryName,
                    DatabaseFileName = manifest.DatabaseFileName,
                    Files = manifest.Files
                };
                SingleWindowDisasterRecoveryManager.ValidatePendingRestore(_pathProvider, marker, stagingRoot);
                SingleWindowDisasterRecoveryManager.WritePendingMarker(_pathProvider, marker);

                return new SingleWindowDisasterRecoveryRestoreResult(
                    Success: true,
                    RestartRequired: true,
                    Message: "灾难恢复已安全排队。请立即退出并重新打开桌面程序；恢复会在读取数据库配置和建立任何数据库连接之前执行，完成后必须重新激活授权。",
                    PackageFileName: Path.GetFileName(fullPackagePath),
                    SafetyBackupRoot: SingleWindowDisasterRecoveryManager.GetSafetyBackupRoot(
                        _pathProvider,
                        manifest.PackageId),
                    StoragePolicy: SingleWindowDisasterRecoveryLayout.StoragePolicy);
            }
            catch
            {
                if (!SingleWindowDisasterRecoveryManager.HasPendingRestore(_pathProvider) &&
                    !string.IsNullOrWhiteSpace(stagingRoot))
                {
                    AtomicFileHelper.TryDeleteDirectory(stagingRoot);
                }
                throw;
            }
            finally
            {
                BackupService.SqliteMaintenanceGate.Release();
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }

        private void EnsureSupported()
        {
            if (!_usesSqlite)
            {
                throw new NotSupportedException("持卡机灾难恢复只支持 SQLite 单机版。");
            }
        }

        private void EnsureLocalMasterKeyFile()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                    LocalSecretProtector.MasterKeyEnvironmentVariable)))
            {
                throw new ServiceValidationException(
                    "当前通过环境变量提供本地主密钥，无法写入独立恢复包。请由部署管理员单独备份该环境密钥。");
            }
            string keyPath = Path.Combine(_pathProvider.SecurityRoot, LocalSecretProtector.MasterKeyFileName);
            if (File.Exists(keyPath))
            {
                if (new FileInfo(keyPath).Length != 32)
                {
                    throw new InvalidDataException("本地主密钥文件长度无效，无法创建恢复包。");
                }
                return;
            }
            byte[] key = RandomNumberGenerator.GetBytes(32);
            try
            {
                AtomicFileHelper.WriteFileAtomic(
                    keyPath,
                    tempPath =>
                    {
                        using var stream = new FileStream(
                            tempPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            4096,
                            FileOptions.WriteThrough);
                        stream.Write(key);
                        stream.Flush(flushToDisk: true);
                    });
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private async Task CreateSqliteSnapshotAsync(
            string snapshotPath,
            CancellationToken cancellationToken)
        {
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
            source.BackupDatabase(destination);
            string quickCheck = await SqliteMaintenanceGateway
                .RunQuickCheckAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite 一致性检查失败：{quickCheck}");
            }
        }

        private static void ValidateArchiveEntries(string zipPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count != 5 || archive.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
            {
                throw new InvalidDataException("灾难恢复包内部文件数量无效。");
            }
            var names = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/').Trim('/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(SingleWindowDisasterRecoveryLayout.ManifestEntry) ||
                !names.Contains(SingleWindowDisasterRecoveryLayout.AppSettingsEntry) ||
                !names.Contains(SingleWindowDisasterRecoveryLayout.MasterKeyEntry) ||
                !names.Contains(SingleWindowDisasterRecoveryLayout.StationIdentityEntry) ||
                names.Count(name => name.StartsWith("Database/", StringComparison.OrdinalIgnoreCase)) != 1)
            {
                throw new InvalidDataException("灾难恢复包包含缺失、重复或未授权的内部文件。");
            }
        }

        private static async Task<DisasterRecoveryPackageManifest> ReadAndValidateManifestAsync(
            string extractedRoot,
            CancellationToken cancellationToken)
        {
            string manifestPath = ResolveExtractedPath(
                extractedRoot,
                SingleWindowDisasterRecoveryLayout.ManifestEntry);
            DisasterRecoveryPackageManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<DisasterRecoveryPackageManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false),
                    JsonOptions) ?? throw new InvalidDataException("灾难恢复包清单为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("灾难恢复包清单 JSON 无效。", ex);
            }
            if (manifest.SchemaVersion != SingleWindowDisasterRecoveryLayout.SchemaVersion ||
                !Guid.TryParseExact(manifest.PackageId, "N", out _) ||
                !SingleWindowDisasterRecoveryManager.IsValidStationKey(manifest.StationKey))
            {
                throw new InvalidDataException("灾难恢复包清单版本或持卡机身份无效。");
            }
            string databaseFileName = DbHelper.NormalizeRuntimeSqliteDatabaseFileName(manifest.DatabaseFileName);
            if (!string.Equals(databaseFileName, manifest.DatabaseFileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("灾难恢复包数据库文件名无效。");
            }
            SingleWindowDisasterRecoveryManager.ValidateFileManifest(manifest.Files, databaseFileName);
            foreach (var file in manifest.Files)
            {
                string path = ResolveExtractedPath(extractedRoot, file.RelativePath);
                if (!File.Exists(path) ||
                    new FileInfo(path).Length != file.SizeBytes ||
                    !string.Equals(
                        await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false),
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"灾难恢复包文件校验失败：{file.RelativePath}");
                }
            }
            string stationKey = (await File.ReadAllTextAsync(
                    ResolveExtractedPath(extractedRoot, SingleWindowDisasterRecoveryLayout.StationIdentityEntry),
                    cancellationToken)
                .ConfigureAwait(false)).Trim();
            if (!string.Equals(stationKey, manifest.StationKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException("灾难恢复包清单与 station.id 不一致。");
            }
            return manifest;
        }

        private static string ResolveExtractedPath(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root);
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathBoundaryHelper.IsWithinRoot(path, fullRoot))
            {
                throw new InvalidDataException("灾难恢复包路径越界。");
            }
            return path;
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
            return Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                .ToLowerInvariant();
        }
    }
}
