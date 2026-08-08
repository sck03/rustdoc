using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiBackupEndpointIntegrationTests
    {
        [Fact]
        public async Task BackupEndpoints_ShouldManageRuntimeDataRootBackupsForAdminsOnly()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-backup",
                "api-backup.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousResponse = await anonymousClient.GetAsync("/api/backup");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "backup-operator",
                fullName = "Backup Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "backup-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var forbiddenResponse = await operatorClient.GetAsync("/api/backup");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            var forbiddenCloudStatusResponse = await operatorClient.GetAsync("/api/backup/cloud/status");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenCloudStatusResponse.StatusCode);

            var initialListResponse = await adminClient.GetAsync("/api/backup");
            Assert.Equal(HttpStatusCode.OK, initialListResponse.StatusCode);
            var initialList = await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupListResponse>(initialListResponse);
            string expectedBackupRoot = Path.Combine(harness.DataRoot, "Backups");
            Assert.Equal(string.Empty, initialList.BackupRoot);
            Assert.Contains("运行数据根 Backups", initialList.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取发票/付款业务表", initialList.StoragePolicy, StringComparison.Ordinal);
            Assert.Empty(initialList.Backups);

            var createBackupResponse = await adminClient.PostAsync("/api/backup", content: null);
            Assert.Equal(HttpStatusCode.OK, createBackupResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupCreateResponse>(createBackupResponse);

            var backup = Assert.Single(created.Backups);
            Assert.True(created.Success);
            Assert.EndsWith(".zip", backup.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, backup.FullPath);
            string physicalBackupPath = Path.Combine(expectedBackupRoot, backup.FileName);
            Assert.True(File.Exists(physicalBackupPath));
            Assert.True(backup.SizeBytes > 0);

            var unsafeRestoreResponse = await adminClient.PostAsJsonAsync("/api/backup/restore", new
            {
                backupFileName = "..\\api-backup.db",
                confirmationText = "RESTORE"
            });
            Assert.Equal(HttpStatusCode.BadRequest, unsafeRestoreResponse.StatusCode);

            var unconfirmedRestoreResponse = await adminClient.PostAsJsonAsync("/api/backup/restore", new
            {
                backupFileName = backup.FileName,
                confirmationText = "restore"
            });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedRestoreResponse.StatusCode);

            File.SetLastWriteTimeUtc(physicalBackupPath, DateTime.UtcNow.AddDays(-10));
            var cleanupResponse = await adminClient.PostAsJsonAsync("/api/backup/cleanup", new
            {
                daysToKeep = 1
            });
            Assert.Equal(HttpStatusCode.OK, cleanupResponse.StatusCode);
            var cleanup = await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupCreateResponse>(cleanupResponse);
            Assert.True(cleanup.Success);
            Assert.Empty(cleanup.Backups);
            Assert.False(File.Exists(physicalBackupPath));
        }

        [Fact]
        public async Task BackupEndpoints_ShouldRevealServerPathsOnlyToTrustedDesktopClient()
        {
            const string desktopToken = "backup-desktop-token-2026";
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-backup-desktop-paths",
                "api-backup-desktop.db",
                desktopAccessToken: desktopToken);
            using var anonymousClient = harness.CreateClient(desktopAccessToken: desktopToken);
            ApiLoginResponse adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken, desktopToken);

            HttpResponseMessage createResponse = await adminClient.PostAsync("/api/backup", content: null);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            ApiBackupCreateResponse created =
                await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupCreateResponse>(createResponse);
            ApiBackupItemDto backup = Assert.Single(created.Backups);

            string expectedRoot = Path.Combine(harness.DataRoot, "Backups");
            Assert.Equal(expectedRoot, created.BackupRoot);
            Assert.Equal(Path.Combine(expectedRoot, backup.FileName), backup.FullPath);
            Assert.True(File.Exists(backup.FullPath));
        }

        [Fact]
        public async Task BackupRestore_ShouldRecoverSnapshotAfterApiRestart()
        {
            ApiIntegrationTestHarness harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-backup-restore",
                "api-backup-restore.db");
            ApiIntegrationTestHarness restartedHarness = null;

            try
            {
                using var anonymousClient = harness.CreateClient();
                var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
                using var adminClient = harness.CreateClient(adminLogin.AccessToken);

                var createBackupResponse = await adminClient.PostAsync("/api/backup", content: null);
                Assert.Equal(HttpStatusCode.OK, createBackupResponse.StatusCode);
                var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupCreateResponse>(createBackupResponse);
                var backup = Assert.Single(created.Backups);
                string physicalBackupPath = Path.Combine(harness.DataRoot, "Backups", backup.FileName);
                Assert.Equal(string.Empty, backup.FullPath);
                Assert.True(File.Exists(physicalBackupPath));

                const string transientUsername = "restore-transient";
                var createTransientUserResponse = await adminClient.PostAsJsonAsync("/api/users", new
                {
                    username = transientUsername,
                    fullName = "Restore Transient User",
                    role = UserRoleCatalog.User,
                    departmentId = string.Empty,
                    companyScope = string.Empty,
                    isActive = true,
                    resetPassword = "restore-pass"
                });
                Assert.Equal(HttpStatusCode.OK, createTransientUserResponse.StatusCode);

                var usersBeforeRestore = await GetUsersAsync(adminClient);
                Assert.Contains(usersBeforeRestore.Users, user => user.Username == transientUsername);

                var restoreResponse = await adminClient.PostAsJsonAsync("/api/backup/restore", new
                {
                    backupFileName = backup.FileName,
                    confirmationText = "RESTORE"
                });
                Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
                var restoreResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(restoreResponse);
                Assert.True(restoreResult.Success);
                Assert.Contains("重启", restoreResult.Message, StringComparison.Ordinal);

                string appRoot = harness.AppRoot;
                string dataRoot = harness.DataRoot;
                string databaseFileName = Path.GetFileName(harness.DatabasePath);
                await harness.StopAppAsync();

                restartedHarness = await ApiIntegrationTestHarness.StartWithExistingRootsAsync(
                    appRoot,
                    dataRoot,
                    databaseFileName);
                using var restartedAnonymousClient = restartedHarness.CreateClient();
                var restartedAdminLogin = await restartedHarness.LoginAsync(
                    restartedAnonymousClient,
                    "admin",
                    string.Empty);
                using var restartedAdminClient = restartedHarness.CreateClient(restartedAdminLogin.AccessToken);

                var usersAfterRestart = await GetUsersAsync(restartedAdminClient);
                Assert.Contains(usersAfterRestart.Users, user => user.Username == "admin");
                Assert.DoesNotContain(usersAfterRestart.Users, user => user.Username == transientUsername);

                var backupListAfterRestart = await GetBackupsAsync(restartedAdminClient);
                Assert.Equal(string.Empty, backupListAfterRestart.BackupRoot);
                Assert.Contains(backupListAfterRestart.Backups, item => item.FileName == backup.FileName);
            }
            finally
            {
                if (restartedHarness != null)
                {
                    await restartedHarness.DisposeAsync();
                }

                await harness.DisposeAsync();
            }
        }

        [Fact]
        public async Task DisasterRecoveryEndpoints_ShouldRequireAdministratorAndTrustedDesktopAccess()
        {
            const string desktopToken = "recovery-desktop-token-2026";
            var recoveryService = new RecordingDisasterRecoveryService();
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-disaster-recovery",
                "api-disaster-recovery.db",
                desktopAccessToken: desktopToken,
                configureServices: services =>
                    services.AddSingleton<ISingleWindowDisasterRecoveryService>(recoveryService));

            using var untrustedAnonymousClient = harness.CreateClient();
            var missingDesktopTokenResponse = await untrustedAnonymousClient.GetAsync(
                "/api/backup/disaster-recovery/status");
            Assert.Equal(HttpStatusCode.Forbidden, missingDesktopTokenResponse.StatusCode);

            using var trustedAnonymousClient = harness.CreateClient(desktopAccessToken: desktopToken);
            var anonymousStatusResponse = await trustedAnonymousClient.GetAsync(
                "/api/backup/disaster-recovery/status");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatusResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(trustedAnonymousClient, "admin", string.Empty);
            using var trustedAdminClient = harness.CreateClient(adminLogin.AccessToken, desktopToken);
            using var untrustedAdminClient = harness.CreateClient(adminLogin.AccessToken);

            var createOperatorResponse = await trustedAdminClient.PostAsJsonAsync("/api/users", new
            {
                username = "recovery-operator",
                fullName = "Recovery Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "recovery-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(
                trustedAnonymousClient,
                "recovery-operator",
                "recovery-pass");
            using var trustedOperatorClient = harness.CreateClient(operatorLogin.AccessToken, desktopToken);

            var operatorStatusResponse = await trustedOperatorClient.GetAsync(
                "/api/backup/disaster-recovery/status");
            Assert.Equal(HttpStatusCode.Forbidden, operatorStatusResponse.StatusCode);

            var adminStatusResponse = await trustedAdminClient.GetAsync(
                "/api/backup/disaster-recovery/status");
            Assert.Equal(HttpStatusCode.OK, adminStatusResponse.StatusCode);

            var untrustedCreateResponse = await untrustedAdminClient.PostAsJsonAsync(
                "/api/backup/disaster-recovery/create",
                new { password = "strong-recovery-password" });
            Assert.Equal(HttpStatusCode.Forbidden, untrustedCreateResponse.StatusCode);

            var operatorCreateResponse = await trustedOperatorClient.PostAsJsonAsync(
                "/api/backup/disaster-recovery/create",
                new { password = "strong-recovery-password" });
            Assert.Equal(HttpStatusCode.Forbidden, operatorCreateResponse.StatusCode);

            var createResponse = await trustedAdminClient.PostAsJsonAsync(
                "/api/backup/disaster-recovery/create",
                new { password = "strong-recovery-password" });
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            Assert.Equal(1, recoveryService.CreateCalls);

            var unconfirmedRestoreResponse = await trustedAdminClient.PostAsJsonAsync(
                "/api/backup/disaster-recovery/restore",
                new
                {
                    packagePath = "E:\\Recovery\\holding-station.edmrecovery",
                    password = "strong-recovery-password",
                    confirmationText = "recover"
                });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedRestoreResponse.StatusCode);

            var restoreResponse = await trustedAdminClient.PostAsJsonAsync(
                "/api/backup/disaster-recovery/restore",
                new
                {
                    packagePath = "E:\\Recovery\\holding-station.edmrecovery",
                    password = "strong-recovery-password",
                    confirmationText = "RECOVER"
                });
            Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
            Assert.Equal(1, recoveryService.RestoreCalls);
        }

        [Fact]
        public async Task CloudBackupEndpoints_ShouldUploadLatestRuntimeBackupToSavedWebDavEndpoint()
        {
            await using var webDavServer = await FakeWebDavServer.StartAsync();
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-cloud-backup",
                "api-cloud-backup.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousStatusResponse = await anonymousClient.GetAsync("/api/backup/cloud/status");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatusResponse.StatusCode);
            var anonymousListResponse = await anonymousClient.GetAsync("/api/backup/cloud/backups");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);
            var anonymousUploadResponse = await anonymousClient.PostAsync("/api/backup/cloud/upload-latest", content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousUploadResponse.StatusCode);
            var anonymousDownloadResponse = await anonymousClient.PostAsJsonAsync("/api/backup/cloud/download", new
            {
                remoteFileName = "backup.zip"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDownloadResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var initialStatusResponse = await adminClient.GetAsync("/api/backup/cloud/status");
            Assert.Equal(HttpStatusCode.OK, initialStatusResponse.StatusCode);
            var initialStatus = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupStatusResponse>(initialStatusResponse);
            Assert.False(initialStatus.Enabled);
            Assert.False(initialStatus.IsConfigured);
            Assert.Contains("运行数据根 Backups", initialStatus.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销业务表", initialStatus.StoragePolicy, StringComparison.Ordinal);

            var uploadBeforeConfigResponse = await adminClient.PostAsync("/api/backup/cloud/upload-latest", content: null);
            Assert.Equal(HttpStatusCode.BadRequest, uploadBeforeConfigResponse.StatusCode);
            var listBeforeConfigResponse = await adminClient.GetAsync("/api/backup/cloud/backups");
            Assert.Equal(HttpStatusCode.BadRequest, listBeforeConfigResponse.StatusCode);

            var settingsResponse = await adminClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
            var settings = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(settingsResponse);
            var requestedSettings = CloneSettings(settings.Settings);
            requestedSettings.WebDav.Url = webDavServer.BaseUrl;
            requestedSettings.WebDav.UserName = "webdav-user";
            requestedSettings.WebDav.Password = "webdav-password";
            requestedSettings.WebDav.Enabled = true;

            var saveResponse = await adminClient.PutAsJsonAsync("/api/settings", new
            {
                settings = requestedSettings,
                updateSecrets = true
            });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

            var createBackupResponse = await adminClient.PostAsync("/api/backup", content: null);
            Assert.Equal(HttpStatusCode.OK, createBackupResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupCreateResponse>(createBackupResponse);
            var backup = Assert.Single(created.Backups);
            string physicalBackupPath = Path.Combine(harness.DataRoot, "Backups", backup.FileName);
            Assert.Equal(string.Empty, backup.FullPath);
            Assert.True(File.Exists(physicalBackupPath));

            var configuredStatusResponse = await adminClient.GetAsync("/api/backup/cloud/status");
            Assert.Equal(HttpStatusCode.OK, configuredStatusResponse.StatusCode);
            var configuredStatus = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupStatusResponse>(configuredStatusResponse);
            Assert.True(configuredStatus.Enabled);
            Assert.True(configuredStatus.IsConfigured);
            Assert.Equal(webDavServer.BaseUrl, configuredStatus.Url);
            Assert.Equal(backup.FileName, configuredStatus.LatestBackupFileName);

            var testConnectionResponse = await adminClient.PostAsync("/api/backup/cloud/test-connection", content: null);
            Assert.Equal(HttpStatusCode.OK, testConnectionResponse.StatusCode);
            var testConnection = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupCommandResponse>(testConnectionResponse);
            Assert.True(testConnection.Success);
            Assert.Contains("测试成功", testConnection.Message, StringComparison.Ordinal);

            var uploadResponse = await adminClient.PostAsync("/api/backup/cloud/upload-latest", content: null);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
            var uploaded = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupCommandResponse>(uploadResponse);
            Assert.True(uploaded.Success);
            Assert.Equal(backup.FileName, uploaded.RemoteFileName);
            Assert.Equal(string.Empty, uploaded.LocalBackupPath);
            Assert.Equal(backup.SizeBytes, uploaded.SizeBytes);
            Assert.Equal(string.Empty, uploaded.BackupRoot);

            Assert.True(webDavServer.Uploads.TryGetValue(backup.FileName, out var remoteBytes));
            Assert.True(remoteBytes.Length > 0);
            Assert.Equal(backup.SizeBytes, remoteBytes.Length);

            var listResponse = await adminClient.GetAsync("/api/backup/cloud/backups");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var remoteList = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupListResponse>(listResponse);
            var remoteBackup = Assert.Single(remoteList.Backups);
            Assert.Equal(backup.FileName, remoteBackup.FileName);
            Assert.Equal(backup.SizeBytes, remoteBackup.SizeBytes);
            Assert.Equal(string.Empty, remoteList.BackupRoot);

            File.Delete(physicalBackupPath);
            Assert.False(File.Exists(physicalBackupPath));

            var invalidDownloadResponse = await adminClient.PostAsJsonAsync("/api/backup/cloud/download", new
            {
                remoteFileName = "..\\forbidden.zip"
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDownloadResponse.StatusCode);

            var downloadResponse = await adminClient.PostAsJsonAsync("/api/backup/cloud/download", new
            {
                remoteFileName = backup.FileName
            });
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            var downloaded = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCloudBackupCommandResponse>(downloadResponse);
            Assert.True(downloaded.Success);
            Assert.Equal(backup.FileName, downloaded.RemoteFileName);
            Assert.Equal(backup.SizeBytes, downloaded.SizeBytes);
            Assert.Equal(string.Empty, downloaded.LocalBackupPath);
            Assert.Equal(string.Empty, downloaded.BackupRoot);
            Assert.True(File.Exists(physicalBackupPath));
            Assert.Equal(remoteBytes, await File.ReadAllBytesAsync(physicalBackupPath));

            var localBackupsAfterDownload = await GetBackupsAsync(adminClient);
            Assert.Contains(localBackupsAfterDownload.Backups, item => item.FileName == backup.FileName);
        }

        private static async Task<ApiBackupListResponse> GetBackupsAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/backup");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<ApiBackupListResponse>(response);
        }

        private static async Task<ApiUserListResponse> GetUsersAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserListResponse>(response);
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            return ApiSettingsDtoFactory.PrepareForSave(
                settings,
                new AppSettings(),
                updateSecrets: true);
        }

        private sealed class RecordingDisasterRecoveryService : ISingleWindowDisasterRecoveryService
        {
            public int CreateCalls { get; private set; }

            public int RestoreCalls { get; private set; }

            public SingleWindowDisasterRecoveryStatus GetStatus() => new(
                Supported: true,
                UsesSqlite: true,
                PendingRestore: false,
                RecoveryRoot: "E:\\RuntimeData\\Backups\\DisasterRecovery",
                Message: "ready",
                StoragePolicy: "runtime-data-root-only");

            public Task<SingleWindowDisasterRecoveryPackageResult> CreatePackageAsync(
                string password,
                CancellationToken cancellationToken = default)
            {
                CreateCalls++;
                return Task.FromResult(new SingleWindowDisasterRecoveryPackageResult(
                    Success: true,
                    Message: "created",
                    FileName: "holding-station.edmrecovery",
                    FilePath: "E:\\RuntimeData\\Backups\\DisasterRecovery\\holding-station.edmrecovery",
                    SizeBytes: 128,
                    StoragePolicy: "runtime-data-root-only"));
            }

            public Task<SingleWindowDisasterRecoveryRestoreResult> ScheduleRestoreAsync(
                string packagePath,
                string password,
                CancellationToken cancellationToken = default)
            {
                RestoreCalls++;
                return Task.FromResult(new SingleWindowDisasterRecoveryRestoreResult(
                    Success: true,
                    RestartRequired: true,
                    Message: "scheduled",
                    PackageFileName: Path.GetFileName(packagePath),
                    SafetyBackupRoot: "E:\\RuntimeData\\Backups\\DisasterRecovery\\Safety",
                    StoragePolicy: "runtime-data-root-only"));
            }
        }

        private sealed class FakeWebDavServer : IAsyncDisposable
        {
            private readonly WebApplication _app;

            private FakeWebDavServer(WebApplication app, string baseUrl)
            {
                _app = app;
                BaseUrl = baseUrl;
            }

            public string BaseUrl { get; }

            public Dictionary<string, byte[]> Uploads { get; } = new(StringComparer.OrdinalIgnoreCase);

            public static async Task<FakeWebDavServer> StartAsync()
            {
                string baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls(baseUrl);
                var app = builder.Build();
                var server = new FakeWebDavServer(app, baseUrl);

                app.MapMethods("/{**path}", new[] { "OPTIONS" }, () => Results.Ok());
                app.MapMethods("/{**path}", new[] { "PROPFIND" }, () =>
                    Results.Text(server.BuildPropFindXml(), "application/xml", Encoding.UTF8, statusCode: 207));
                app.MapPut("/{**path}", async (HttpContext context) =>
                {
                    string remotePath = Uri.UnescapeDataString((context.Request.Path.Value ?? string.Empty).Trim('/'));
                    using var stream = new MemoryStream();
                    await context.Request.Body.CopyToAsync(stream);
                    server.Uploads[Path.GetFileName(remotePath)] = stream.ToArray();
                    return Results.Ok();
                });
                app.MapGet("/{**path}", (HttpContext context) =>
                {
                    string remotePath = Uri.UnescapeDataString((context.Request.Path.Value ?? string.Empty).Trim('/'));
                    string fileName = Path.GetFileName(remotePath);
                    return server.Uploads.TryGetValue(fileName, out var bytes)
                        ? Results.File(bytes, "application/zip", fileName)
                        : Results.NotFound();
                });

                await app.StartAsync();
                return server;
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }

            private static int GetAvailablePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private string BuildPropFindXml()
            {
                var builder = new StringBuilder();
                builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                builder.Append("<d:multistatus xmlns:d=\"DAV:\">");
                builder.Append("<d:response><d:href>/</d:href><d:propstat><d:prop><d:resourcetype><d:collection /></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");
                foreach (var upload in Uploads.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string encodedName = Uri.EscapeDataString(upload.Key);
                    string escapedName = WebUtility.HtmlEncode(upload.Key);
                    string modified = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
                    builder.Append("<d:response>");
                    builder.Append("<d:href>/").Append(encodedName).Append("</d:href>");
                    builder.Append("<d:propstat><d:prop>");
                    builder.Append("<d:displayname>").Append(escapedName).Append("</d:displayname>");
                    builder.Append("<d:getcontentlength>").Append(upload.Value.Length.ToString(CultureInfo.InvariantCulture)).Append("</d:getcontentlength>");
                    builder.Append("<d:getlastmodified>").Append(modified).Append("</d:getlastmodified>");
                    builder.Append("<d:resourcetype />");
                    builder.Append("</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat>");
                    builder.Append("</d:response>");
                }

                builder.Append("</d:multistatus>");
                return builder.ToString();
            }
        }
    }
}
