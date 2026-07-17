using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

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

                var settingsPath = Path.Combine(appRoot, "appsettings.json");
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
