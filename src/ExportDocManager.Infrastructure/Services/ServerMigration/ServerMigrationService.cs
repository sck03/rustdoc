using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 服务器迁移应用门面。包生成、包校验、数据库恢复、文件切换和启动恢复状态机分别位于专用组件。
/// </summary>
public sealed class ServerMigrationService : IServerMigrationService
{
    private static readonly SemaphoreSlim MigrationGate = new(1, 1);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly DatabaseConnectionSettings _databaseSettings;
    private readonly IAppPathProvider _pathProvider;
    private readonly ServerMigrationPackageGenerator _packageGenerator;
    private readonly IBusinessClock _clock;

    public ServerMigrationService(
        DatabaseConnectionSettings databaseSettings,
        IAppPathProvider pathProvider,
        ISharedDatabaseMaintenanceService databaseMaintenance,
        IBusinessClock? clock = null)
    {
        _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _clock = clock ?? BusinessClock.CreateSystem();
        _packageGenerator = new ServerMigrationPackageGenerator(
            pathProvider,
            databaseMaintenance ?? throw new ArgumentNullException(nameof(databaseMaintenance)),
            Path.Combine(pathProvider.BackupRoot, ServerMigrationLayout.PackageDirectoryName));
    }

    private string PackageRoot
    {
        get
        {
            string path = Path.Combine(_pathProvider.BackupRoot, ServerMigrationLayout.PackageDirectoryName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public ServerMigrationStatus GetStatus()
    {
        PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
        bool configured = DatabaseModeHelper.UsesSharedDatabase(_databaseSettings);
        bool pending = ServerMigrationManager.HasPendingRestore(_pathProvider);
        ServerMigrationRestoreStatusSnapshot? lastRestore = ServerMigrationManager.ReadStatus(_pathProvider);
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
            throw new ResourceConflictException("已有服务器迁移任务等待重启执行。请先完成恢复。");
        }

        await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string workingRoot = string.Empty;
        string payloadPath = string.Empty;
        string packageId = Guid.NewGuid().ToString("N");
        string packagePath = string.Empty;
        try
        {
            using FileStream migrationLock = ServerMigrationManager.AcquireExclusiveLock(_pathProvider);
            if (ServerMigrationManager.HasPendingRestore(_pathProvider))
            {
                throw new ResourceConflictException("已有服务器迁移任务等待重启执行。请先完成恢复。");
            }

            workingRoot = Path.Combine(
                _pathProvider.CacheRoot,
                "ServerMigration",
                Guid.NewGuid().ToString("N"));
            payloadPath = Path.Combine(workingRoot, "payload.zip");
            packagePath = Path.Combine(
                PackageRoot,
                $"server-migration-{_clock.Now:yyyyMMdd-HHmmss}-{packageId[..8]}{ServerMigrationLayout.PackageExtension}");
            Directory.CreateDirectory(workingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
            ServerMigrationSecurityAudit.Write(
                _pathProvider,
                "create-package",
                requestContext,
                packageId,
                null,
                "开始创建服务器迁移包。");
            try
            {
                ServerMigrationPackageResult result = await _packageGenerator
                    .CreateAsync(password, packageId, packagePath, workingRoot, payloadPath, cancellationToken)
                    .ConfigureAwait(false);
                ServerMigrationSecurityAudit.Write(
                    _pathProvider,
                    "create-package",
                    requestContext,
                    packageId,
                    true,
                    "服务器迁移包创建成功。");
                return result;
            }
            catch (Exception ex)
            {
                AtomicFileHelper.TryDeleteFile(packagePath);
                TryWriteSecurityAudit(
                    _pathProvider,
                    "create-package",
                    requestContext,
                    packageId,
                    false,
                    ex.Message);
                throw;
            }
        }
        finally
        {
            MigrationGate.Release();
            if (!string.IsNullOrWhiteSpace(workingRoot))
            {
                AtomicFileHelper.TryDeleteDirectory(workingRoot);
            }
        }
    }

    public async Task<ServerMigrationRestoreResult> StageRestoreAsync(
        Stream package,
        string packageFileName,
        string password,
        ServerMigrationRequestContext requestContext,
        CancellationToken cancellationToken = default,
        long? expectedPackageBytes = null)
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
            throw new ResourceConflictException("已有服务器迁移任务等待重启执行。");
        }

        using FileStream migrationLock = ServerMigrationManager.AcquireExclusiveLock(_pathProvider);
        await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string workingRoot = Path.Combine(_pathProvider.CacheRoot, "ServerMigration", Guid.NewGuid().ToString("N"));
        string encryptedPath = Path.Combine(workingRoot, fileName);
        string payloadPath = Path.Combine(workingRoot, "payload.zip");
        string extractedRoot = Path.Combine(workingRoot, "extracted");
        string stagingDirectoryName = string.Empty;
        string packageId = string.Empty;
        try
        {
            Directory.CreateDirectory(workingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
            long? incomingBytes = expectedPackageBytes;
            if (!incomingBytes.HasValue && package.CanSeek)
            {
                incomingBytes = Math.Max(0, package.Length - package.Position);
            }
            if (incomingBytes is > ServerMigrationPackageValidator.MaximumPackageBytes)
            {
                throw new PayloadLimitExceededException(ServerMigrationPackageValidator.MaximumPackageBytes);
            }
            ServerMigrationStorageBudget.EnsureAvailable(
                workingRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(incomingBytes ?? 0),
                "接收服务器迁移包");
            ServerMigrationStorageBudget.IncrementalWriteGuard packageWriteGuard =
                ServerMigrationStorageBudget.CreateIncrementalWriteGuard(workingRoot, "接收服务器迁移包");
            await ServerMigrationPackageValidator.CopyBoundedAsync(
                package,
                encryptedPath,
                ServerMigrationPackageValidator.MaximumPackageBytes,
                cancellationToken,
                packageWriteGuard.EnsureCanWrite).ConfigureAwait(false);
            long declaredPlaintextBytes = await DisasterRecoveryPackageCrypto
                .ReadDeclaredPlaintextLengthAsync(encryptedPath, cancellationToken)
                .ConfigureAwait(false);
            ServerMigrationStorageBudget.EnsureAvailable(
                workingRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(declaredPlaintextBytes),
                "解密服务器迁移包");
            await DisasterRecoveryPackageCrypto.DecryptAsync(encryptedPath, payloadPath, password, cancellationToken).ConfigureAwait(false);
            ServerMigrationPackageValidator.ValidateArchiveEntries(payloadPath);
            long declaredExtractedBytes = ServerMigrationPackageValidator.GetDeclaredUncompressedBytes(payloadPath);
            ServerMigrationStorageBudget.EnsureAvailable(
                extractedRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(declaredExtractedBytes),
                "解压服务器迁移包");
            await ZipArchiveHelper.ExtractToDirectorySafeAsync(payloadPath, extractedRoot, cancellationToken, limits: ServerMigrationPackageValidator.ExtractionLimits).ConfigureAwait(false);
            ServerMigrationManifest manifest = await ServerMigrationPackageValidator.ReadAndValidateManifestAsync(extractedRoot, cancellationToken).ConfigureAwait(false);
            long stagedBytes = ServerMigrationStorageBudget.SumManifestBytes(manifest);
            ServerMigrationStorageBudget.EnsureAvailable(
                _pathProvider.BackupRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(stagedBytes),
                "准备服务器迁移暂存数据");
            packageId = manifest.PackageId;
            string sourceDump = ResolvePath(extractedRoot, ServerMigrationLayout.DatabaseEntry);
            await ServerMigrationDatabaseRestorer.ValidateDumpContainerAsync(PostgreSqlToolLocator.Resolve(_pathProvider), sourceDump, cancellationToken).ConfigureAwait(false);

            stagingDirectoryName = $"pending-{manifest.PackageId}";
            string controlRoot = ServerMigrationManager.GetControlRoot(_pathProvider);
            string stagingRoot = Path.Combine(controlRoot, stagingDirectoryName);
            if (Directory.Exists(stagingRoot)) throw new ResourceConflictException("同一迁移包已存在暂存数据，请先处理上一次恢复任务。");
            Directory.CreateDirectory(stagingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(stagingRoot);
            await CopyManifestFilesAsync(manifest, extractedRoot, stagingRoot, cancellationToken).ConfigureAwait(false);
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
            TryWriteSecurityAudit(_pathProvider, "stage-full-restore", requestContext, manifest.PackageId, true, "服务器迁移恢复已排队。");
            return new ServerMigrationRestoreResult(true, true, "服务器迁移已安全排队。请重启 API 服务；恢复会在建立数据库连接前执行。", fileName, ServerMigrationManager.GetSafetyBackupRoot(_pathProvider, manifest.PackageId), ServerMigrationLayout.StoragePolicy);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(stagingDirectoryName))
            {
                AtomicFileHelper.TryDeleteDirectory(Path.Combine(ServerMigrationManager.GetControlRoot(_pathProvider), stagingDirectoryName));
            }
            TryWriteSecurityAudit(_pathProvider, "stage-full-restore", requestContext, packageId, false, ex.Message);
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
        CancellationToken cancellationToken = default,
        long? expectedBackupBytes = null)
    {
        EnsurePostgreSqlSupported();
        ArgumentNullException.ThrowIfNull(databaseBackup);
        requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
        string fileName = Path.GetFileName(backupFileName ?? string.Empty);
        if (!fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PostgreSQL 恢复文件必须是 .dump custom-format 备份。");
        if (ServerMigrationManager.HasPendingRestore(_pathProvider)) throw new ResourceConflictException("已有服务器迁移或数据库恢复任务等待重启执行。");

        using FileStream migrationLock = ServerMigrationManager.AcquireExclusiveLock(_pathProvider);
        await MigrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string workingRoot = Path.Combine(_pathProvider.CacheRoot, "ServerMigration", Guid.NewGuid().ToString("N"));
        string dumpPath = Path.Combine(workingRoot, fileName);
        string stagingDirectoryName = string.Empty;
        string packageId = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(workingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(workingRoot);
            long? incomingBytes = expectedBackupBytes;
            if (!incomingBytes.HasValue && databaseBackup.CanSeek)
            {
                incomingBytes = Math.Max(0, databaseBackup.Length - databaseBackup.Position);
            }
            if (incomingBytes is > DisasterRecoveryPackageCrypto.MaximumPlaintextBytes)
            {
                throw new PayloadLimitExceededException(DisasterRecoveryPackageCrypto.MaximumPlaintextBytes);
            }
            ServerMigrationStorageBudget.EnsureAvailable(
                workingRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(incomingBytes ?? 0),
                "接收 PostgreSQL 数据库备份");
            ServerMigrationStorageBudget.IncrementalWriteGuard backupWriteGuard =
                ServerMigrationStorageBudget.CreateIncrementalWriteGuard(workingRoot, "接收 PostgreSQL 数据库备份");
            long receivedBackupBytes = await ServerMigrationPackageValidator.CopyBoundedAsync(
                databaseBackup,
                dumpPath,
                DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
                cancellationToken,
                backupWriteGuard.EnsureCanWrite).ConfigureAwait(false);
            if (receivedBackupBytes == 0) throw new InvalidDataException("PostgreSQL 备份文件不能为空。");
            await ServerMigrationDatabaseRestorer.ValidateDumpContainerAsync(PostgreSqlToolLocator.Resolve(_pathProvider), dumpPath, cancellationToken).ConfigureAwait(false);
            stagingDirectoryName = $"pending-{packageId}";
            string stagingRoot = Path.Combine(ServerMigrationManager.GetControlRoot(_pathProvider), stagingDirectoryName);
            Directory.CreateDirectory(stagingRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(stagingRoot);
            ServerMigrationStorageBudget.EnsureAvailable(
                stagingRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(new FileInfo(dumpPath).Length),
                "准备 PostgreSQL 数据库恢复暂存数据");
            string target = ResolvePath(stagingRoot, ServerMigrationLayout.DatabaseEntry);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await FileCopyHelper.CopyAsync(dumpPath, target, overwrite: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            RuntimeFilePermissionHelper.RestrictFile(target);
            var manifest = new ServerMigrationManifest
            {
                SchemaVersion = ServerMigrationLayout.SchemaVersion,
                PackageId = packageId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SourceDataRoot = _pathProvider.DataRoot,
                SourcePlatform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
                SourcePathCaseSensitive = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS(),
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
            TryWriteSecurityAudit(_pathProvider, "stage-database-restore", requestContext, packageId, true, "PostgreSQL 数据库恢复已排队。");
            return new ServerMigrationRestoreResult(true, true, "PostgreSQL 数据库恢复已排队。请重启 API 服务；恢复会在建立数据库连接前执行。", fileName, ServerMigrationManager.GetSafetyBackupRoot(_pathProvider, packageId), ServerMigrationLayout.StoragePolicy);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(stagingDirectoryName)) AtomicFileHelper.TryDeleteDirectory(Path.Combine(ServerMigrationManager.GetControlRoot(_pathProvider), stagingDirectoryName));
            TryWriteSecurityAudit(_pathProvider, "stage-database-restore", requestContext, packageId, false, ex.Message);
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
        if (!DatabaseModeHelper.UsesSharedDatabase(_databaseSettings)) throw new NotSupportedException("服务器迁移功能只支持已配置的 PostgreSQL 团队库。");
        PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
        if (!tools.ToolsReady)
        {
            throw new InfrastructureServiceException("未找到兼容的完整 PostgreSQL 客户端工具。请使用程序 Tools/PostgreSQL/bin 中随包提供的 PostgreSQL 18 工具，或设置 EXPORTDOCMANAGER_POSTGRES_BIN。");
        }
    }

    private static async Task CopyManifestFilesAsync(ServerMigrationManifest manifest, string sourceRoot, string targetRoot, CancellationToken cancellationToken)
    {
        foreach (ServerMigrationFileManifest file in manifest.Files)
        {
            string source = ResolvePath(sourceRoot, file.RelativePath);
            string target = ResolvePath(targetRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await FileCopyHelper.CopyAsync(source, target, overwrite: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            RuntimeFilePermissionHelper.RestrictFile(target);
        }
    }

    private static async Task WriteStagedManifestAsync(string stagingRoot, ServerMigrationManifest manifest, CancellationToken cancellationToken)
    {
        string path = Path.Combine(stagingRoot, ServerMigrationLayout.ManifestEntry);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
        RuntimeFilePermissionHelper.RestrictFile(path);
    }

    // Stable internal seams used by security tests and by the recovery state machine.
    internal static Task<ServerMigrationManifest> ReadAndValidateManifestAsync(string root, CancellationToken cancellationToken) =>
        ServerMigrationPackageValidator.ReadAndValidateManifestAsync(root, cancellationToken);

    internal static void ValidateArchiveEntries(string path) => ServerMigrationPackageValidator.ValidateArchiveEntries(path);

    internal static string ResolvePath(string root, string relativePath) => ServerMigrationPackageValidator.ResolvePath(root, relativePath);

    internal static string NormalizeRelativePath(string relativePath) => ServerMigrationPackageValidator.NormalizeRelativePath(relativePath);

    internal static string ComputeSha256(string path) => ServerMigrationPackageValidator.ComputeSha256(path);

    internal static Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken) =>
        ServerMigrationPackageValidator.ComputeSha256Async(path, cancellationToken);

    internal static async Task CopyBoundedAsync(
        Stream source,
        string destination,
        long maximumBytes,
        CancellationToken cancellationToken) =>
        await ServerMigrationPackageValidator.CopyBoundedAsync(
            source,
            destination,
            maximumBytes,
            cancellationToken).ConfigureAwait(false);

    private static void TryWriteSecurityAudit(
        IAppPathProvider pathProvider,
        string action,
        ServerMigrationRequestContext requestContext,
        string packageId,
        bool? success,
        string message)
    {
        try
        {
            ServerMigrationSecurityAudit.Write(
                pathProvider,
                action,
                requestContext,
                packageId,
                success,
                message);
        }
        catch
        {
            // Preserve the original migration failure. Audit storage errors are
            // diagnosed separately and must not replace the actionable cause.
        }
    }

    internal static byte[] ParseConfiguredMasterKey(string configured)
    {
        try
        {
            byte[] key = configured.Length == 64 && configured.All(Uri.IsHexDigit)
                ? Convert.FromHexString(configured)
                : Convert.FromBase64String(configured);
            if (key.Length == 32) return key;
            CryptographicOperations.ZeroMemory(key);
            throw new ServiceValidationException("EXPORTDOCMANAGER_MASTER_KEY 必须解码为 32 字节。");
        }
        catch (FormatException ex)
        {
            throw new ServiceValidationException("EXPORTDOCMANAGER_MASTER_KEY 格式无效。", ex);
        }
    }
}
