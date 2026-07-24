using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Npgsql;

namespace ExportDocManager.DataAccess
{
    public static class DbHelper
    {
        public const string PostgreSqlMaximumPoolSizeEnvironmentVariable = "EXPORTDOCMANAGER_DB_MAX_POOL_SIZE";
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
            return LoadDatabaseSettingsFromPath(Path.Combine(_pathProvider.AppRoot, "appsettings.json"));
        }

        public static DatabaseConnectionSettings LoadDatabaseSettings(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("数据库设置文件路径不能为空。", nameof(settingsPath));
            }

            return LoadDatabaseSettingsFromPath(ResolveFromAppRoot(settingsPath));
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
                    throw new InvalidOperationException("数据库设置文件缺少 System 节点。");
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"数据库设置文件无法读取或格式无效: {resolvedSettingsPath}",
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
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(databaseSettings);

            var validationMessage = DatabaseModeHelper.Validate(databaseSettings);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                options.UseNpgsql(BuildPostgreSqlConnectionString(databaseSettings), sql => sql.EnableRetryOnFailure());
                return;
            }

            var sqliteFileName = NormalizeSqliteDatabaseFileName(databaseSettings.SqliteDatabaseFileName);
            var dbPath = GetDatabasePath(sqliteFileName);
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
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var decrypted = SecurityHelper.Decrypt(value);
            return decrypted ?? value;
        }

        private static string ResolveFromAppRoot(string path)
        {
            var trimmed = path.Trim();
            return Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.GetFullPath(Path.Combine(_pathProvider.AppRoot, trimmed));
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
