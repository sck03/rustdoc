using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class ServerMigrationService : IServerMigrationService
    {
        private static readonly SemaphoreSlim MigrationGate = new(1, 1);
        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        private static readonly ZipExtractionLimits ExtractionLimits = new(
            MaximumEntries: 100_000,
            MaximumEntryBytes: DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
            MaximumTotalBytes: DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
            MaximumCompressionRatio: 2_000,
            MaximumPathDepth: 12);
        private const long MaximumPackageBytes =
            DisasterRecoveryPackageCrypto.MaximumPlaintextBytes + 32L * 1024L * 1024L;
        private const long MaximumManifestBytes = 32L * 1024L * 1024L;

        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly IAppPathProvider _pathProvider;
        private readonly ISharedDatabaseMaintenanceService _databaseMaintenance;

        public ServerMigrationService(
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            ISharedDatabaseMaintenanceService databaseMaintenance)
        {
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _databaseMaintenance = databaseMaintenance ?? throw new ArgumentNullException(nameof(databaseMaintenance));
        }

        private string PackageRoot =>
            EnsureDirectory(Path.Combine(_pathProvider.BackupRoot, ServerMigrationLayout.PackageDirectoryName));

        public ServerMigrationStatus GetStatus()
        {
            PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            bool configured = DatabaseModeHelper.UsesSharedDatabase(_databaseSettings);
            bool pending = ServerMigrationManager.HasPendingRestore(_pathProvider);
            ServerMigrationRestoreStatusSnapshot lastRestore = ServerMigrationManager.ReadStatus(_pathProvider);
            string message = pending
                ? "服务器迁移已排队，将在服务下次启动、建立数据库连接前执行。"
                : configured && tools.ToolsReady
                    ? "可创建加密迁移包，或上传迁移包并安排服务重启恢复。"
                    : !configured
                        ? "当前不是已配置的 PostgreSQL 团队库。"
                        : "PostgreSQL 客户端工具缺失、不完整或版本不兼容。";
            return new ServerMigrationStatus(
                Supported: configured,
                PostgreSqlConfigured: configured,
                ToolsReady: tools.ToolsReady,
                PendingRestore: pending,
                PackageRoot,
                message,
                ServerMigrationLayout.StoragePolicy,
                lastRestore?.Phase ?? string.Empty,
                lastRestore?.Message ?? string.Empty,
                lastRestore?.UpdatedAtUtc);
        }

        public async Task<ServerMigrationPackageResult> CreatePackageAsync(
            string password,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default)
        {
            requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
            EnsurePostgreSqlSupported();
            DisasterRecoveryPackageCrypto.ValidatePassword(password);
            if (ServerMigrationManager.HasPendingRestore(_pathProvider))
            {
                throw new InvalidOperationException("已有服务器迁移任务等待重启执行。请先完成恢复。");
            }

            await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "ServerMigration",
                Guid.NewGuid().ToString("N"));
            string payloadPath = Path.Combine(workingRoot, "payload.zip");
            string packageId = Guid.NewGuid().ToString("N");
            string packagePath = Path.Combine(
                PackageRoot,
                $"server-migration-{DateTime.Now:yyyyMMdd-HHmmss}-{packageId[..8]}{ServerMigrationLayout.PackageExtension}");
            Directory.CreateDirectory(workingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
            ServerMigrationSecurityAudit.Write(
                _pathProvider,
                "create-package",
                requestContext,
                packageId,
                success: null,
                "开始创建服务器迁移包。");
            try
            {
                ServerMigrationPackageResult result = await CreatePackageCoreAsync(
                    password,
                    packageId,
                    packagePath,
                    workingRoot,
                    payloadPath,
                    cancellationToken).ConfigureAwait(false);
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "create-package",
                    requestContext,
                    packageId,
                    success: true,
                    "服务器迁移包创建成功。");
                return result;
            }
            catch (Exception ex)
            {
                AtomicFileHelper.TryDeleteFile(packagePath);
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "create-package",
                    requestContext,
                    packageId,
                    success: false,
                    ex.Message);
                throw;
            }
            finally
            {
                MigrationGate.Release();
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }

        private async Task<ServerMigrationPackageResult> CreatePackageCoreAsync(
            string password,
            string packageId,
            string packagePath,
            string workingRoot,
            string payloadPath,
            CancellationToken cancellationToken)
        {
            var databaseBackup = await _databaseMaintenance
                .CreatePostgreSqlPhysicalBackupAsync(cancellationToken)
                .ConfigureAwait(false);
            var sources = new List<(string SourcePath, string EntryName)>
            {
                (databaseBackup.FullPath, ServerMigrationLayout.DatabaseEntry)
            };
            AddDirectoryFiles(sources, _pathProvider.ConfigRoot, ServerMigrationLayout.ConfigEntry);
            AddDirectoryFiles(
                sources,
                _pathProvider.FileRoot,
                relative => ServerMigrationLayout.DataEntry("Files", relative));
            AddDirectoryFiles(
                sources,
                _pathProvider.UserTemplateRoot,
                relative => ServerMigrationLayout.DataEntry("Templates", relative));
            AddDirectoryFiles(
                sources,
                _pathProvider.SingleWindowRoot,
                relative => ServerMigrationLayout.DataEntry("SingleWindow", relative));
            AddDirectoryFiles(
                sources,
                Path.Combine(_pathProvider.DataRoot, "Marks"),
                relative => ServerMigrationLayout.DataEntry("Marks", relative));

            string masterKeyPath = Path.Combine(
                _pathProvider.SecurityRoot,
                LocalSecretProtector.MasterKeyFileName);
            EnsureDirectoryRootIsNotLink(_pathProvider.SecurityRoot);
            EnsureMasterKeyFile(masterKeyPath);
            sources.Add((
                masterKeyPath,
                ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName)));
            string stationPath = Path.Combine(_pathProvider.SecurityRoot, "SingleWindow", "station.id");
            if (File.Exists(stationPath))
            {
                EnsureFileIsNotLink(stationPath);
                sources.Add((
                    stationPath,
                    ServerMigrationLayout.SecurityEntry("SingleWindow/station.id")));
            }

            var manifest = new ServerMigrationManifest
            {
                SchemaVersion = ServerMigrationLayout.SchemaVersion,
                PackageId = packageId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SourceDataRoot = _pathProvider.DataRoot
            };
            foreach ((string sourcePath, string entryName) in sources)
            {
                var info = new FileInfo(sourcePath);
                bool allowsEmptyContent = entryName.StartsWith(
                    "Data/",
                    StringComparison.OrdinalIgnoreCase);
                if (!info.Exists || !allowsEmptyContent && info.Length <= 0)
                {
                    throw new InvalidDataException($"迁移源文件不存在或为空：{sourcePath}");
                }
                manifest.Files.Add(new ServerMigrationFileManifest
                {
                    RelativePath = entryName,
                    SizeBytes = info.Length,
                    Sha256 = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false)
                });
            }

            string manifestPath = Path.Combine(workingRoot, ServerMigrationLayout.ManifestEntry);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            sources.Add((manifestPath, ServerMigrationLayout.ManifestEntry));
            await ZipArchiveHelper.CreateFromFilesAsync(sources, payloadPath, cancellationToken)
                .ConfigureAwait(false);
            await AtomicFileHelper.WriteFileAtomicAsync(
                packagePath,
                (tempPath, ct) => DisasterRecoveryPackageCrypto.EncryptAsync(payloadPath, tempPath, password, ct),
                cancellationToken).ConfigureAwait(false);
            RuntimeFilePermissionHelper.RestrictFile(packagePath);
            var package = new FileInfo(packagePath);
            return new ServerMigrationPackageResult(
                true,
                "服务器加密迁移包已创建。请将迁移包和加密密码分开保管。",
                package.Name,
                package.FullName,
                package.Length,
                PackageRoot,
                ServerMigrationLayout.StoragePolicy);
        }

        public async Task<ServerMigrationRestoreResult> StageRestoreAsync(
            Stream package,
            string packageFileName,
            string password,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default)
        {
            EnsurePostgreSqlSupported();
            ArgumentNullException.ThrowIfNull(package);
            requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
            DisasterRecoveryPackageCrypto.ValidatePassword(password);
            string fileName = Path.GetFileName(packageFileName ?? string.Empty);
            if (!fileName.EndsWith(ServerMigrationLayout.PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("请选择 .edmmigration 服务器迁移包。");
            }
            if (ServerMigrationManager.HasPendingRestore(_pathProvider))
            {
                throw new InvalidOperationException("已有服务器迁移任务等待重启执行。");
            }

            await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "ServerMigration",
                Guid.NewGuid().ToString("N"));
            string encryptedPath = Path.Combine(workingRoot, fileName);
            string payloadPath = Path.Combine(workingRoot, "payload.zip");
            string extractedRoot = Path.Combine(workingRoot, "extracted");
            string stagingDirectoryName = string.Empty;
            string packageId = string.Empty;
            try
            {
                Directory.CreateDirectory(workingRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
                await CopyBoundedAsync(package, encryptedPath, MaximumPackageBytes, cancellationToken)
                    .ConfigureAwait(false);
                await DisasterRecoveryPackageCrypto.DecryptAsync(
                    encryptedPath,
                    payloadPath,
                    password,
                    cancellationToken).ConfigureAwait(false);
                ValidateArchiveEntries(payloadPath);
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(
                    payloadPath,
                    extractedRoot,
                    cancellationToken,
                    limits: ExtractionLimits).ConfigureAwait(false);
                ServerMigrationManifest manifest = await ReadAndValidateManifestAsync(
                    extractedRoot,
                    cancellationToken).ConfigureAwait(false);
                packageId = manifest.PackageId;
                string sourceDump = ResolvePath(extractedRoot, ServerMigrationLayout.DatabaseEntry);
                await ServerMigrationPostgreSql.ValidateDumpContainerAsync(
                    PostgreSqlToolLocator.Resolve(_pathProvider),
                    sourceDump,
                    cancellationToken).ConfigureAwait(false);

                stagingDirectoryName = $"pending-{manifest.PackageId}";
                string controlRoot = ServerMigrationManager.GetControlRoot(_pathProvider);
                string stagingRoot = Path.Combine(controlRoot, stagingDirectoryName);
                if (Directory.Exists(stagingRoot))
                {
                    throw new InvalidOperationException("同一迁移包已存在暂存数据，请先处理上一次恢复任务。");
                }
                Directory.CreateDirectory(stagingRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(stagingRoot);
                await CopyManifestFilesAsync(manifest, extractedRoot, stagingRoot, cancellationToken)
                    .ConfigureAwait(false);
                await WriteStagedManifestAsync(stagingRoot, manifest, cancellationToken).ConfigureAwait(false);
                ServerMigrationManager.WritePendingMarker(_pathProvider, new PendingServerMigrationRestore
                {
                    SchemaVersion = ServerMigrationLayout.SchemaVersion,
                    PackageId = manifest.PackageId,
                    PackageFileName = fileName,
                    ScheduledAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    StagingDirectoryName = stagingDirectoryName,
                    Phase = ServerMigrationRestorePhase.Pending,
                    RequestedBy = requestContext.RequestedBy?.Trim() ?? string.Empty,
                    RemoteAddress = requestContext.RemoteAddress?.Trim() ?? string.Empty,
                    Manifest = manifest
                });
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "stage-full-restore",
                    requestContext,
                    manifest.PackageId,
                    success: true,
                    "服务器迁移恢复已排队。");
                return new ServerMigrationRestoreResult(
                    true,
                    true,
                    "服务器迁移已安全排队。请重启 API 服务；恢复会在建立数据库连接前执行。",
                    fileName,
                    ServerMigrationManager.GetSafetyBackupRoot(_pathProvider, manifest.PackageId),
                    ServerMigrationLayout.StoragePolicy);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(stagingDirectoryName))
                {
                    AtomicFileHelper.TryDeleteDirectory(Path.Combine(
                        ServerMigrationManager.GetControlRoot(_pathProvider),
                        stagingDirectoryName));
                }
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "stage-full-restore",
                    requestContext,
                    packageId,
                    success: false,
                    ex.Message);
                throw;
            }
            finally
            {
                MigrationGate.Release();
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }

        public async Task<ServerMigrationRestoreResult> StageDatabaseRestoreAsync(
            Stream databaseBackup,
            string backupFileName,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default)
        {
            EnsurePostgreSqlSupported();
            ArgumentNullException.ThrowIfNull(databaseBackup);
            requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
            string fileName = Path.GetFileName(backupFileName ?? string.Empty);
            if (!fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("PostgreSQL 恢复文件必须是 .dump custom-format 备份。");
            }
            if (ServerMigrationManager.HasPendingRestore(_pathProvider))
            {
                throw new InvalidOperationException("已有服务器迁移或数据库恢复任务等待重启执行。");
            }

            await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "ServerMigration",
                Guid.NewGuid().ToString("N"));
            string dumpPath = Path.Combine(workingRoot, fileName);
            string stagingDirectoryName = string.Empty;
            string packageId = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(workingRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
                await CopyBoundedAsync(
                    databaseBackup,
                    dumpPath,
                    DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
                    cancellationToken).ConfigureAwait(false);
                if (new FileInfo(dumpPath).Length == 0)
                {
                    throw new InvalidDataException("PostgreSQL 备份文件不能为空。");
                }
                await ServerMigrationPostgreSql.ValidateDumpContainerAsync(
                    PostgreSqlToolLocator.Resolve(_pathProvider),
                    dumpPath,
                    cancellationToken).ConfigureAwait(false);

                stagingDirectoryName = $"pending-{packageId}";
                string stagingRoot = Path.Combine(
                    ServerMigrationManager.GetControlRoot(_pathProvider),
                    stagingDirectoryName);
                Directory.CreateDirectory(stagingRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(stagingRoot);
                string target = ResolvePath(stagingRoot, ServerMigrationLayout.DatabaseEntry);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await FileCopyHelper.CopyAsync(
                    dumpPath,
                    target,
                    overwrite: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                RuntimeFilePermissionHelper.RestrictFile(target);
                var manifest = new ServerMigrationManifest
                {
                    SchemaVersion = ServerMigrationLayout.SchemaVersion,
                    PackageId = packageId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    SourceDataRoot = _pathProvider.DataRoot,
                    Files =
                    [
                        new ServerMigrationFileManifest
                        {
                            RelativePath = ServerMigrationLayout.DatabaseEntry,
                            SizeBytes = new FileInfo(target).Length,
                            Sha256 = ComputeSha256(target)
                        }
                    ]
                };
                await WriteStagedManifestAsync(stagingRoot, manifest, cancellationToken).ConfigureAwait(false);
                ServerMigrationManager.WritePendingMarker(_pathProvider, new PendingServerMigrationRestore
                {
                    SchemaVersion = ServerMigrationLayout.SchemaVersion,
                    PackageId = packageId,
                    PackageFileName = fileName,
                    ScheduledAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    StagingDirectoryName = stagingDirectoryName,
                    Phase = ServerMigrationRestorePhase.Pending,
                    RequestedBy = requestContext.RequestedBy?.Trim() ?? string.Empty,
                    RemoteAddress = requestContext.RemoteAddress?.Trim() ?? string.Empty,
                    Manifest = manifest
                });
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "stage-database-restore",
                    requestContext,
                    packageId,
                    success: true,
                    "PostgreSQL 数据库恢复已排队。");
                return new ServerMigrationRestoreResult(
                    true,
                    true,
                    "PostgreSQL 数据库恢复已排队。请重启 API 服务；恢复会在建立数据库连接前执行。",
                    fileName,
                    ServerMigrationManager.GetSafetyBackupRoot(_pathProvider, packageId),
                    ServerMigrationLayout.StoragePolicy);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(stagingDirectoryName))
                {
                    AtomicFileHelper.TryDeleteDirectory(Path.Combine(
                        ServerMigrationManager.GetControlRoot(_pathProvider),
                        stagingDirectoryName));
                }
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "stage-database-restore",
                    requestContext,
                    packageId,
                    success: false,
                    ex.Message);
                throw;
            }
            finally
            {
                MigrationGate.Release();
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }

        private void EnsurePostgreSqlSupported()
        {
            if (!DatabaseModeHelper.UsesSharedDatabase(_databaseSettings))
            {
                throw new NotSupportedException("服务器迁移功能只支持已配置的 PostgreSQL 团队库。");
            }
            PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            if (!tools.ToolsReady)
            {
                throw new InvalidOperationException(
                    "未找到兼容的完整 PostgreSQL 客户端工具。请使用程序 Tools/PostgreSQL/bin 中随包提供的 PostgreSQL 18 工具，或设置 EXPORTDOCMANAGER_POSTGRES_BIN。");
            }
        }

        private static async Task CopyManifestFilesAsync(
            ServerMigrationManifest manifest,
            string sourceRoot,
            string targetRoot,
            CancellationToken cancellationToken)
        {
            foreach (ServerMigrationFileManifest file in manifest.Files)
            {
                string source = ResolvePath(sourceRoot, file.RelativePath);
                string target = ResolvePath(targetRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await FileCopyHelper.CopyAsync(
                    source,
                    target,
                    overwrite: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                RuntimeFilePermissionHelper.RestrictFile(target);
            }
        }

        private static async Task WriteStagedManifestAsync(
            string stagingRoot,
            ServerMigrationManifest manifest,
            CancellationToken cancellationToken)
        {
            string path = Path.Combine(stagingRoot, ServerMigrationLayout.ManifestEntry);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            RuntimeFilePermissionHelper.RestrictFile(path);
        }

        private static void AddDirectoryFiles(
            ICollection<(string SourcePath, string EntryName)> sources,
            string root,
            Func<string, string> entryFactory)
        {
            if (!Directory.Exists(root))
            {
                return;
            }
            string fullRoot = Path.GetFullPath(root);
            EnsureDirectoryRootIsNotLink(fullRoot);
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(fullRoot);
            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    bool isSymbolic = (attributes & FileAttributes.ReparsePoint) != 0;
                    if (isSymbolic)
                    {
                        throw new InvalidOperationException(
                            $"服务器迁移源目录不能包含符号链接或重解析点：{entry}");
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(entry);
                        continue;
                    }
                    string relative = Path.GetRelativePath(fullRoot, entry);
                    sources.Add((entry, entryFactory(relative)));
                }
            }
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static void EnsureMasterKeyFile(string path)
        {
            string configured = Environment.GetEnvironmentVariable(
                LocalSecretProtector.MasterKeyEnvironmentVariable)?.Trim() ?? string.Empty;
            byte[] configuredKey = string.IsNullOrWhiteSpace(configured)
                ? null
                : ParseConfiguredMasterKey(configured);
            try
            {
                if (File.Exists(path))
                {
                    EnsureFileIsNotLink(path);
                    byte[] fileKey = File.ReadAllBytes(path);
                    try
                    {
                        if (fileKey.Length != 32)
                        {
                            throw new InvalidDataException("本地主密钥文件长度无效。");
                        }
                        if (configuredKey != null &&
                            !CryptographicOperations.FixedTimeEquals(configuredKey, fileKey))
                        {
                            throw new InvalidOperationException(
                                "EXPORTDOCMANAGER_MASTER_KEY 与本地主密钥文件不一致，不能创建可恢复的服务器迁移包。");
                        }
                        return;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(fileKey);
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                byte[] key = configuredKey ?? RandomNumberGenerator.GetBytes(32);
                try
                {
                    AtomicFileHelper.WriteFileAtomic(path, temp => File.WriteAllBytes(temp, key));
                    RuntimeFilePermissionHelper.RestrictFile(path);
                }
                finally
                {
                    if (!ReferenceEquals(key, configuredKey))
                    {
                        CryptographicOperations.ZeroMemory(key);
                    }
                }
            }
            finally
            {
                if (configuredKey != null)
                {
                    CryptographicOperations.ZeroMemory(configuredKey);
                }
            }
        }

        private static void EnsureDirectoryRootIsNotLink(string path)
        {
            if (Directory.Exists(path) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"服务器迁移源目录不能是符号链接或重解析点：{path}");
            }
        }

        private static void EnsureFileIsNotLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"服务器迁移源文件不能是符号链接或重解析点：{path}");
            }
        }

        internal static byte[] ParseConfiguredMasterKey(string configured)
        {
            try
            {
                byte[] key = configured.Length == 64 && configured.All(Uri.IsHexDigit)
                    ? Convert.FromHexString(configured)
                    : Convert.FromBase64String(configured);
                if (key.Length == 32)
                {
                    return key;
                }
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidOperationException("EXPORTDOCMANAGER_MASTER_KEY 必须解码为 32 字节。");
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("EXPORTDOCMANAGER_MASTER_KEY 格式无效。", ex);
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
            return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                .ToLowerInvariant();
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static async Task CopyBoundedAsync(
            Stream source,
            string destination,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new PayloadLimitExceededException(maximumBytes);
                }
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<ServerMigrationManifest> ReadAndValidateManifestAsync(
            string extractedRoot,
            CancellationToken cancellationToken)
        {
            string path = ResolvePath(extractedRoot, ServerMigrationLayout.ManifestEntry);
            var manifest = JsonSerializer.Deserialize<ServerMigrationManifest>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions) ?? throw new InvalidDataException("服务器迁移包清单为空。");
            if (manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
                !Guid.TryParseExact(manifest.PackageId, "N", out _))
            {
                throw new InvalidDataException("服务器迁移包清单版本或包 ID 无效。");
            }
            if (manifest.Files is null ||
                manifest.Files.Count == 0 ||
                manifest.Files.Count > ExtractionLimits.MaximumEntries ||
                manifest.Files.Any(file => file is null))
            {
                throw new InvalidDataException("服务器迁移包文件清单为空或过大。");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerMigrationFileManifest file in manifest.Files)
            {
                string normalized = NormalizeRelativePath(file.RelativePath);
                bool allowed = normalized.Equals(
                        ServerMigrationLayout.DatabaseEntry,
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("Config/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName),
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(
                        ServerMigrationLayout.SecurityEntry("SingleWindow/station.id"),
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("Data/Files/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("Data/Templates/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("Data/SingleWindow/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("Data/Marks/", StringComparison.OrdinalIgnoreCase);
                bool allowsEmptyContent = normalized.StartsWith(
                    "Data/",
                    StringComparison.OrdinalIgnoreCase);
                if (!names.Add(normalized) ||
                    (allowsEmptyContent ? file.SizeBytes < 0 : file.SizeBytes <= 0) ||
                    string.IsNullOrWhiteSpace(file.Sha256) ||
                    file.Sha256.Length != 64 ||
                    !file.Sha256.All(Uri.IsHexDigit) ||
                    !allowed)
                {
                    throw new InvalidDataException($"服务器迁移包文件清单无效：{file.RelativePath}");
                }
                string source = ResolvePath(extractedRoot, normalized);
                if (!File.Exists(source) ||
                    new FileInfo(source).Length != file.SizeBytes ||
                    !string.Equals(
                        await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false),
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"服务器迁移包文件校验失败：{file.RelativePath}");
                }
            }
            if (!names.Contains(ServerMigrationLayout.DatabaseEntry))
            {
                throw new InvalidDataException("服务器迁移包缺少 PostgreSQL 数据库备份。");
            }
            if (!names.Contains(ServerMigrationLayout.ConfigEntry("appsettings.json")))
            {
                throw new InvalidDataException("服务器迁移包缺少运行配置 appsettings.json。");
            }
            if (!names.Contains(ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName)))
            {
                throw new InvalidDataException("服务器迁移包缺少本地主密钥。");
            }
            return manifest;
        }

        private static void ValidateArchiveEntries(string zipPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count < 2 || archive.Entries.Count > ExtractionLimits.MaximumEntries)
            {
                throw new InvalidDataException("服务器迁移包内部条目数量无效。");
            }
            if (archive.Entries.Any(item => string.IsNullOrEmpty(item.Name)))
            {
                throw new InvalidDataException("服务器迁移包不能包含目录条目。");
            }
            var fileEntries = archive.Entries
                .Select(item => new
                {
                    Entry = item,
                    Name = item.FullName.Replace('\\', '/').Trim('/')
                })
                .ToList();
            var names = fileEntries
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var manifestEntries = fileEntries.Where(item =>
                item.Name.Equals(
                    ServerMigrationLayout.ManifestEntry,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (manifestEntries.Count != 1 ||
                manifestEntries[0].Entry.Length <= 0 ||
                manifestEntries[0].Entry.Length > MaximumManifestBytes ||
                !names.Contains(ServerMigrationLayout.DatabaseEntry))
            {
                throw new InvalidDataException("服务器迁移包缺少唯一清单或 PostgreSQL 数据库备份。");
            }
            var manifestEntry = manifestEntries[0];
            using Stream manifestStream = manifestEntry.Entry.Open();
            ServerMigrationManifest manifest = JsonSerializer.Deserialize<ServerMigrationManifest>(
                manifestStream,
                JsonOptions) ?? throw new InvalidDataException("服务器迁移包清单为空。");
            if (manifest.Files is null || manifest.Files.Any(file => file is null))
            {
                throw new InvalidDataException("服务器迁移包清单文件列表无效。");
            }
            var expectedNames = manifest.Files
                .Select(file => NormalizeRelativePath(file.RelativePath))
                .Append(ServerMigrationLayout.ManifestEntry)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (expectedNames.Count != fileEntries.Count ||
                fileEntries.Any(item => !expectedNames.Contains(item.Name)))
            {
                throw new InvalidDataException("服务器迁移包包含清单之外的文件。");
            }
            if (names.Any(name =>
                name.Equals("Deployment/Certificates", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Deployment/Certificates/", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Deployment/Certbot", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Deployment/Certbot/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("服务器迁移包不能包含 TLS/Certbot 证书；请在部署层重新签发证书。");
            }
        }

        internal static string ResolvePath(string root, string relativePath)
        {
            string normalizedRelativePath = NormalizeRelativePath(relativePath);
            string fullRoot = Path.GetFullPath(root);
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathBoundaryHelper.IsWithinRoot(path, fullRoot))
            {
                throw new InvalidDataException("服务器迁移包路径越界。");
            }
            return path;
        }

        internal static string NormalizeRelativePath(string relativePath)
        {
            string normalized = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized) ||
                normalized.IndexOf('\0') >= 0)
            {
                throw new InvalidDataException("服务器迁移包相对路径无效。");
            }

            string[] segments = normalized.Split('/', StringSplitOptions.None);
            if (segments.Length == 0 ||
                segments.Length > ExtractionLimits.MaximumPathDepth ||
                segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment is "." or ".." ||
                    segment.IndexOf(':') >= 0 ||
                    segment.Any(char.IsControl)))
            {
                throw new InvalidDataException("服务器迁移包相对路径无效。");
            }

            return string.Join('/', segments);
        }
    }
}
