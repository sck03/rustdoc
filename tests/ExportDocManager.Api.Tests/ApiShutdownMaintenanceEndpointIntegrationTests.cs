using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiShutdownMaintenanceEndpointIntegrationTests
    {
        [Fact]
        public async Task ShutdownMaintenanceEndpoint_ShouldRunWithDesktopTokenOnly()
        {
            const string desktopToken = "desktop-secret";
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-shutdown-maintenance",
                "api-shutdown-maintenance.db",
                desktopToken);

            using var noDesktopTokenClient = harness.CreateClient();
            var forbiddenResponse = await noDesktopTokenClient.PostAsync(
                "/api/system/shutdown-maintenance",
                content: null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

            using var desktopClient = harness.CreateClient(desktopAccessToken: desktopToken);
            var bearerOnlyEndpointResponse = await desktopClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, bearerOnlyEndpointResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(desktopClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken, desktopToken);
            await SaveMaintenanceSettingsAsync(adminClient);

            string logRoot = Path.Combine(harness.DataRoot, "Logs");
            Directory.CreateDirectory(logRoot);
            string oldLog = Path.Combine(logRoot, "shutdown-old.txt");
            await File.WriteAllTextAsync(oldLog, "old shutdown log");
            File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-3));

            var response = await desktopClient.PostAsync(
                "/api/system/shutdown-maintenance",
                content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await ApiIntegrationTestHarness.ReadJsonAsync<ApiShutdownMaintenanceResponse>(response);

            Assert.True(result.Success);
            Assert.Equal(1, result.DeletedTextLogs);
            Assert.Equal(Path.Combine(harness.DataRoot, "Backups"), result.BackupRoot);
            Assert.Equal(logRoot, result.LogRoot);
            Assert.Contains("运行数据根 Backups", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("运行数据根 Logs", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取发票/报关业务表", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销业务表", result.StoragePolicy, StringComparison.Ordinal);
            Assert.False(File.Exists(oldLog));
            Assert.NotEmpty(Directory.GetFiles(result.BackupRoot, "*.zip", SearchOption.TopDirectoryOnly));
        }

        [Fact]
        public async Task SystemLogCleanupEndpoint_ShouldUseSavedSettingsAndRuntimeDataLogs()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-system-log-cleanup",
                "api-system-log-cleanup.db");

            using var anonymousClient = harness.CreateClient();
            var unauthorizedResponse = await anonymousClient.PostAsync(
                "/api/system/logs/cleanup",
                content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);
            await SaveLogCleanupSettingsAsync(adminClient);

            string logRoot = Path.Combine(harness.DataRoot, "Logs");
            Directory.CreateDirectory(logRoot);
            string oldByAge = Path.Combine(logRoot, "manual-old-by-age.txt");
            string oldByCount = Path.Combine(logRoot, "manual-old-by-count.txt");
            string retained = Path.Combine(logRoot, "manual-retained.txt");
            await File.WriteAllTextAsync(oldByAge, "old by age");
            await File.WriteAllTextAsync(oldByCount, "old by count");
            await File.WriteAllTextAsync(retained, "retained");
            File.SetLastWriteTime(oldByAge, DateTime.Now.AddDays(-5));
            File.SetLastWriteTime(oldByCount, DateTime.Now.AddMinutes(-5));
            File.SetLastWriteTime(retained, DateTime.Now);

            var response = await adminClient.PostAsync(
                "/api/system/logs/cleanup",
                content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSystemLogCleanupResponse>(response);

            Assert.True(result.Success);
            Assert.Equal(0, result.DeletedAuditLogs);
            Assert.Equal(1, result.DeletedTextLogsByAge);
            Assert.Equal(1, result.DeletedTextLogsByCount);
            Assert.Equal(2, result.DeletedTextLogs);
            Assert.Equal(logRoot, result.LogRoot);
            Assert.Contains("运行数据根 Logs", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("运行数据根数据库 AuditLogs", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取发票/报关业务表", result.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销业务表", result.StoragePolicy, StringComparison.Ordinal);
            Assert.False(File.Exists(oldByAge));
            Assert.False(File.Exists(oldByCount));
            Assert.True(File.Exists(retained));
        }

        private static async Task SaveMaintenanceSettingsAsync(HttpClient adminClient)
        {
            var settingsResponse = await adminClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
            var settings = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(settingsResponse);
            AppSettings requestedSettings = settings.Settings;
            requestedSettings.System.BackupRetentionDays = 1;
            requestedSettings.System.AuditLogRetentionDays = 1;
            requestedSettings.System.LogRetentionDays = 1;
            requestedSettings.WebDav.Enabled = false;

            var saveResponse = await adminClient.PutAsJsonAsync("/api/settings", new
            {
                settings = requestedSettings,
                updateSecrets = false
            });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        }

        private static async Task SaveLogCleanupSettingsAsync(HttpClient adminClient)
        {
            var settingsResponse = await adminClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
            var settings = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(settingsResponse);
            AppSettings requestedSettings = settings.Settings;
            requestedSettings.System.BackupRetentionDays = 0;
            requestedSettings.System.AuditLogRetentionDays = 1;
            requestedSettings.System.LogRetentionDays = 1;
            requestedSettings.System.LogRetainedFileCount = 1;

            var saveResponse = await adminClient.PutAsJsonAsync("/api/settings", new
            {
                settings = requestedSettings,
                updateSecrets = false
            });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        }
    }
}
