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
public static partial class ServerMigrationRecoveryStateMachine
{
    public static async Task ApplyAsync(
        IAppPathProvider pathProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        using FileStream migrationLock = ServerMigrationManager.AcquireExclusiveLock(pathProvider);
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
            if (marker.ManualRecoveryRequired)
            {
                string recoveryMessage = string.IsNullOrWhiteSpace(marker.LastError)
                    ? "服务器迁移自动回滚未完整完成，需要管理员使用安全备份人工恢复。"
                    : marker.LastError;
                ServerMigrationManager.WriteStatus(
                    pathProvider,
                    marker,
                    recoveryMessage,
                    ServerMigrationManager.GetSafetyBackupRoot(pathProvider, marker.PackageId));
                throw new ManualRecoveryRequiredException(recoveryMessage);
            }

            ServerMigrationManager.WriteStatus(
                pathProvider,
                marker,
                string.IsNullOrWhiteSpace(marker.StatusMessage)
                    ? string.IsNullOrWhiteSpace(marker.LastError)
                        ? "服务器迁移已结束。"
                        : marker.LastError
                    : marker.StatusMessage,
                marker.SafetyBackupRoot);
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
        DatabaseConnectionSettings? applicationSettings = null;
        DatabaseConnectionSettings? settings = null;
        PostgreSqlToolPaths? tools = null;
        ServerMigrationFileTransactionState? fileState = null;
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
            applicationSettings = DbHelper.LoadDatabaseSettings(pathProvider);
            if (!DatabaseModeHelper.UsesSharedDatabase(applicationSettings))
            {
                throw new ServiceValidationException(
                    "服务器迁移恢复要求当前服务器已配置 PostgreSQL 连接。");
            }
            settings = PostgreSqlMaintenanceConnectionResolver.Resolve(
                applicationSettings,
                pathProvider).ConnectionSettings;
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
            long stagedManifestBytes = ServerMigrationStorageBudget.SumManifestBytes(marker.Manifest);
            long stagedDatabaseBytes = new FileInfo(databaseDump).Length;
            long replacementBytes = Math.Max(0, stagedManifestBytes - stagedDatabaseBytes);
            long currentRuntimeBytes =
                ServerMigrationStorageBudget.SumDirectoryBytes(pathProvider.FileRoot) +
                ServerMigrationStorageBudget.SumDirectoryBytes(pathProvider.UserTemplateRoot) +
                ServerMigrationStorageBudget.SumDirectoryBytes(pathProvider.SingleWindowRoot) +
                ServerMigrationStorageBudget.SumDirectoryBytes(Path.Combine(pathProvider.DataRoot, "Marks")) +
                ServerMigrationStorageBudget.SumDirectoryBytes(pathProvider.ConfigRoot) +
                ServerMigrationStorageBudget.SumDirectoryBytes(pathProvider.SecurityRoot);
            ServerMigrationStorageBudget.EnsureAvailable(
                safetyRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(Math.Max(stagedDatabaseBytes * 2, stagedDatabaseBytes)),
                "创建 PostgreSQL 安全备份");
            ServerMigrationStorageBudget.EnsureAvailable(
                pathProvider.DataRoot,
                ServerMigrationStorageBudget.WithSafetyMargin(currentRuntimeBytes, replacementBytes),
                "创建运行文件安全备份");
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
                applicationSettings);

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
                marker.Manifest.SourcePathCaseSensitive,
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
            marker.ManualRecoveryRequired = false;
            ServerMigrationManager.WriteStatus(
                pathProvider,
                marker,
                "服务器迁移恢复已完成。恢复前安全备份已保留。",
                safetyRoot);
            AtomicFileHelper.TryDeleteFile(markerPath);
            AtomicFileHelper.TryDeleteDirectory(stagingRoot);
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
            if (!marker.ManualRecoveryRequired)
            {
                AtomicFileHelper.TryDeleteDirectory(stagingRoot);
            }
            TryWriteSecurityAudit(
                pathProvider,
                "apply-restore",
                requestContext,
                marker.PackageId,
                success: false,
                rollbackMessage);
            Console.Error.WriteLine($"Server migration restore failed: {rollbackMessage}");
            if (marker.ManualRecoveryRequired)
            {
                throw new ManualRecoveryRequiredException(rollbackMessage, ex);
            }

            return;
        }

        // The restore is committed once the terminal status is durable and the
        // pending marker has been removed. Security-audit IO is intentionally
        // best effort after that point: a log permission or disk error must
        // never roll back an already committed database/file switch.
        TryWriteSecurityAudit(
            pathProvider,
            "apply-restore",
            requestContext,
            marker.PackageId,
            success: true,
            "服务器迁移恢复完成。");
    }

    private static async Task RecoverInterruptedRestoreAsync(
        IAppPathProvider pathProvider,
        string markerPath,
        PendingServerMigrationRestore marker,
        ServerMigrationRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        string safetyRoot = ServerMigrationManager.GetSafetyBackupRoot(pathProvider, marker.PackageId);
        DatabaseConnectionSettings? settings = null;
        PostgreSqlToolPaths? tools = null;
        try
        {
            DatabaseConnectionSettings applicationSettings = DbHelper.LoadDatabaseSettings(pathProvider);
            settings = PostgreSqlMaintenanceConnectionResolver.Resolve(
                applicationSettings,
                pathProvider).ConnectionSettings;
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
        if (!marker.ManualRecoveryRequired)
        {
            AtomicFileHelper.TryDeleteDirectory(ServerMigrationPackageValidator.ResolvePath(
                ServerMigrationManager.GetControlRoot(pathProvider),
                marker.StagingDirectoryName));
        }
        TryWriteSecurityAudit(
            pathProvider,
            "recover-interrupted-restore",
            requestContext,
            marker.PackageId,
            success: false,
            message);
        Console.Error.WriteLine($"Interrupted server migration recovered: {message}");
        if (marker.ManualRecoveryRequired)
        {
            throw new ManualRecoveryRequiredException(message, interruption);
        }
    }

    private static async Task<string> TryRollbackAsync(
        IAppPathProvider pathProvider,
        string markerPath,
        PendingServerMigrationRestore marker,
        DatabaseConnectionSettings? settings,
        PostgreSqlToolPaths? tools,
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
                ServerMigrationFileTransactionState? state =
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
        marker.ManualRecoveryRequired = rollbackErrors.Count > 0;
        ServerMigrationManager.WriteStatus(pathProvider, marker, result, safetyRoot);
        if (!marker.ManualRecoveryRequired)
        {
            AtomicFileHelper.TryDeleteFile(markerPath);
        }
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
        TryWriteSecurityAudit(
            pathProvider,
            "apply-restore",
            new ServerMigrationRequestContext(string.Empty, string.Empty),
            string.Empty,
            success: false,
            marker.LastError);
        await Task.CompletedTask;
    }

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
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Server migration security audit write failed after state handling completed: {ex.Message}");
        }
    }

}
