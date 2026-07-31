using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SingleWindowDisasterRecoveryTests
    {
        private const string PackagePassword = "Recovery-test-2026!";

        [Fact]
        public async Task EncryptedPackage_ShouldRestoreSqliteIdentityKeyAndSettingsBeforeStartup()
        {
            string root = CreateTestRoot();
            try
            {
                string appRoot = Path.Combine(root, "app");
                string dataRoot = Path.Combine(root, "data");
                var paths = new RuntimeAppPathProvider(appRoot, dataRoot);
                DbHelper.ConfigurePathProvider(paths);
                var settings = new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.SqliteProvider,
                    SqliteDatabaseFileName = "holding-station.db"
                };
                string databasePath = DbHelper.ResolveRuntimeSqliteDatabasePath(paths, settings.SqliteDatabaseFileName);
                await WriteValueAsync(databasePath, "recover-me");

                string appSettingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
                string originalSettings = JsonSerializer.Serialize(new
                {
                    System = new
                    {
                        DatabaseProvider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = settings.SqliteDatabaseFileName
                    },
                    RecoveryProbe = "original"
                }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(appSettingsPath, originalSettings);
                byte[] originalKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
                string keyPath = Path.Combine(paths.SecurityRoot, LocalSecretProtector.MasterKeyFileName);
                await File.WriteAllBytesAsync(keyPath, originalKey);

                var stationService = new SingleWindowStationIdentityService(paths);
                string originalStation = await stationService.GetCurrentStationKeyAsync();
                string stationPath = Path.Combine(paths.SecurityRoot, "SingleWindow", "station.id");
                var service = new SingleWindowDisasterRecoveryService(settings, paths, stationService);

                var package = await service.CreatePackageAsync(PackagePassword);
                Assert.True(package.Success, package.Message);
                Assert.EndsWith(".edmrecovery", package.FileName, StringComparison.OrdinalIgnoreCase);
                byte[] packageBytes = await File.ReadAllBytesAsync(package.FilePath);
                Assert.StartsWith("EDMDRP01", Encoding.ASCII.GetString(packageBytes, 0, 8), StringComparison.Ordinal);
                Assert.DoesNotContain(originalStation, Encoding.UTF8.GetString(packageBytes), StringComparison.Ordinal);

                await WriteValueAsync(databasePath, "damaged");
                await File.WriteAllTextAsync(appSettingsPath, originalSettings.Replace("original", "damaged"));
                await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
                await File.WriteAllTextAsync(stationPath, $"SWS-{Guid.NewGuid():N}".ToUpperInvariant());

                var wrongPassword = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    service.ScheduleRestoreAsync(package.FilePath, "Wrong-password-2026!"));
                Assert.Contains("密码错误或包已损坏", wrongPassword.Message, StringComparison.Ordinal);
                Assert.False(SingleWindowDisasterRecoveryManager.HasPendingRestore(paths));

                var scheduled = await service.ScheduleRestoreAsync(package.FilePath, PackagePassword);
                Assert.True(scheduled.Success);
                Assert.True(scheduled.RestartRequired);
                Assert.True(SingleWindowDisasterRecoveryManager.HasPendingRestore(paths));

                SqliteConnection.ClearAllPools();
                SingleWindowDisasterRecoveryManager.ApplyPendingRestore(paths);

                Assert.Equal("recover-me", await ReadValueAsync(databasePath));
                Assert.Equal(originalSettings, await File.ReadAllTextAsync(appSettingsPath));
                Assert.Equal(originalKey, await File.ReadAllBytesAsync(keyPath));
                Assert.Equal(originalStation, (await File.ReadAllTextAsync(stationPath)).Trim());
                Assert.False(SingleWindowDisasterRecoveryManager.HasPendingRestore(paths));
                Assert.True(RecoveryLicenseReactivationMarker.Exists(paths));
                Assert.True(Directory.Exists(scheduled.SafetyBackupRoot));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                DbHelper.ConfigurePathProvider(new RuntimeAppPathProvider());
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("short")]
        [InlineData("lowercase-only-2026")]
        public void PackagePasswordPolicy_ShouldRejectWeakPasswords(string password)
        {
            Assert.Throws<ArgumentException>(() => DisasterRecoveryPackageCrypto.ValidatePassword(password));
        }

        private static async Task WriteValueAsync(string databasePath, string value)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(DbHelper.BuildConnectionString(databasePath));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS RecoveryProbe (Value TEXT NOT NULL); DELETE FROM RecoveryProbe; INSERT INTO RecoveryProbe (Value) VALUES ($value);";
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string> ReadValueAsync(string databasePath)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(DbHelper.BuildConnectionString(databasePath));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM RecoveryProbe LIMIT 1;";
            return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        }

        private static string CreateTestRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "edm-disaster-recovery-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
