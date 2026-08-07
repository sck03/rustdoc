using System.Diagnostics;
using ExportDocManager.DataAccess;
using ExportDocManager.Services;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Npgsql;

namespace ExportDocManager.Services.Infrastructure
{
    internal static class ServerMigrationPostgreSql
    {
        public static async Task ValidateDumpContainerAsync(
            PostgreSqlToolPaths tools,
            string dumpPath,
            CancellationToken cancellationToken)
        {
            EnsureReady(tools);
            await RunToolAsync(
                tools.PgRestorePath,
                ["--list", dumpPath],
                password: string.Empty,
                timeout: TimeSpan.FromMinutes(5),
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task CreateSafetyBackupAsync(
            PostgreSqlToolPaths tools,
            DatabaseConnectionSettings settings,
            string destination,
            CancellationToken cancellationToken)
        {
            EnsureReady(tools);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporaryPath = AtomicFileHelper.GetSiblingTempFilePath(destination);
            try
            {
                await RunToolAsync(
                    tools.PgDumpPath,
                    [
                        "--format=custom",
                        "--blobs",
                        "--no-owner",
                        "--file", temporaryPath,
                        "--host", settings.PostgreSqlHost,
                        "--port", settings.PostgreSqlPort.ToString(),
                        "--username", settings.PostgreSqlUsername,
                        "--dbname", settings.PostgreSqlDatabase
                    ],
                    settings.PostgreSqlPassword,
                    TimeSpan.FromMinutes(30),
                    cancellationToken).ConfigureAwait(false);

                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                {
                    throw new InvalidDataException("服务器迁移恢复前 PostgreSQL 安全备份为空。");
                }

                AtomicFileHelper.ReplaceFile(temporaryPath, destination);
                RuntimeFilePermissionHelper.RestrictFile(destination);
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(temporaryPath);
            }
        }

        public static Task RestoreDatabaseAsync(
            PostgreSqlToolPaths tools,
            DatabaseConnectionSettings settings,
            string dumpPath,
            CancellationToken cancellationToken)
        {
            EnsureReady(tools);
            return RunToolAsync(
                tools.PgRestorePath,
                [
                    "--clean",
                    "--if-exists",
                    "--exit-on-error",
                    "--single-transaction",
                    "--no-owner",
                    "--no-privileges",
                    "--host", settings.PostgreSqlHost,
                    "--port", settings.PostgreSqlPort.ToString(),
                    "--username", settings.PostgreSqlUsername,
                    "--dbname", settings.PostgreSqlDatabase,
                    dumpPath
                ],
                settings.PostgreSqlPassword,
                TimeSpan.FromMinutes(60),
                cancellationToken);
        }

        public static async Task ValidateProductDumpAsync(
            PostgreSqlToolPaths tools,
            DatabaseConnectionSettings settings,
            string dumpPath,
            string validationDatabaseName,
            CancellationToken cancellationToken)
        {
            EnsureReady(tools);
            ValidateGeneratedDatabaseName(validationDatabaseName);
            await TryDropDatabaseAsync(settings, validationDatabaseName, cancellationToken)
                .ConfigureAwait(false);
            await CreateDatabaseAsync(settings, validationDatabaseName, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                DatabaseConnectionSettings validationSettings = CloneWithDatabase(
                    settings,
                    validationDatabaseName);
                await RunToolAsync(
                    tools.PgRestorePath,
                    [
                        "--exit-on-error",
                        "--single-transaction",
                        "--no-owner",
                        "--no-privileges",
                        "--host", settings.PostgreSqlHost,
                        "--port", settings.PostgreSqlPort.ToString(),
                        "--username", settings.PostgreSqlUsername,
                        "--dbname", validationDatabaseName,
                        dumpPath
                    ],
                    settings.PostgreSqlPassword,
                    TimeSpan.FromMinutes(60),
                    cancellationToken).ConfigureAwait(false);
                await ValidateProductSchemaAsync(validationSettings, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await TryDropDatabaseAsync(settings, validationDatabaseName, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        public static async Task TryDropDatabaseAsync(
            DatabaseConnectionSettings settings,
            string databaseName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return;
            }
            ValidateGeneratedDatabaseName(databaseName);
            await using var connection = new NpgsqlConnection(
                DbHelper.BuildPostgreSqlConnectionString(settings));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (NpgsqlCommand terminate = connection.CreateCommand())
            {
                terminate.CommandText =
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid();";
                terminate.Parameters.AddWithValue("database", databaseName);
                await terminate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using NpgsqlCommand drop = connection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)};";
            await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task CreateDatabaseAsync(
            DatabaseConnectionSettings settings,
            string databaseName,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = new NpgsqlConnection(
                    DbHelper.BuildPostgreSqlConnectionString(settings));
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)} TEMPLATE template0 ENCODING 'UTF8';";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == "42501")
            {
                throw new InfrastructureServiceException(
                    "PostgreSQL 恢复账号必须具备 CREATEDB 权限，才能在覆盖业务库前验证备份身份和架构版本。",
                    ex);
            }
        }

        private static async Task ValidateProductSchemaAsync(
            DatabaseConnectionSettings settings,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(
                DbHelper.BuildPostgreSqlConnectionString(settings));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (NpgsqlCommand version = connection.CreateCommand())
            {
                version.CommandText =
                    "SELECT \"Version\" FROM \"__ExportDocManagerSchema\" WHERE \"Id\" = 1;";
                object value;
                try
                {
                    value = await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01")
                {
                    throw new InvalidDataException("PostgreSQL 备份不是 ExportDocManager 产品数据库：缺少架构版本表。", ex);
                }
                if (value == null || Convert.ToInt32(value) != DatabaseSchemaBaseline.CurrentVersion)
                {
                    throw new InvalidDataException(
                        $"PostgreSQL 备份架构版本不受支持；要求版本 {DatabaseSchemaBaseline.CurrentVersion}。当前项目尚未投产，不提供旧架构兼容恢复。");
                }
            }

            await using NpgsqlCommand identity = connection.CreateCommand();
            identity.CommandText =
                "SELECT to_regclass('public.\"Users\"') IS NOT NULL " +
                "AND to_regclass('public.\"Invoices\"') IS NOT NULL " +
                "AND to_regclass('public.\"AuditLogs\"') IS NOT NULL;";
            if (await identity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            {
                throw new InvalidDataException("PostgreSQL 备份缺少 ExportDocManager 核心业务表。");
            }

            await using NpgsqlCommand smoke = connection.CreateCommand();
            smoke.CommandText = "SELECT COUNT(*) FROM \"Users\";";
            _ = await smoke.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task RunToolAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string password,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executable);
            ArgumentNullException.ThrowIfNull(arguments);
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            if (!string.IsNullOrEmpty(password))
            {
                startInfo.Environment["PGPASSWORD"] = password;
            }

            using var process = Process.Start(startInfo)
                ?? throw new InfrastructureServiceException("无法启动 PostgreSQL 客户端工具。");
            Task<string> standardOutput = ReadProcessOutputAsync(process.StandardOutput);
            Task<string> standardError = ReadProcessOutputAsync(process.StandardError);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                await DrainExitedProcessAsync(process).ConfigureAwait(false);
                await ObserveProcessOutputAsync(standardOutput, standardError).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                throw new ServiceTimeoutException("PostgreSQL 客户端工具执行超时。");
            }

            string output = (await standardOutput.ConfigureAwait(false)).Trim();
            string error = (await standardError.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                throw new InfrastructureServiceException(
                    "PostgreSQL 客户端工具执行失败，请检查数据库连接和运行目录权限。",
                    new InvalidOperationException((string.IsNullOrWhiteSpace(error) ? output : error).Trim()));
            }
        }

        internal static async Task<string> ReadProcessOutputAsync(StreamReader reader)
        {
            return await BoundedProcessOutput.ReadAsync(
                reader,
                truncationMessage: "[PostgreSQL 工具输出过长，已截断]").ConfigureAwait(false);
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

        private static DatabaseConnectionSettings CloneWithDatabase(
            DatabaseConnectionSettings source,
            string databaseName)
        {
            return new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = source.PostgreSqlHost,
                PostgreSqlPort = source.PostgreSqlPort,
                PostgreSqlDatabase = databaseName,
                PostgreSqlUsername = source.PostgreSqlUsername,
                PostgreSqlPassword = source.PostgreSqlPassword,
                PostgreSqlAdditionalOptions = source.PostgreSqlAdditionalOptions
            };
        }

        private static void EnsureReady(PostgreSqlToolPaths tools)
        {
            if (tools == null || !tools.ToolsReady)
            {
                throw new InfrastructureServiceException("PostgreSQL 18 客户端工具不完整或版本不兼容。");
            }
        }

        private static void ValidateGeneratedDatabaseName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 63 ||
                !value.StartsWith("edm_migration_verify_", StringComparison.Ordinal) ||
                value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            {
                throw new ServiceValidationException("服务器迁移临时验证数据库名称无效。");
            }
        }

        private static string QuoteIdentifier(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
