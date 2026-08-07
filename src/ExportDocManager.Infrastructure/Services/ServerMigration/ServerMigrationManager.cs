using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public static class ServerMigrationManager
    {
        public static bool HasPendingRestore(IAppPathProvider pathProvider) =>
            File.Exists(GetPendingMarkerPath(pathProvider));

        public static string GetControlRoot(IAppPathProvider pathProvider) =>
            Path.Combine(pathProvider.SecurityRoot, ServerMigrationLayout.ControlDirectoryName);

        public static string GetPendingMarkerPath(IAppPathProvider pathProvider) =>
            Path.Combine(GetControlRoot(pathProvider), ServerMigrationLayout.PendingMarkerFileName);

        public static string GetStatusPath(IAppPathProvider pathProvider) =>
            Path.Combine(GetControlRoot(pathProvider), ServerMigrationLayout.StatusFileName);

        public static string GetSafetyBackupRoot(IAppPathProvider pathProvider, string packageId) =>
            Path.Combine(
                pathProvider.BackupRoot,
                ServerMigrationLayout.PackageDirectoryName,
                "Safety",
                packageId);

        internal static void WritePendingMarker(
            IAppPathProvider pathProvider,
            PendingServerMigrationRestore marker)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(marker);
            ValidateMarker(marker);
            if (!string.Equals(marker.Phase, ServerMigrationRestorePhase.Pending, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("新的服务器迁移任务必须处于等待重启阶段。");
            }
            string markerPath = GetPendingMarkerPath(pathProvider);
            if (File.Exists(markerPath))
            {
                throw new InvalidOperationException("已有服务器迁移任务等待重启执行。");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            RuntimeFilePermissionHelper.RestrictDirectory(Path.GetDirectoryName(markerPath)!);
            WriteMarker(markerPath, marker);
            WriteStatus(
                pathProvider,
                marker,
                "服务器迁移恢复已排队，等待服务重启。",
                GetSafetyBackupRoot(pathProvider, marker.PackageId));
        }

        internal static ServerMigrationRestoreStatusSnapshot ReadStatus(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string path = GetStatusPath(pathProvider);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<ServerMigrationRestoreStatusSnapshot>(
                    File.ReadAllText(path),
                    ServerMigrationService.JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new ServerMigrationRestoreStatusSnapshot
                {
                    Phase = ServerMigrationRestorePhase.Failed,
                    UpdatedAtUtc = File.GetLastWriteTimeUtc(path),
                    Message = $"服务器迁移状态文件损坏：{ex.Message}"
                };
            }
        }

        public static async Task ApplyPendingRestoreAsync(
            IAppPathProvider pathProvider,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string markerPath = GetPendingMarkerPath(pathProvider);
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
                ValidateMarker(marker);
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
            string stagingRoot = ServerMigrationService.ResolvePath(
                GetControlRoot(pathProvider),
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

            string safetyRoot = GetSafetyBackupRoot(pathProvider, marker.PackageId);
            string databaseDump = ServerMigrationService.ResolvePath(
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
                    throw new InvalidOperationException(
                        "服务器迁移恢复要求当前服务器已配置 PostgreSQL 连接。");
                }
                tools = PostgreSqlToolLocator.Resolve(pathProvider);
                if (!tools.ToolsReady)
                {
                    throw new InvalidOperationException(
                        "服务器迁移恢复缺少兼容的 PostgreSQL 18 客户端工具。");
                }
                await ServerMigrationPostgreSql.ValidateProductDumpAsync(
                    tools,
                    settings,
                    databaseDump,
                    marker.ValidationDatabaseName,
                    cancellationToken).ConfigureAwait(false);
                marker.ValidationDatabaseName = string.Empty;
                WriteMarker(markerPath, marker);

                UpdatePhase(
                    pathProvider,
                    markerPath,
                    marker,
                    ServerMigrationRestorePhase.SafetyBackup,
                    "正在创建数据库与运行文件安全备份。");
                Directory.CreateDirectory(safetyRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(safetyRoot);
                string safetyDump = Path.Combine(safetyRoot, "before-restore.dump");
                await ServerMigrationPostgreSql.CreateSafetyBackupAsync(
                    tools,
                    settings,
                    safetyDump,
                    cancellationToken).ConfigureAwait(false);
                fileState = ServerMigrationFileTransaction.Prepare(
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
                await ServerMigrationPostgreSql.RestoreDatabaseAsync(
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
                ServerMigrationFileTransaction.Apply(fileState);
                ServerMigrationFileTransaction.CleanupPrepared(fileState);

                marker.Phase = ServerMigrationRestorePhase.Completed;
                marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
                marker.LastError = string.Empty;
                WriteMarker(markerPath, marker);
                WriteStatus(
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
            string safetyRoot = GetSafetyBackupRoot(pathProvider, marker.PackageId);
            DatabaseConnectionSettings settings = null;
            PostgreSqlToolPaths tools = null;
            try
            {
                settings = DbHelper.LoadDatabaseSettings();
                tools = PostgreSqlToolLocator.Resolve(pathProvider);
                if (!string.IsNullOrWhiteSpace(marker.ValidationDatabaseName) &&
                    DatabaseModeHelper.UsesSharedDatabase(settings))
                {
                    await ServerMigrationPostgreSql.TryDropDatabaseAsync(
                        settings,
                        marker.ValidationDatabaseName,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // The rollback result below records the actionable aggregate failure.
            }

            var interruption = new InvalidOperationException(
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
            AtomicFileHelper.TryDeleteDirectory(ServerMigrationService.ResolvePath(
                GetControlRoot(pathProvider),
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
            bool databaseMayHaveChanged = marker.Phase is
                ServerMigrationRestorePhase.ApplyingDatabase or
                ServerMigrationRestorePhase.ApplyingFiles or
                ServerMigrationRestorePhase.RollingBack or
                ServerMigrationRestorePhase.Completed;
            marker.Phase = ServerMigrationRestorePhase.RollingBack;
            marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
            marker.LastError = originalError.Message;
            TryWriteMarker(markerPath, marker);
            WriteStatus(
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
                        ServerMigrationFileTransaction.ReadState(safetyRoot);
                    if (state != null)
                    {
                        if (marker.Phase is ServerMigrationRestorePhase.RollingBack)
                        {
                            ServerMigrationFileTransaction.Rollback(safetyRoot);
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
                    await ServerMigrationPostgreSql.RestoreDatabaseAsync(
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
                ? $"{originalError.Message} 已自动回滚，服务将继续启动。"
                : $"{originalError.Message} 自动回滚未完全成功：{string.Join("；", rollbackErrors)} 请使用安全备份 {safetyRoot} 人工恢复。";
            marker.Phase = ServerMigrationRestorePhase.Failed;
            marker.UpdatedAtUtc = DateTimeOffset.UtcNow;
            marker.LastError = result;
            TryWriteMarker(markerPath, marker);
            WriteStatus(pathProvider, marker, result, safetyRoot);
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
            WriteMarker(markerPath, marker);
            WriteStatus(
                pathProvider,
                marker,
                message,
                GetSafetyBackupRoot(pathProvider, marker.PackageId));
        }

        private static void WriteStatus(
            IAppPathProvider pathProvider,
            PendingServerMigrationRestore marker,
            string message,
            string safetyBackupRoot)
        {
            string path = GetStatusPath(pathProvider);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var status = new ServerMigrationRestoreStatusSnapshot
            {
                PackageId = marker.PackageId,
                PackageFileName = marker.PackageFileName,
                Phase = marker.Phase,
                Attempt = marker.Attempt,
                RequestedBy = marker.RequestedBy,
                UpdatedAtUtc = marker.UpdatedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : marker.UpdatedAtUtc,
                Message = message,
                SafetyBackupRoot = safetyBackupRoot
            };
            AtomicFileHelper.WriteAllTextAtomic(
                path,
                JsonSerializer.Serialize(status, ServerMigrationService.JsonOptions));
            RuntimeFilePermissionHelper.RestrictFile(path);
        }

        private static void WriteMarker(string markerPath, PendingServerMigrationRestore marker)
        {
            AtomicFileHelper.WriteAllTextAtomic(
                markerPath,
                JsonSerializer.Serialize(marker, ServerMigrationService.JsonOptions));
            RuntimeFilePermissionHelper.RestrictFile(markerPath);
        }

        private static void TryWriteMarker(string markerPath, PendingServerMigrationRestore marker)
        {
            try
            {
                WriteMarker(markerPath, marker);
            }
            catch
            {
            }
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
            WriteStatus(pathProvider, marker, marker.LastError, string.Empty);
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

        private static void ValidateMarker(PendingServerMigrationRestore marker)
        {
            if (marker == null ||
                marker.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
                !Guid.TryParseExact(marker.PackageId, "N", out _) ||
                !string.Equals(
                    marker.StagingDirectoryName,
                    $"pending-{marker.PackageId}",
                    StringComparison.Ordinal) ||
                !ServerMigrationRestorePhase.IsKnown(marker.Phase) ||
                marker.Manifest == null ||
                marker.Manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
                !string.Equals(
                    marker.Manifest.PackageId,
                    marker.PackageId,
                    StringComparison.Ordinal) ||
                marker.Manifest.Files is null ||
                marker.Manifest.Files.Any(file => file is null))
            {
                throw new InvalidDataException("服务器迁移恢复标记无效。");
            }
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
                _ = ServerMigrationService.NormalizeRelativePath(file.RelativePath);
                string path = ServerMigrationService.ResolvePath(stagingRoot, file.RelativePath);
                if (!File.Exists(path) ||
                    new FileInfo(path).Length != file.SizeBytes ||
                    !string.Equals(
                        ServerMigrationService.ComputeSha256(path),
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
            byte[] packageKey = File.ReadAllBytes(ServerMigrationService.ResolvePath(
                stagingRoot,
                masterKeyEntry));
            try
            {
                if (packageKey.Length != 32 ||
                    !CryptographicOperations.FixedTimeEquals(configuredKey, packageKey))
                {
                    throw new InvalidOperationException(
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
}
