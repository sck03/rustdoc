using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        public async Task<PostgreSqlPhysicalBackupResult> CreatePostgreSqlPhysicalBackupAsync(CancellationToken cancellationToken = default)
        {
            EnsurePostgreSqlReady();
            var tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            if (string.IsNullOrWhiteSpace(tools.PgDumpPath))
            {
                throw new InfrastructureServiceException("未找到 pg_dump。请把 PostgreSQL 客户端工具放到程序根 Tools/PostgreSQL/bin，或用 EXPORTDOCMANAGER_POSTGRES_BIN 指向工具目录。");
            }

            await PostgreSqlBackupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ValidatePgDumpCompatibilityAsync(tools.PgDumpPath, cancellationToken).ConfigureAwait(false);

                string database = DbHelper.NormalizePostgreSqlText(_maintenanceDatabaseSettings.PostgreSqlDatabase);
                string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = $"{timestamp}_{NormalizeFileToken(database)}_{Guid.NewGuid():N}.dump";
                string outputPath = Path.Combine(PostgreSqlBackupRoot, fileName);
                string tempPath = AtomicFileHelper.GetSiblingTempFilePath(outputPath);
                var arguments = new[]
                {
                    "--format=custom",
                    "--blobs",
                    "--verbose",
                    "--no-owner",
                    "--file", tempPath,
                    "--host", DbHelper.NormalizePostgreSqlText(_maintenanceDatabaseSettings.PostgreSqlHost),
                    "--port", DbHelper.NormalizePostgreSqlPort(_maintenanceDatabaseSettings.PostgreSqlPort).ToString(),
                    "--username", DbHelper.NormalizePostgreSqlText(_maintenanceDatabaseSettings.PostgreSqlUsername),
                    "--dbname", database
                };

                try
                {
                    await RunPostgreSqlToolAsync(
                        tools.PgDumpPath,
                        arguments,
                        PostgreSqlBackupTimeout,
                        cancellationToken).ConfigureAwait(false);
                    if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    {
                        throw new InfrastructureServiceException("pg_dump 未生成有效的备份文件。");
                    }

                    RuntimeFilePermissionHelper.RestrictFile(tempPath);
                    AtomicFileHelper.ReplaceFile(tempPath, outputPath);
                    RuntimeFilePermissionHelper.RestrictFile(outputPath);
                    var file = new FileInfo(outputPath);
                    return new PostgreSqlPhysicalBackupResult(
                        true,
                        $"PostgreSQL 团队库物理备份已创建：{file.Name}",
                        file.Name,
                        file.FullName,
                        file.Length,
                        PostgreSqlBackupRoot,
                        PostgreSqlPhysicalBackupStoragePolicy);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(tempPath);
                }
            }
            finally
            {
                PostgreSqlBackupGate.Release();
            }
        }

        public async Task<PostgreSqlRestorePlanResult> CreatePostgreSqlRestorePlanAsync(
            PostgreSqlRestorePlanRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string backupPath = ResolveKnownPostgreSqlBackupPath(request.BackupFileName);
            string targetDatabase = string.IsNullOrWhiteSpace(request.TargetDatabase)
                ? DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlDatabase)
                : request.TargetDatabase.Trim();
            string appRole = string.IsNullOrWhiteSpace(request.ApplicationRole)
                ? DbHelper.NormalizePostgreSqlText(_databaseSettings.PostgreSqlUsername)
                : request.ApplicationRole.Trim();
            targetDatabase = NormalizePostgreSqlIdentifier(targetDatabase, "目标数据库名");
            appRole = NormalizePostgreSqlIdentifier(appRole, "应用账号");
            IReadOnlyList<string> oldOwnerRoles = NormalizePostgreSqlOwnerRoles(request.OldOwnerRoles);

            var tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string planRoot = EnsureDirectory(Path.Combine(PostgreSqlRestorePlanRoot, $"{timestamp}_{Guid.NewGuid():N}"));
            string ownershipSqlPath = Path.Combine(planRoot, "post_restore_ownership.sql");
            string restoreScriptPath = Path.Combine(planRoot, OperatingSystem.IsWindows() ? "restore-postgresql.ps1" : "restore-postgresql.sh");

            try
            {
                await File.WriteAllTextAsync(
                    ownershipSqlPath,
                    BuildPostRestoreOwnershipSql(
                        targetDatabase,
                        _postgreSqlOwnerRole,
                        appRole,
                        oldOwnerRoles),
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    restoreScriptPath,
                    BuildRestoreScript(
                        backupPath,
                        targetDatabase,
                        _postgreSqlOwnerRole,
                        ownershipSqlPath,
                        tools),
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                RuntimeFilePermissionHelper.RestrictFile(ownershipSqlPath);
                RuntimeFilePermissionHelper.RestrictFile(restoreScriptPath);

                return new PostgreSqlRestorePlanResult(
                    true,
                    "PostgreSQL 还原计划已生成。请在目标服务器复核脚本后执行，完成后重启应用客户端。",
                    planRoot,
                    restoreScriptPath,
                    ownershipSqlPath,
                    backupPath,
                    PostgreSqlRestorePlanStoragePolicy);
            }
            catch
            {
                TryDeleteDirectory(planRoot);
                throw;
            }
        }

        private static string NormalizePostgreSqlIdentifier(string value, string fieldName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ServiceValidationException($"{fieldName}不能为空。");
            }

            if (normalized.Any(char.IsControl) ||
                Encoding.UTF8.GetByteCount(normalized) > MaximumPostgreSqlIdentifierBytes)
            {
                throw new ServiceValidationException(
                    $"{fieldName}不能包含控制字符，且 UTF-8 长度不能超过 {MaximumPostgreSqlIdentifierBytes} 字节。");
            }

            return normalized;
        }

        private static IReadOnlyList<string> NormalizePostgreSqlOwnerRoles(IReadOnlyList<string> values)
        {
            var roles = (values ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roles.Length > MaximumPostgreSqlOldOwnerRoleCount)
            {
                throw new ServiceValidationException(
                    $"原数据库所有者不能超过 {MaximumPostgreSqlOldOwnerRoleCount} 个。");
            }

            return roles
                .Select(role => NormalizePostgreSqlIdentifier(role, "原数据库所有者"))
                .ToArray();
        }

    }
}
