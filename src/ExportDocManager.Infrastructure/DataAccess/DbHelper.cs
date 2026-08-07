using System;
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
        private static IAppPathProvider _pathProvider = new RuntimeAppPathProvider();

        public static void ConfigurePathProvider(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            SecurityHelper.ConfigurePathProvider(_pathProvider);
        }

        public static string BuildConnectionString(string path)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 10,
                ForeignKeys = true,
            };
            
            return builder.ToString();
        }

        public static DatabaseConnectionSettings LoadDatabaseSettings()
        {
            return LoadDatabaseSettingsFromPath(Path.Combine(_pathProvider.ConfigRoot, "appsettings.json"));
        }

        public static DatabaseConnectionSettings LoadDatabaseSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("数据库设置文件路径不能为空。", nameof(settingsPath));
            }

            return LoadDatabaseSettingsFromPath(ResolveFromConfigRoot(settingsPath));
        }

        private static DatabaseConnectionSettings LoadDatabaseSettingsFromPath(string resolvedSettingsPath)
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
                    PostgreSqlPassword = NormalizePostgreSqlPassword(system.PostgreSqlPassword),
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
            DatabaseConnectionSettings databaseSettings)
        {
            ConfigureDbContextOptions(options, databaseSettings, _pathProvider);
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

        public static AppDbContext CreateDbContext()
        {
            return CreateDbContext(LoadDatabaseSettings());
        }

        public static AppDbContext CreateDbContext(DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);

            var options = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureDbContextOptions(options, databaseSettings);
            return new AppDbContext(options.Options);
        }

        public static string GetDatabasePath(string dbFileName)
        {
            string normalizedFileName = string.IsNullOrWhiteSpace(dbFileName)
                ? DatabaseConnectionSettings.DefaultSqliteDatabaseFileName
                : dbFileName.Trim();

            if (Path.IsPathRooted(normalizedFileName))
            {
                TryEnsureDirectory(Path.GetDirectoryName(normalizedFileName));
                return normalizedFileName;
            }

            var pathSegments = normalizedFileName
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

            var databasePath = _pathProvider.DatabaseRoot;
            foreach (var segment in pathSegments)
            {
                databasePath = Path.Combine(databasePath, segment.Trim());
            }

            TryEnsureDirectory(Path.GetDirectoryName(databasePath));
            return databasePath;
        }

        public static string NormalizeSqliteDatabaseFileName(string sqliteDatabaseFileName)
        {
            return string.IsNullOrWhiteSpace(sqliteDatabaseFileName)
                ? DatabaseConnectionSettings.DefaultSqliteDatabaseFileName
                : sqliteDatabaseFileName.Trim();
        }

        public static string NormalizeRuntimeSqliteDatabaseFileName(string sqliteDatabaseFileName)
        {
            string normalized = NormalizeSqliteDatabaseFileName(sqliteDatabaseFileName);
            if (Path.IsPathRooted(normalized) ||
                !string.Equals(normalized, Path.GetFileName(normalized), StringComparison.Ordinal) ||
                CrossPlatformFileNamePolicy.ContainsInvalidCharacters(normalized) ||
                normalized.EndsWith(' ') ||
                normalized.EndsWith('.'))
            {
                throw new ArgumentException(
                    "SQLite 只能填写运行数据根 Database 目录内的文件名，不能填写绝对路径、上级目录或子目录。",
                    nameof(sqliteDatabaseFileName));
            }

            string windowsBaseName = normalized.Split('.', 2)[0];
            if (windowsBaseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                windowsBaseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                windowsBaseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                windowsBaseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                IsWindowsNumberedDeviceName(windowsBaseName, "COM") ||
                IsWindowsNumberedDeviceName(windowsBaseName, "LPT"))
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

            string normalized = NormalizeSqliteDatabaseFileName(sqliteDatabaseFileName);
            string databasePath;
            if (Path.IsPathRooted(normalized))
            {
                _ = NormalizeRuntimeSqliteDatabaseFileName(Path.GetFileName(normalized));
                databasePath = Path.GetFullPath(normalized);
            }
            else
            {
                normalized = NormalizeRuntimeSqliteDatabaseFileName(normalized);
                databasePath = Path.GetFullPath(Path.Combine(pathProvider.DatabaseRoot, normalized));
            }

            if (!PathBoundaryHelper.IsWithinRoot(databasePath, pathProvider.DatabaseRoot))
            {
                throw new ServiceValidationException(
                    $"SQLite 数据库必须位于运行数据根 Database 目录下: {databasePath}");
            }

            return databasePath;
        }

        private static bool IsWindowsNumberedDeviceName(string value, string prefix)
        {
            return value.Length == prefix.Length + 1 &&
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                value[^1] is >= '1' and <= '9';
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

            string connectionString = builder.ConnectionString;
            string additionalOptions = NormalizePostgreSqlAdditionalOptions(settings.PostgreSqlAdditionalOptions);
            if (!string.IsNullOrWhiteSpace(additionalOptions))
            {
                connectionString = $"{connectionString};{additionalOptions}";
            }

            return connectionString;
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
            return NormalizePostgreSqlText(value).Trim(';');
        }

        public static string NormalizePostgreSqlPassword(string value)
        {
            return PostgreSqlPasswordResolver.Resolve(value, _pathProvider);
        }

        private static string ResolveFromConfigRoot(string path)
        {
            var trimmed = path.Trim();
            return Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.GetFullPath(Path.Combine(_pathProvider.ConfigRoot, trimmed));
        }

        private static bool TryEnsureDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(directoryPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class DatabaseSettingsFile
        {
            public DatabaseSystemSettings System { get; set; }
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
