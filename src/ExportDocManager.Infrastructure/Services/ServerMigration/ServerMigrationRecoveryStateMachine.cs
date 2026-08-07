using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 启动前服务器恢复状态机。它独立编排验证、安全备份、数据库恢复、文件切换、完成与回滚。
/// </summary>
public static class ServerMigrationRecoveryStateMachine
{
    public static async Task ApplyAsync(
        IAppPathProvider pathProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string markerPath = ServerMigrationManager.GetPendingMarkerPath(pathProvider);
        if (!File.Exists(markerPath))
        {
            return;
        }

        PendingServerMigrationRestore marker;
        try
        {
            marker = JsonSerializer.Deserialize<PendingServerMigrationRestore>(
                await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false),
                ServerMigrationService.JsonOptions)
                ?? throw new InvalidDataException("服务器迁移恢复标记为空。");
            ServerMigrationManager.ValidateMarker(marker);
        }
        catch (Exception ex)
        {
            await MarkUnreadableMarkerFailedAsync(pathProvider, markerPath, ex)
                .ConfigureAwait(false);
            return;
        }

        var requestContext = new ServerMigrationRequestContext(
            marker.RequestedBy,
            marker.RemoteAddress);
        string stagingRoot = ServerMigrationPackageValidator.ResolvePath(
            ServerMigrationManager.GetControlRoot(pathProvider),
            marker.StagingDirectoryName);
        if (ServerMigrationRestorePhase.IsTerminal(marker.Phase))
        {
            AtomicFileHelper.TryDeleteDirectory(stagingRoot);
            AtomicFileHelper.TryDeleteFile(markerPath);
            return;
        }
        if (!string.Equals(
            marker.Phase,
            ServerMigrationRestorePhase.Pending,
            StringComparison.Ordinal))
        {
            await RecoverInterruptedRestoreAsync(
                pathProvider,
                markerPath,
                marker,
                requestContext,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string safetyRoot = ServerMigrationManager.GetSafetyBackupRoot(pathProvider, marker.PackageId);
        string databaseDump = ServerMigrationPackageValidator.ResolvePath(
            stagingRoot,
            ServerMigrationLayout.DatabaseEntry);
        DatabaseConnectionSettings settings = null;
        PostgreSqlToolPaths tools = null;
        ServerMigrationFileTransactionState fileState = null;
        marker.Attempt++;
        marker.ValidationDatabaseName =
            $"edm_migration_verify_{marker.PackageId[..16]}";
        try
        {
            UpdatePhase(
                pathProvider,
                markerPath,
                marker,
                ServerMigrationRestorePhase.Validating,
                "正在验证迁移包、产品数据库身份和架构版本。");
            if (!Directory.Exists(stagingRoot))
            {
                throw new InvalidDataException("服务器迁移暂存目录不存在。");
            }
            ValidateStagedManifest(stagingRoot, marker.Manifest);
            ValidateMasterKeyCompatibility(stagingRoot, marker.Manifest);
            settings = DbHelper.LoadDatabaseSettings();
            if (!DatabaseModeHelper.UsesSharedDatabase(settings))
            {
                throw new ServiceValidationException(
                    "服务器迁移恢复要求当前服务器已配置 PostgreSQL 连接。");
            }
            tools = PostgreSqlToolLocator.Resolve(pathProvider);
            if (!tools.ToolsReady)
            {
                throw new InfrastructureServiceException(
                    "服务器迁移恢复缺少兼容的 PostgreSQL 18 客户端工具。");
            }
            await ServerMigrationDatabaseRestorer.ValidateProductDumpAsync(
                tools,
                settings,
                databaseDump,
                marker.ValidationDatabaseName,
                cancellationToken).ConfigureAwait(false);
            marker.ValidationDatabaseName = string.Empty;
            ServerMigrationManager.WriteMarker(markerPath, marker);

            UpdatePhase(
                pathProvider,
                markerPath,
                marker,
                ServerMigrationRestorePhase.SafetyBackup,
                "正在创建数据库与运行文件安全备份。");
            Directory.CreateDirectory(safetyRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(safetyRoot);
            string safetyDump = Path.Combine(safetyRoot, "before-restore.dump");
            await ServerMigrationDatabaseRestorer.CreateSafetyBackupAsync(
                tools,
                settings,
                safetyDump,
                cancellationToken).ConfigureAwait(false);
            fileState = ServerMigrationFileSwitcher.Prepare(
                pathProvider,
                stagingRoot,
                safetyRoot,
                marker,
                settings);

            UpdatePhase(
                pathProvider,
                markerPath,
                marker,
                ServerMigrationRestorePhase.ApplyingDatabase,
                "正在事务性恢复 PostgreSQL 业务库。");
            await ServerMigrationDatabaseRestorer.RestoreAsync(
                tools,
                settings,
                databaseDump,
                cancellationToken).ConfigureAwait(false);
            await ServerMigrationPathRewriter.RewriteDatabasePathsAsync(
                settings,
                marker.Manifest.SourceDataRoot,
                pathProvider.DataRoot,
                cancellationToken).ConfigureAwait(false);

            UpdatePhase(
                pathProvider,
                markerPath,
                marker,
                ServerMigrationRestorePhase.ApplyingFiles,
                "正在原子替换运行配置与业务文件。");
            ServerMigrationFileSwitcher.Apply(fileState);
            ServerMigrationFileSwitcher.CleanupPrepared(fileState);

            marker.Phase = ServerMigrationRestorePhase.Completed;
            marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
            marker.LastError = string.Empty;
            ServerMigrationManager.WriteMarker(markerPath, marker);
            ServerMigrationManager.WriteStatus(
                pathProvider,
                marker,
                "服务器迁移恢复已完成。恢复前安全备份已保留。",
                safetyRoot);
            AtomicFileHelper.TryDeleteFile(markerPath);
            AtomicFileHelper.TryDeleteDirectory(stagingRoot);
            ServerMigrationSecurityAudit.Write(
                pathProvider,
                "apply-restore",
                requestContext,
                marker.PackageId,
                success: true,
                "服务器迁移恢复完成。");
        }
        catch (Exception ex)
        {
            string rollbackMessage = await TryRollbackAsync(
                pathProvider,
                markerPath,
                marker,
                settings,
                tools,
                safetyRoot,
                ex,
                cancellationToken).ConfigureAwait(false);
            AtomicFileHelper.TryDeleteDirectory(stagingRoot);
            ServerMigrationSecurityAudit.Write(
                pathProvider,
                "apply-restore",
                requestContext,
                marker.PackageId,
                success: false,
                rollbackMessage);
            Console.Error.WriteLine($"Server migration restore failed: {rollbackMessage}");
        }
    }

    private static async Task RecoverInterruptedRestoreAsync(
        IAppPathProvider pathProvider,
        string markerPath,
        PendingServerMigrationRestore marker,
        ServerMigrationRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        string safetyRoot = ServerMigrationManager.GetSafetyBackupRoot(pathProvider, marker.PackageId);
        DatabaseConnectionSettings settings = null;
        PostgreSqlToolPaths tools = null;
        try
        {
            settings = DbHelper.LoadDatabaseSettings();
            tools = PostgreSqlToolLocator.Resolve(pathProvider);
            if (!string.IsNullOrWhiteSpace(marker.ValidationDatabaseName) &&
                DatabaseModeHelper.UsesSharedDatabase(settings))
            {
                await ServerMigrationDatabaseRestorer.TryDropValidationDatabaseAsync(
                    settings,
                    marker.ValidationDatabaseName,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // The rollback result below records the actionable aggregate failure.
        }

        var interruption = new InfrastructureServiceException(
            $"检测到上次服务器迁移在阶段 {marker.Phase} 异常中断，已停止自动重试并执行回滚。");
        string message = await TryRollbackAsync(
            pathProvider,
            markerPath,
            marker,
            settings,
            tools,
            safetyRoot,
            interruption,
            cancellationToken).ConfigureAwait(false);
        AtomicFileHelper.TryDeleteDirectory(ServerMigrationPackageValidator.ResolvePath(
            ServerMigrationManager.GetControlRoot(pathProvider),
            marker.StagingDirectoryName));
        ServerMigrationSecurityAudit.Write(
            pathProvider,
            "recover-interrupted-restore",
            requestContext,
            marker.PackageId,
            success: false,
            message);
        Console.Error.WriteLine($"Interrupted server migration recovered: {message}");
    }

    private static async Task<string> TryRollbackAsync(
        IAppPathProvider pathProvider,
        string markerPath,
        PendingServerMigrationRestore marker,
        DatabaseConnectionSettings settings,
        PostgreSqlToolPaths tools,
        string safetyRoot,
        Exception originalError,
        CancellationToken cancellationToken)
    {
        string interruptedPhase = marker.Phase;
        bool databaseMayHaveChanged = interruptedPhase is
            ServerMigrationRestorePhase.ApplyingDatabase or
            ServerMigrationRestorePhase.ApplyingFiles or
            ServerMigrationRestorePhase.RollingBack or
            ServerMigrationRestorePhase.Completed;
        bool filesMayHaveChanged = interruptedPhase is
            ServerMigrationRestorePhase.ApplyingFiles or
            ServerMigrationRestorePhase.RollingBack or
            ServerMigrationRestorePhase.Completed;
        marker.Phase = ServerMigrationRestorePhase.RollingBack;
        marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
        marker.LastError = originalError.Message;
        ServerMigrationManager.TryWriteMarker(markerPath, marker);
        ServerMigrationManager.WriteStatus(
            pathProvider,
            marker,
            $"迁移失败，正在回滚：{originalError.Message}",
            safetyRoot);

        var rollbackErrors = new List<string>();
        try
        {
            if (Directory.Exists(safetyRoot))
            {
                ServerMigrationFileTransactionState state =
                    ServerMigrationFileSwitcher.ReadState(safetyRoot);
                if (state != null)
                {
                    if (filesMayHaveChanged)
                    {
                        ServerMigrationFileSwitcher.Rollback(safetyRoot);
                    }
                    else
                    {
                        ServerMigrationFileSwitcher.CleanupPrepared(state);
                        ServerMigrationFileSwitcher.CleanupSnapshots(state);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            rollbackErrors.Add($"文件回滚失败：{ex.Message}");
        }

        string safetyDump = Path.Combine(safetyRoot, "before-restore.dump");
        if (databaseMayHaveChanged &&
            settings != null &&
            tools?.ToolsReady == true &&
            File.Exists(safetyDump) &&
            new FileInfo(safetyDump).Length > 0)
        {
            try
            {
                await ServerMigrationDatabaseRestorer.RestoreAsync(
                    tools,
                    settings,
                    safetyDump,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                rollbackErrors.Add($"数据库回滚失败：{ex.Message}");
            }
        }

        string result = rollbackErrors.Count == 0
            ? filesMayHaveChanged || databaseMayHaveChanged
                ? $"{originalError.Message} 已自动回滚，服务将继续启动。"
                : $"{originalError.Message} 已清理未应用的迁移准备数据，服务将继续启动。"
            : $"{originalError.Message} 自动回滚未完全成功：{string.Join("；", rollbackErrors)} 请使用安全备份 {safetyRoot} 人工恢复。";
        marker.Phase = ServerMigrationRestorePhase.Failed;
        marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
        marker.LastError = result;
        ServerMigrationManager.TryWriteMarker(markerPath, marker);
        ServerMigrationManager.WriteStatus(pathProvider, marker, result, safetyRoot);
        AtomicFileHelper.TryDeleteFile(markerPath);
        return result;
    }

    private static void UpdatePhase(
        IAppPathProvider pathProvider,
        string markerPath,
        PendingServerMigrationRestore marker,
        string phase,
        string message)
    {
        marker.Phase = phase;
        marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
        marker.LastError = string.Empty;
        ServerMigrationManager.WriteMarker(markerPath, marker);
        ServerMigrationManager.WriteStatus(
            pathProvider,
            marker,
            message,
            ServerMigrationManager.GetSafetyBackupRoot(pathProvider, marker.PackageId));
    }

    private static async Task MarkUnreadableMarkerFailedAsync(
        IAppPathProvider pathProvider,
        string markerPath,
        Exception error)
    {
        var marker = new PendingServerMigrationRestore
        {
            PackageId = Guid.NewGuid().ToString("N"),
            PackageFileName = Path.GetFileName(markerPath),
            Phase = ServerMigrationRestorePhase.Failed,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastError = $"服务器迁移恢复标记无效：{error.Message}"
        };
        ServerMigrationManager.WriteStatus(pathProvider, marker, marker.LastError, string.Empty);
        string failedPath = Path.Combine(
            Path.GetDirectoryName(markerPath)!,
            $"invalid-marker-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        try
        {
            File.Move(markerPath, failedPath, overwrite: false);
            RuntimeFilePermissionHelper.RestrictFile(failedPath);
        }
        catch
        {
            AtomicFileHelper.TryDeleteFile(markerPath);
        }
        ServerMigrationSecurityAudit.Write(
            pathProvider,
            "apply-restore",
            new ServerMigrationRequestContext(string.Empty, string.Empty),
            string.Empty,
            success: false,
            marker.LastError);
        await Task.CompletedTask;
    }

    private static void ValidateStagedManifest(
        string stagingRoot,
        ServerMigrationManifest manifest)
    {
        if (manifest is null ||
            manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
            manifest.Files is null ||
            manifest.Files.Count == 0 ||
            manifest.Files.Any(file => file is null) ||
            !manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("服务器迁移清单缺少数据库备份或版本无效。");
        }
        bool fullMigration = manifest.Files.Any(file =>
            !file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase));
        if (fullMigration &&
            (!manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.ConfigEntry("appsettings.json"),
                StringComparison.OrdinalIgnoreCase)) ||
             !manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName),
                StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("服务器完整迁移清单缺少运行配置或本地主密钥。");
        }
        foreach (ServerMigrationFileManifest file in manifest.Files)
        {
            _ = ServerMigrationPackageValidator.NormalizeRelativePath(file.RelativePath);
            string path = ServerMigrationPackageValidator.ResolvePath(stagingRoot, file.RelativePath);
            if (!File.Exists(path) ||
                new FileInfo(path).Length != file.SizeBytes ||
                !string.Equals(
                    ServerMigrationPackageValidator.ComputeSha256(path),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"服务器迁移暂存文件校验失败：{file.RelativePath}");
            }
        }
    }

    private static void ValidateMasterKeyCompatibility(
        string stagingRoot,
        ServerMigrationManifest manifest)
    {
        if (manifest?.Files is null || manifest.Files.Any(file => file is null))
        {
            throw new InvalidDataException("服务器迁移清单文件列表无效。");
        }
        string masterKeyEntry = ServerMigrationLayout.SecurityEntry(
            LocalSecretProtector.MasterKeyFileName);
        bool fullMigration = manifest.Files.Any(file =>
            !file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase));
        ServerMigrationFileManifest masterKey = manifest.Files.FirstOrDefault(file =>
            file.RelativePath.Equals(masterKeyEntry, StringComparison.OrdinalIgnoreCase));
        if (!fullMigration)
        {
            return;
        }
        if (masterKey == null)
        {
            throw new InvalidDataException("服务器迁移包缺少本地主密钥。");
        }

        string configured = Environment.GetEnvironmentVariable(
            LocalSecretProtector.MasterKeyEnvironmentVariable)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }
        byte[] configuredKey = ServerMigrationService.ParseConfiguredMasterKey(configured);
        byte[] packageKey = File.ReadAllBytes(ServerMigrationPackageValidator.ResolvePath(
            stagingRoot,
            masterKeyEntry));
        try
        {
            if (packageKey.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(configuredKey, packageKey))
            {
                throw new ServiceValidationException(
                    "目标服务器的 EXPORTDOCMANAGER_MASTER_KEY 与迁移包不一致；数据库尚未恢复，请使用源服务器主密钥重新部署目标服务。");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(configuredKey);
            CryptographicOperations.ZeroMemory(packageKey);
        }
    }
}
