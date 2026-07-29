using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
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

                var error = Assert.Throws<InvalidOperationException>(() =>
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
