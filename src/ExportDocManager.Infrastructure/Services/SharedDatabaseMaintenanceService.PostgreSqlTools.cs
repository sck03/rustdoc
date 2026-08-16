using System.Diagnostics;
using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        private const int MaximumPostgreSqlToolOutputCharacters = 1 * 1024 * 1024;

        private async Task ValidatePgDumpCompatibilityAsync(
            string pgDumpPath,
            CancellationToken cancellationToken)
        {
            PostgreSqlToolRunResult versionResult = await RunPostgreSqlToolAsync(
                pgDumpPath,
                ["--version"],
                PostgreSqlVersionCheckTimeout,
                cancellationToken).ConfigureAwait(false);
            int pgDumpMajor = ParsePostgreSqlMajorVersion(
                string.IsNullOrWhiteSpace(versionResult.StandardOutput)
                    ? versionResult.StandardError
                    : versionResult.StandardOutput,
                "pg_dump");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            string serverVersionText = context.Database.GetDbConnection().ServerVersion ?? string.Empty;
            int serverMajor = ParsePostgreSqlMajorVersion(serverVersionText, "PostgreSQL 服务器");
            EnsurePgDumpVersionSupported(pgDumpMajor, serverMajor);
        }

        private async Task ValidatePostgreSqlDumpAsync(
            string pgRestorePath,
            string dumpPath,
            CancellationToken cancellationToken)
        {
            await RunPostgreSqlToolAsync(
                pgRestorePath,
                BuildPgRestoreValidationArguments(dumpPath),
                PostgreSqlBackupTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        internal static IReadOnlyList<string> BuildPgRestoreValidationArguments(string dumpPath)
        {
            if (string.IsNullOrWhiteSpace(dumpPath))
            {
                throw new ArgumentException("PostgreSQL 备份路径不能为空。", nameof(dumpPath));
            }

            return ["--list", dumpPath];
        }

        private async Task<PostgreSqlToolRunResult> RunPostgreSqlToolAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrEmpty(_maintenanceDatabaseSettings.PostgreSqlPassword))
            {
                startInfo.Environment["PGPASSWORD"] = _maintenanceDatabaseSettings.PostgreSqlPassword;
            }

            using var process = new Process { StartInfo = startInfo };

            if (!process.Start())
            {
                throw new InfrastructureServiceException("无法启动 PostgreSQL 客户端工具。");
            }

            Task<string> standardOutputTask = ReadProcessOutputAsync(process.StandardOutput);
            Task<string> standardErrorTask = ReadProcessOutputAsync(process.StandardError);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                await DrainExitedProcessAsync(process).ConfigureAwait(false);
                await ObserveProcessOutputAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new TimeoutException(
                    $"PostgreSQL 客户端工具执行超过 {timeout.TotalMinutes:0.#} 分钟，进程已终止。");
            }

            string standardOutput = await standardOutputTask.ConfigureAwait(false);
            string standardError = await standardErrorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string message = !string.IsNullOrWhiteSpace(standardError)
                    ? standardError.Trim()
                    : standardOutput.Trim();
                throw new InfrastructureServiceException("PostgreSQL 客户端工具执行失败，请检查数据库连接和运行目录权限。", new InvalidOperationException(message));
            }

            return new PostgreSqlToolRunResult(standardOutput.Trim(), standardError.Trim());
        }

        private static async Task<string> ReadProcessOutputAsync(StreamReader reader)
        {
            return await BoundedProcessOutput.ReadAsync(
                reader,
                MaximumPostgreSqlToolOutputCharacters,
                "[PostgreSQL 工具输出过长，已截断]").ConfigureAwait(false);
        }

        private static async Task ObserveProcessOutputAsync(params Task<string>[] outputTasks)
        {
            try
            {
                await BoundedProcessOutput.ObserveAsync(TimeSpan.FromSeconds(5), outputTasks)
                    .ConfigureAwait(false);
            }
            catch
            {
                // The original timeout/cancellation is more useful than a stream-drain error.
            }
        }

        internal static int ParsePostgreSqlMajorVersion(string value, string sourceName)
        {
            Match match;
            try
            {
                match = PostgreSqlVersionPattern.Match(value ?? string.Empty);
            }
            catch (RegexMatchTimeoutException exception)
            {
                throw new InfrastructureServiceException($"无法解析 {sourceName} 版本。", exception);
            }

            if (!match.Success ||
                !int.TryParse(match.Groups["major"].Value, out int major) ||
                major <= 0)
            {
                throw new InfrastructureServiceException($"无法解析 {sourceName} 版本：{value}");
            }

            return major;
        }

        internal static void EnsurePgDumpVersionSupported(int pgDumpMajor, int serverMajor)
        {
            if (pgDumpMajor < serverMajor)
            {
                throw new InfrastructureServiceException(
                    $"pg_dump 主版本 {pgDumpMajor} 低于 PostgreSQL 服务器主版本 {serverMajor}。请把匹配或更新版本的客户端工具放入程序 Tools/PostgreSQL/bin。");
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between HasExited and Kill.
            }
        }

        private static async Task DrainExitedProcessAsync(Process process)
        {
            try
            {
                await BoundedProcessOutput.DrainProcessAsync(process, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch
            {
                // A timed-out child is already being torn down; do not mask the original error.
            }
        }

        private string ResolveKnownPostgreSqlBackupPath(string backupFileName)
        {
            string fileName = (backupFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ServiceValidationException("PostgreSQL 备份文件名不能为空。");
            }

            if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new ServiceValidationException("只能选择 PostgreSQL 备份列表中的文件名，不能传入路径。");
            }

            var item = ListPostgreSqlPhysicalBackups()
                .FirstOrDefault(backup => string.Equals(backup.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            return item?.FullPath ?? throw new ResourceNotFoundException("未找到指定 PostgreSQL 物理备份。");
        }

        internal string BuildRestoreScript(
            string backupPath,
            string targetDatabase,
            string restoreRole,
            string ownershipSqlPath,
            PostgreSqlToolPaths tools)
        {
            string host = DbHelper.NormalizePostgreSqlText(_maintenanceDatabaseSettings.PostgreSqlHost);
            string port = DbHelper.NormalizePostgreSqlPort(_maintenanceDatabaseSettings.PostgreSqlPort).ToString();
            string username = DbHelper.NormalizePostgreSqlText(_maintenanceDatabaseSettings.PostgreSqlUsername);
            string pgRestore = string.IsNullOrWhiteSpace(tools.PgRestorePath) ? "pg_restore" : tools.PgRestorePath;
            string psql = string.IsNullOrWhiteSpace(tools.PsqlPath) ? "psql" : tools.PsqlPath;

            if (OperatingSystem.IsWindows())
            {
                return $$"""
$ErrorActionPreference = 'Stop'
# PostgreSQL 团队版业务数据库还原计划。执行前请确认目标服务器、数据库名和应用账号。
# 如需避免输入密码，可临时设置 PGPASSWORD，或使用 pgpass.conf / 密码管理工具。
$pgRestore = {{QuotePowerShellLiteral(pgRestore)}}
$restoreArgs = @(
    '--clean', '--if-exists', '--no-owner', '--single-transaction',
    '--role', {{QuotePowerShellLiteral(restoreRole)}},
    '--host', {{QuotePowerShellLiteral(host)}},
    '--port', {{QuotePowerShellLiteral(port)}},
    '--username', {{QuotePowerShellLiteral(username)}},
    '--dbname', {{QuotePowerShellLiteral(targetDatabase)}},
    {{QuotePowerShellLiteral(backupPath)}}
)
& $pgRestore @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }

$psql = {{QuotePowerShellLiteral(psql)}}
$psqlArgs = @(
    '--host', {{QuotePowerShellLiteral(host)}},
    '--port', {{QuotePowerShellLiteral(port)}},
    '--username', {{QuotePowerShellLiteral(username)}},
    '--dbname', {{QuotePowerShellLiteral(targetDatabase)}},
    '--single-transaction',
    '--set', 'ON_ERROR_STOP=1',
    '--file', {{QuotePowerShellLiteral(ownershipSqlPath)}}
)
& $psql @psqlArgs
if ($LASTEXITCODE -ne 0) { throw "psql failed with exit code $LASTEXITCODE." }
""";
            }

            return $"""
#!/usr/bin/env sh
set -eu
# PostgreSQL 团队版业务数据库还原计划。执行前请确认目标服务器、数据库名和应用账号。
{QuotePosixShellArgument(pgRestore)} --clean --if-exists --no-owner --single-transaction --role {QuotePosixShellArgument(restoreRole)} --host {QuotePosixShellArgument(host)} --port {QuotePosixShellArgument(port)} --username {QuotePosixShellArgument(username)} --dbname {QuotePosixShellArgument(targetDatabase)} {QuotePosixShellArgument(backupPath)}
{QuotePosixShellArgument(psql)} --host {QuotePosixShellArgument(host)} --port {QuotePosixShellArgument(port)} --username {QuotePosixShellArgument(username)} --dbname {QuotePosixShellArgument(targetDatabase)} --single-transaction --set=ON_ERROR_STOP=1 --file {QuotePosixShellArgument(ownershipSqlPath)}
""";
        }

        internal static string BuildPostRestoreOwnershipSql(
            string targetDatabase,
            string appRole,
            IReadOnlyList<string> oldOwnerRoles) =>
            BuildPostRestoreOwnershipSql(targetDatabase, appRole, appRole, oldOwnerRoles);

        internal static string BuildPostRestoreOwnershipSql(
            string targetDatabase,
            string ownerRole,
            string appRole,
            IReadOnlyList<string> oldOwnerRoles)
        {
            targetDatabase = NormalizePostgreSqlIdentifier(targetDatabase, "目标数据库名");
            ownerRole = NormalizePostgreSqlIdentifier(ownerRole, "所有者角色");
            appRole = NormalizePostgreSqlIdentifier(appRole, "应用账号");
            string ownerRoleLiteral = ToSqlLiteral(ownerRole);
            string targetDatabaseComment = NormalizeSqlCommentValue(targetDatabase);
            string ownerRoleComment = NormalizeSqlCommentValue(ownerRole);
            string appRoleComment = NormalizeSqlCommentValue(appRole);
            IReadOnlyList<string> oldRoles = NormalizePostgreSqlOwnerRoles(oldOwnerRoles);
            string reassignBlock = oldRoles.Count == 0
                ? "-- 如迁移后存在旧 owner 角色，可按需执行：REASSIGN OWNED BY old_role TO " + QuoteIdentifier(ownerRole) + ";" + Environment.NewLine
                : string.Join(Environment.NewLine, oldRoles.Select(role => $"REASSIGN OWNED BY {QuoteIdentifier(role)} TO {QuoteIdentifier(ownerRole)};"));

            return $"""
-- PostgreSQL 团队版业务数据库还原后 owner / schema / table / sequence / 权限改派脚本
-- Target database: {targetDatabaseComment}
-- Owner role: {ownerRoleComment}
-- Application role: {appRoleComment}

{reassignBlock}

DO $$
DECLARE
    owner_role text := {ownerRoleLiteral};
    item record;
BEGIN
    FOR item IN
        SELECT nspname
        FROM pg_namespace
        WHERE nspname NOT IN ('pg_catalog', 'information_schema')
          AND nspname NOT LIKE 'pg_toast%'
    LOOP
        EXECUTE format('ALTER SCHEMA %I OWNER TO %I', item.nspname, owner_role);
    END LOOP;

    FOR item IN
        SELECT schemaname, tablename
        FROM pg_tables
        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER TABLE %I.%I OWNER TO %I', item.schemaname, item.tablename, owner_role);
    END LOOP;

    FOR item IN
        SELECT sequence_schema, sequence_name
        FROM information_schema.sequences
        WHERE sequence_schema NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO %I', item.sequence_schema, item.sequence_name, owner_role);
    END LOOP;

    FOR item IN
        SELECT schemaname, viewname
        FROM pg_views
        WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format('ALTER VIEW %I.%I OWNER TO %I', item.schemaname, item.viewname, owner_role);
    END LOOP;

    FOR item IN
        SELECT n.nspname AS schema_name,
               p.proname AS routine_name,
               p.prokind AS routine_kind,
               pg_get_function_identity_arguments(p.oid) AS args
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
    LOOP
        IF item.routine_kind = 'p' THEN
            EXECUTE format('ALTER PROCEDURE %I.%I(%s) OWNER TO %I', item.schema_name, item.routine_name, item.args, owner_role);
        ELSIF item.routine_kind = 'a' THEN
            EXECUTE format('ALTER AGGREGATE %I.%I(%s) OWNER TO %I', item.schema_name, item.routine_name, item.args, owner_role);
        ELSE
            EXECUTE format('ALTER FUNCTION %I.%I(%s) OWNER TO %I', item.schema_name, item.routine_name, item.args, owner_role);
        END IF;
    END LOOP;
END $$;

ALTER DATABASE {QuoteIdentifier(targetDatabase)} OWNER TO {QuoteIdentifier(ownerRole)};
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE {QuoteIdentifier(targetDatabase)} TO {QuoteIdentifier(appRole)};
GRANT USAGE ON SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {QuoteIdentifier(appRole)};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA public TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES FOR ROLE {QuoteIdentifier(ownerRole)} IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES FOR ROLE {QuoteIdentifier(ownerRole)} IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {QuoteIdentifier(appRole)};
ALTER DEFAULT PRIVILEGES FOR ROLE {QuoteIdentifier(ownerRole)} IN SCHEMA public GRANT EXECUTE ON ROUTINES TO {QuoteIdentifier(appRole)};
""";
        }

    }
}
