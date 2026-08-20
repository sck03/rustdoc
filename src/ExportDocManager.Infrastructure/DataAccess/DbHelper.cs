using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Npgsql;

namespace ExportDocManager.DataAccess
{
    public static class DbHelper
    {
        public const string PostgreSqlMaximumPoolSizeEnvironmentVariable = "EXPORTDOCMANAGER_DB_MAX_POOL_SIZE";
        public const string PostgreSqlPasswordEnvironmentVariable = PostgreSqlPasswordResolver.PasswordEnvironmentVariable;
        public const string PostgreSqlPasswordFileEnvironmentVariable = PostgreSqlPasswordResolver.PasswordFileEnvironmentVariable;
        private static readonly HashSet<string> AllowedPostgreSqlAdditionalOptionNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "SSL Mode",
                "Trust Server Certificate",
                "Root Certificate",
                "SSL Certificate",
                "SSL Key",
                "Check Certificate Revocation",
                "Channel Binding",
                "Timeout",
                "Command Timeout",
                "Cancellation Timeout",
                "Keepalive",
                "Tcp Keepalive",
                "Tcp Keepalive Time",
                "Tcp Keepalive Interval",
                "Pooling",
                "Minimum Pool Size",
                "Maximum Pool Size",
                "Connection Idle Lifetime",
                "Connection Pruning Interval",
                "Connection Lifetime",
                "No Reset On Close",
                "Enlist",
                "Multiplexing",
                "Read Buffer Size",
                "Write Buffer Size",
                "Socket Receive Buffer Size",
                "Socket Send Buffer Size",
                "Load Balance Hosts",
                "Host Recheck Seconds",
                "Target Session Attributes"
            };
        private static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> BoundedPostgreSqlAdditionalIntegerOptions =
            new Dictionary<string, (int Minimum, int Maximum)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Minimum Pool Size"] = (0, 200),
                ["Maximum Pool Size"] = (5, 200),
                ["Connection Idle Lifetime"] = (0, 3600),
                ["Timeout"] = (1, 120),
                ["Command Timeout"] = (1, 600)
            };

        public static string BuildConnectionString(string path)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = PrepareSqliteDataSource(path),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = true,
                DefaultTimeout = 10,
                ForeignKeys = true,
            };

            return builder.ToString();
        }

        private static string PrepareSqliteDataSource(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!OperatingSystem.IsWindows() || !Path.IsPathRooted(path))
                return path;

            string fullPath = Path.GetFullPath(path);
            bool alreadyExtended = fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                                   fullPath.StartsWith(@"\\.\", StringComparison.Ordinal);
            if (alreadyExtended || fullPath.Length <= 240)
                return fullPath;

            return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + fullPath[2..]
                : @"\\?\" + fullPath;
        }

        public static DatabaseConnectionSettings LoadDatabaseSettings(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            return LoadDatabaseSettingsFromPath(
                Path.Combine(pathProvider.ConfigRoot, "appsettings.json"),
                pathProvider);
        }

        public static DatabaseConnectionSettings LoadDatabaseSettings(
            IAppPathProvider pathProvider,
            string settingsPath)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("数据库设置文件路径不能为空。", nameof(settingsPath));
            }

            string resolved = Path.IsPathRooted(settingsPath)
                ? Path.GetFullPath(settingsPath)
                : Path.GetFullPath(Path.Combine(pathProvider.ConfigRoot, settingsPath));
            return LoadDatabaseSettingsFromPath(resolved, pathProvider);
        }

        private static DatabaseConnectionSettings LoadDatabaseSettingsFromPath(
            string resolvedSettingsPath,
            IAppPathProvider pathProvider)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resolvedSettingsPath);

            if (!File.Exists(resolvedSettingsPath))
            {
                return CreateDefaultDatabaseSettings();
            }

            try
            {
                var json = File.ReadAllText(resolvedSettingsPath);
                var appSettings = JsonSerializer.Deserialize<DatabaseSettingsFile>(json);
                var system = appSettings?.System;
                if (system == null)
                {
                    throw new ServiceValidationException("数据库设置文件缺少 System 节点。");
                }

                return new DatabaseConnectionSettings
                {
                    Provider = DatabaseModeHelper.NormalizeProvider(system.DatabaseProvider),
                    SqliteDatabaseFileName = NormalizeSqliteDatabaseFileName(system.SqliteDatabaseFileName),
                    PostgreSqlHost = NormalizePostgreSqlText(system.PostgreSqlHost),
                    PostgreSqlPort = NormalizePostgreSqlPort(system.PostgreSqlPort),
                    PostgreSqlDatabase = NormalizePostgreSqlText(system.PostgreSqlDatabase),
                    PostgreSqlUsername = NormalizePostgreSqlText(system.PostgreSqlUsername),
                    PostgreSqlPassword = PostgreSqlPasswordResolver.Resolve(system.PostgreSqlPassword, pathProvider),
                    PostgreSqlAdditionalOptions = NormalizePostgreSqlAdditionalOptions(system.PostgreSqlAdditionalOptions)
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InfrastructureServiceException(
                    $"数据库设置文件无法读取：{resolvedSettingsPath}",
                    ex);
            }
            catch (JsonException ex)
            {
                throw new ServiceValidationException(
                    $"数据库设置文件 JSON 格式无效：{resolvedSettingsPath}",
                    ex);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceValidationException(
                    $"数据库设置文件包含无效配置：{resolvedSettingsPath}",
                    ex);
            }
        }

        private static DatabaseConnectionSettings CreateDefaultDatabaseSettings()
        {
            return new DatabaseConnectionSettings();
        }

        public static void ConfigureDbContextOptions(
            DbContextOptionsBuilder options,
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(pathProvider);

            var validationMessage = DatabaseModeHelper.Validate(databaseSettings);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                throw new ServiceValidationException(validationMessage);
            }

            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                options.UseNpgsql(BuildPostgreSqlConnectionString(databaseSettings), sql => sql.EnableRetryOnFailure());
                return;
            }

            var dbPath = ResolveRuntimeSqliteDatabasePath(pathProvider, databaseSettings.SqliteDatabaseFileName);
            var connectionString = BuildConnectionString(dbPath);

            options.UseSqlite(connectionString);
        }

        public static AppDbContext CreateDbContext(
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);

            var options = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureDbContextOptions(options, databaseSettings, pathProvider);
            return new AppDbContext(options.Options);
        }

        public static string NormalizeSqliteDatabaseFileName(string sqliteDatabaseFileName)
        {
            return string.IsNullOrWhiteSpace(sqliteDatabaseFileName)
                ? DatabaseConnectionSettings.DefaultSqliteDatabaseFileName
                : sqliteDatabaseFileName.Trim();
        }

        public static string NormalizeRuntimeSqliteDatabaseFileName(string sqliteDatabaseFileName)
        {
            string normalized = NormalizeSqliteDatabaseFileName(sqliteDatabaseFileName).Normalize();
            if (Path.IsPathRooted(normalized) ||
                !string.Equals(normalized, Path.GetFileName(normalized), StringComparison.Ordinal) ||
                !CrossPlatformFileNamePolicy.IsSafeFileName(normalized) ||
                normalized.EndsWith(' ') ||
                normalized.EndsWith('.'))
            {
                throw new ArgumentException(
                    "SQLite 只能填写运行数据根 Database 目录内的文件名，不能填写绝对路径、上级目录或子目录。",
                    nameof(sqliteDatabaseFileName));
            }

            if (CrossPlatformFileNamePolicy.IsReservedDeviceName(normalized))
            {
                throw new ArgumentException(
                    "SQLite 文件名不能使用 Windows 保留设备名。",
                    nameof(sqliteDatabaseFileName));
            }

            string extension = Path.GetExtension(normalized).ToLowerInvariant();
            if (extension is not (".db" or ".sqlite" or ".sqlite3"))
            {
                throw new ArgumentException(
                    "SQLite 文件名必须以 .db、.sqlite 或 .sqlite3 结尾。",
                    nameof(sqliteDatabaseFileName));
            }

            return normalized;
        }

        public static string ResolveRuntimeSqliteDatabasePath(
            IAppPathProvider pathProvider,
            string sqliteDatabaseFileName)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string normalized = NormalizeRuntimeSqliteDatabaseFileName(sqliteDatabaseFileName);
            string databasePath = Path.GetFullPath(Path.Combine(pathProvider.DatabaseRoot, normalized));

            if (!PathBoundaryHelper.IsWithinRoot(databasePath, pathProvider.DatabaseRoot))
            {
                throw new ServiceValidationException(
                    $"SQLite 数据库必须位于运行数据根 Database 目录下: {databasePath}");
            }

            return databasePath;
        }

        public static string BuildPostgreSqlConnectionString(DatabaseConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = NormalizePostgreSqlText(settings.PostgreSqlHost),
                Port = NormalizePostgreSqlPort(settings.PostgreSqlPort),
                Database = NormalizePostgreSqlText(settings.PostgreSqlDatabase),
                Username = NormalizePostgreSqlText(settings.PostgreSqlUsername),
                Password = settings.PostgreSqlPassword ?? string.Empty,
                Pooling = true,
                MinPoolSize = 2,
                MaxPoolSize = ReadEnvironmentInt(PostgreSqlMaximumPoolSizeEnvironmentVariable, 30, 5, 200),
                ConnectionIdleLifetime = 300,
                Timeout = 10,
                CommandTimeout = 30,
                ApplicationName = "ExportDocManager"
            };

            string additionalOptions = NormalizePostgreSqlAdditionalOptions(settings.PostgreSqlAdditionalOptions);
            if (!string.IsNullOrWhiteSpace(additionalOptions))
            {
                var additionalBuilder = new NpgsqlConnectionStringBuilder(additionalOptions);
                foreach (string key in additionalBuilder.Keys)
                {
                    if (!AllowedPostgreSqlAdditionalOptionNames.Contains(key))
                    {
                        throw new ServiceValidationException(
                            $"PostgreSQL 附加参数不支持字段：{key}。仅允许 TLS、超时、连接池和保活参数。");
                    }

                    builder[key] = additionalBuilder[key];
                }
            }

            // Keep operator-tunable pool and timeout values inside safe bounds even
            // when they come from the optional connection parameter string.
            builder.Pooling = true;
            builder.MinPoolSize = Math.Clamp(builder.MinPoolSize, 0, 200);
            builder.MaxPoolSize = Math.Clamp(builder.MaxPoolSize, 5, 200);
            builder.MinPoolSize = Math.Min(builder.MinPoolSize, builder.MaxPoolSize);
            builder.ConnectionIdleLifetime = Math.Clamp(builder.ConnectionIdleLifetime, 0, 3600);
            builder.Timeout = Math.Clamp(builder.Timeout, 1, 120);
            builder.CommandTimeout = Math.Clamp(builder.CommandTimeout, 1, 600);

            return builder.ConnectionString;
        }

        public static int NormalizePostgreSqlPort(int postgreSqlPort)
        {
            return postgreSqlPort <= 0
                ? DatabaseConnectionSettings.DefaultPostgreSqlPort
                : postgreSqlPort;
        }

        public static string NormalizePostgreSqlText(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static int ReadEnvironmentInt(string variableName, int fallback, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed)
                ? Math.Clamp(parsed, minimum, maximum)
                : fallback;
        }

        public static string NormalizePostgreSqlAdditionalOptions(string value)
        {
            string normalized = NormalizePostgreSqlText(value).Trim(';');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            try
            {
                var rawBuilder = new DbConnectionStringBuilder
                {
                    ConnectionString = normalized
                };

                // Validate every key before handing any value to Npgsql. This keeps
                // identity replacement errors deterministic even when another value
                // is outside Npgsql's own range (for example Timeout > 1024).
                foreach (string key in rawBuilder.Keys)
                {
                    if (!AllowedPostgreSqlAdditionalOptionNames.Contains(key))
                    {
                        throw new ServiceValidationException(
                            $"PostgreSQL 附加参数不支持字段：{key}。仅允许 TLS、超时、连接池和保活参数。");
                    }
                }

                var builder = new NpgsqlConnectionStringBuilder();
                foreach (string key in rawBuilder.Keys)
                {
                    object optionValue = NormalizePostgreSqlAdditionalOptionValue(key, rawBuilder[key]);
                    builder[key] = optionValue;
                }

                return builder.ConnectionString.Trim(';');
            }
            catch (ServiceValidationException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                throw new ServiceValidationException(
                    $"PostgreSQL 附加参数格式无效：{ex.Message}", ex);
            }
        }

        private static object NormalizePostgreSqlAdditionalOptionValue(string key, object value)
        {
            if (!BoundedPostgreSqlAdditionalIntegerOptions.TryGetValue(key, out var bounds))
            {
                return value;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new ServiceValidationException(
                    $"PostgreSQL 附加参数 {key} 必须是整数。");
            }

            return Math.Clamp(parsed, bounds.Minimum, bounds.Maximum);
        }

        private sealed class DatabaseSettingsFile
        {
            public DatabaseSystemSettings System { get; set; } = new();
        }

        private sealed class DatabaseSystemSettings
        {
            public string DatabaseProvider { get; set; } = DatabaseConnectionSettings.SqliteProvider;

            public string SqliteDatabaseFileName { get; set; } = DatabaseConnectionSettings.DefaultSqliteDatabaseFileName;

            public string PostgreSqlHost { get; set; } = string.Empty;

            public int PostgreSqlPort { get; set; } = DatabaseConnectionSettings.DefaultPostgreSqlPort;

            public string PostgreSqlDatabase { get; set; } = string.Empty;

            public string PostgreSqlUsername { get; set; } = string.Empty;

            public string PostgreSqlPassword { get; set; } = string.Empty;

            public string PostgreSqlAdditionalOptions { get; set; } = string.Empty;
        }
    }
}
