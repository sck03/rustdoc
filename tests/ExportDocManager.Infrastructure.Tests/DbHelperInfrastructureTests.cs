using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    [Collection(LocalSecretProtectionCollection.Name)]
    public class DbHelperInfrastructureTests
    {
        [Fact]
        public void GetDatabasePath_WithConfiguredPathProvider_ShouldUseRuntimeDatabaseRoot()
        {
            var appRoot = CreateTempDirectory();
            var dataRoot = CreateTempDirectory();
            var previousProvider = new RuntimeAppPathProvider();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot, dataRoot);
                DbHelper.ConfigurePathProvider(provider);

                var path = DbHelper.GetDatabasePath(Path.Combine("tenant-a", "exportdoc.db"));

                Assert.Equal(
                    Path.Combine(provider.DatabaseRoot, "tenant-a", "exportdoc.db"),
                    path);
                Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
            }
            finally
            {
                DbHelper.ConfigurePathProvider(previousProvider);
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public void LoadDatabaseSettings_ShouldReadOnlyDatabaseFieldsFromSettingsJson()
        {
            var appRoot = CreateTempDirectory();
            var previousProvider = new RuntimeAppPathProvider();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot);
                DbHelper.ConfigurePathProvider(provider);

                var settingsPath = Path.Combine(provider.ConfigRoot, "appsettings.json");
                var json = JsonSerializer.Serialize(new
                {
                    System = new
                    {
                        DatabaseProvider = " PostgreSQL ",
                        SqliteDatabaseFileName = " custom.db ",
                        PostgreSqlHost = " 10.0.0.8 ",
                        PostgreSqlPort = 0,
                        PostgreSqlDatabase = " exportdoc ",
                        PostgreSqlUsername = " shared_user ",
                        PostgreSqlPassword = SecurityHelper.Encrypt("secret"),
                        PostgreSqlAdditionalOptions = " SSL Mode=Prefer; "
                    },
                    OtherSection = new { Value = "ignored" }
                });
                File.WriteAllText(settingsPath, json);

                var settings = DbHelper.LoadDatabaseSettings();

                Assert.Equal(DatabaseConnectionSettings.PostgreSqlProvider, settings.Provider);
                Assert.Equal("custom.db", settings.SqliteDatabaseFileName);
                Assert.Equal("10.0.0.8", settings.PostgreSqlHost);
                Assert.Equal(DatabaseConnectionSettings.DefaultPostgreSqlPort, settings.PostgreSqlPort);
                Assert.Equal("exportdoc", settings.PostgreSqlDatabase);
                Assert.Equal("shared_user", settings.PostgreSqlUsername);
                Assert.Equal("secret", settings.PostgreSqlPassword);
                Assert.Equal("SSL Mode=Prefer", settings.PostgreSqlAdditionalOptions);
            }
            finally
            {
                DbHelper.ConfigurePathProvider(previousProvider);
                DeleteDirectory(appRoot);
            }
        }

        [Fact]
        public void LoadDatabaseSettings_ShouldPreferRuntimePasswordEnvironmentVariable()
        {
            var appRoot = CreateTempDirectory();
            var previousProvider = new RuntimeAppPathProvider();
            string? previousPassword = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable);
            string? previousFile = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable);
            try
            {
                var provider = new RuntimeAppPathProvider(appRoot);
                DbHelper.ConfigurePathProvider(provider);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, "environment-secret");
                File.WriteAllText(
                    Path.Combine(provider.ConfigRoot, "appsettings.json"),
                    """
                    {
                      "System": {
                        "DatabaseProvider": "PostgreSQL",
                        "PostgreSqlHost": "127.0.0.1",
                        "PostgreSqlDatabase": "exportdoc",
                        "PostgreSqlUsername": "exportdoc",
                        "PostgreSqlPassword": ""
                      }
                    }
                    """);

                Assert.Equal("environment-secret", DbHelper.LoadDatabaseSettings().PostgreSqlPassword);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, previousPassword);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, previousFile);
                DbHelper.ConfigurePathProvider(previousProvider);
                DeleteDirectory(appRoot);
            }
        }

        [Fact]
        public void LoadDatabaseSettings_ShouldReadRelativePasswordFileFromSecurityRoot()
        {
            var appRoot = CreateTempDirectory();
            var previousProvider = new RuntimeAppPathProvider();
            string? previousPassword = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable);
            string? previousFile = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable);
            try
            {
                var provider = new RuntimeAppPathProvider(appRoot);
                DbHelper.ConfigurePathProvider(provider);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, "lower-priority-secret");
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, "postgres.password");
                File.WriteAllText(Path.Combine(provider.SecurityRoot, "postgres.password"), "file-secret\r\n");
                File.WriteAllText(
                    Path.Combine(provider.ConfigRoot, "appsettings.json"),
                    """
                    { "System": { "DatabaseProvider": "PostgreSQL", "PostgreSqlPassword": "" } }
                    """);

                Assert.Equal("file-secret", DbHelper.LoadDatabaseSettings().PostgreSqlPassword);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, previousPassword);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, previousFile);
                DbHelper.ConfigurePathProvider(previousProvider);
                DeleteDirectory(appRoot);
            }
        }

        [Fact]
        public void LoadDatabaseSettings_ShouldRejectPlaintextConfiguredPassword()
        {
            var appRoot = CreateTempDirectory();
            var previousProvider = new RuntimeAppPathProvider();
            string? previousPassword = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable);
            string? previousFile = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable);
            try
            {
                var provider = new RuntimeAppPathProvider(appRoot);
                DbHelper.ConfigurePathProvider(provider);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, null);
                File.WriteAllText(
                    Path.Combine(provider.ConfigRoot, "appsettings.json"),
                    """
                    { "System": { "DatabaseProvider": "PostgreSQL", "PostgreSqlPassword": "plain-secret" } }
                    """);

                var error = Assert.Throws<ServiceValidationException>(() => DbHelper.LoadDatabaseSettings());
                Assert.Contains("不能以明文", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordEnvironmentVariable, previousPassword);
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlPasswordFileEnvironmentVariable, previousFile);
                DbHelper.ConfigurePathProvider(previousProvider);
                DeleteDirectory(appRoot);
            }
        }

        [Theory]
        [InlineData("data.db", "data.db")]
        [InlineData(" team.sqlite ", "team.sqlite")]
        [InlineData("tenant.sqlite3", "tenant.sqlite3")]
        public void NormalizeRuntimeSqliteDatabaseFileName_ShouldAcceptSimpleDatabaseFileNames(
            string value,
            string expected)
        {
            Assert.Equal(expected, DbHelper.NormalizeRuntimeSqliteDatabaseFileName(value));
        }

        [Theory]
        [InlineData("..\\outside.db")]
        [InlineData("tenant/data.db")]
        [InlineData("C:\\data.db")]
        [InlineData("data:archive.db")]
        [InlineData("data\\archive.db")]
        [InlineData("data\narchive.db")]
        [InlineData("CON.db")]
        [InlineData("com1.sqlite")]
        [InlineData("LPT9.sqlite3")]
        [InlineData("data.txt")]
        [InlineData("data.db.")]
        public void NormalizeRuntimeSqliteDatabaseFileName_ShouldRejectPathsAndUnsupportedExtensions(string value)
        {
            Assert.Throws<ArgumentException>(() => DbHelper.NormalizeRuntimeSqliteDatabaseFileName(value));
        }

        [Fact]
        public void ResolveRuntimeSqliteDatabasePath_ShouldRejectOutsidePathWithoutCreatingItsDirectory()
        {
            var root = CreateTempDirectory();
            var outsideRoot = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}");
            var outsidePath = Path.Combine(outsideRoot, "outside.db");
            try
            {
                var provider = new RuntimeAppPathProvider(Path.Combine(root, "app"), Path.Combine(root, "data"));

                var error = Assert.Throws<ServiceValidationException>(() =>
                    DbHelper.ResolveRuntimeSqliteDatabasePath(provider, outsidePath));

                Assert.Contains("运行数据根 Database", error.Message, StringComparison.Ordinal);
                Assert.False(Directory.Exists(outsideRoot));
            }
            finally
            {
                DeleteDirectory(root);
                DeleteDirectory(outsideRoot);
            }
        }

        [Fact]
        public void ConfigureDbContextOptions_WithExplicitPathProvider_ShouldNotDependOnGlobalProvider()
        {
            var root = CreateTempDirectory();
            try
            {
                var provider = new RuntimeAppPathProvider(Path.Combine(root, "app"), Path.Combine(root, "data"));
                var databasePath = Path.Combine(provider.DatabaseRoot, "isolated.db");
                var settings = new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.SqliteProvider,
                    SqliteDatabaseFileName = databasePath,
                };
                var options = new DbContextOptionsBuilder<AppDbContext>();

                DbHelper.ConfigureDbContextOptions(options, settings, provider);

                using var context = new AppDbContext(options.Options);
                Assert.Equal(Path.GetFullPath(databasePath), Path.GetFullPath(context.Database.GetDbConnection().DataSource));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, "App_Data", "Config"));
            Directory.CreateDirectory(Path.Combine(path, "App_Data", "Security"));
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
