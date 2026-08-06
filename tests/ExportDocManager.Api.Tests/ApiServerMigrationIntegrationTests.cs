using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiServerMigrationIntegrationTests
    {
        private const string AdminPassword = "Admin-recovery-2026!";
        private const string PackagePassword = "Migration-package-2026!";

        [Fact]
        public async Task RestoreUploads_ShouldRequireReauthenticationAndConsumeBoundTicketOnce()
        {
            var migrationService = new StubServerMigrationService();
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-server-migration-restore",
                "api-server-migration-restore.db",
                configureServices: services => services.AddSingleton<IServerMigrationService>(migrationService));
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await SetAdminPasswordAndLoginAsync(harness, anonymousClient);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var emptyPasswordResponse = await adminClient.PostAsJsonAsync(
                "/api/server-migration/authorization",
                new { action = ApiSensitiveOperationAction.RestoreDatabase, adminPassword = string.Empty });
            Assert.Equal(HttpStatusCode.BadRequest, emptyPasswordResponse.StatusCode);

            var wrongPasswordResponse = await adminClient.PostAsJsonAsync(
                "/api/server-migration/authorization",
                new { action = ApiSensitiveOperationAction.RestoreDatabase, adminPassword = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);

            ApiSensitiveOperationAuthorizationResponse databaseAuthorization =
                await AuthorizeAsync(adminClient, ApiSensitiveOperationAction.RestoreDatabase);
            using var missingConfirmation = CreateBinaryUploadRequest(
                "/api/postgresql-maintenance/backups/upload-restore",
                [1, 2, 3],
                databaseAuthorization.Ticket);
            missingConfirmation.Headers.Add(
                ApiEndpointRouteBuilderExtensions.PostgreSqlBackupFileNameHeader,
                "database.dump");
            var missingConfirmationResponse = await adminClient.SendAsync(missingConfirmation);
            Assert.Equal(HttpStatusCode.BadRequest, missingConfirmationResponse.StatusCode);

            using var databaseRestore = CreateBinaryUploadRequest(
                "/api/postgresql-maintenance/backups/upload-restore",
                [1, 2, 3],
                databaseAuthorization.Ticket);
            databaseRestore.Headers.Add(
                ApiEndpointRouteBuilderExtensions.PostgreSqlBackupFileNameHeader,
                "database.dump");
            databaseRestore.Headers.Add(
                ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader,
                "RESTORE DATABASE");
            var databaseRestoreResponse = await adminClient.SendAsync(databaseRestore);
            Assert.Equal(HttpStatusCode.OK, databaseRestoreResponse.StatusCode);
            Assert.Equal(1, migrationService.DatabaseRestoreCalls);
            Assert.Equal("database.dump", migrationService.LastDatabaseFileName);
            Assert.Equal(new byte[] { 1, 2, 3 }, migrationService.LastDatabaseBytes);

            using var replay = CreateBinaryUploadRequest(
                "/api/postgresql-maintenance/backups/upload-restore",
                [4, 5, 6],
                databaseAuthorization.Ticket);
            replay.Headers.Add(
                ApiEndpointRouteBuilderExtensions.PostgreSqlBackupFileNameHeader,
                "replay.dump");
            replay.Headers.Add(
                ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader,
                "RESTORE DATABASE");
            var replayResponse = await adminClient.SendAsync(replay);
            Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
            Assert.Equal(1, migrationService.DatabaseRestoreCalls);

            ApiSensitiveOperationAuthorizationResponse serverAuthorization =
                await AuthorizeAsync(adminClient, ApiSensitiveOperationAction.RestoreServer);
            using var serverRestore = CreateBinaryUploadRequest(
                "/api/server-migration/restore",
                [7, 8, 9],
                serverAuthorization.Ticket);
            serverRestore.Headers.Add(
                ApiEndpointRouteBuilderExtensions.ServerMigrationFileNameHeader,
                "server.edmmigration");
            serverRestore.Headers.Add(
                ApiEndpointRouteBuilderExtensions.ServerMigrationPasswordHeader,
                PackagePassword);
            serverRestore.Headers.Add(
                ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader,
                "MIGRATE");
            var serverRestoreResponse = await adminClient.SendAsync(serverRestore);
            Assert.Equal(HttpStatusCode.OK, serverRestoreResponse.StatusCode);
            Assert.Equal(1, migrationService.ServerRestoreCalls);
            Assert.Equal(PackagePassword, migrationService.LastPackagePassword);
            Assert.Equal("admin", migrationService.LastRequestContext?.RequestedBy);
        }

        [Fact]
        public async Task MigrationPackageJob_ShouldStreamOnlyControlledOutputWithDownloadTicket()
        {
            var migrationService = new StubServerMigrationService();
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-server-migration-job",
                "api-server-migration-job.db",
                configureServices: services => services.AddSingleton<IServerMigrationService>(migrationService));
            migrationService.OutputRoot = Path.Combine(harness.DataRoot, "Backups", "StubMigration");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await SetAdminPasswordAndLoginAsync(harness, anonymousClient);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var unconfirmedResponse = await adminClient.PostAsJsonAsync(
                "/api/server-migration/packages",
                new
                {
                    password = PackagePassword,
                    adminPassword = AdminPassword,
                    confirmationText = "wrong"
                });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedResponse.StatusCode);

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/server-migration/packages",
                new
                {
                    password = PackagePassword,
                    adminPassword = AdminPassword,
                    confirmationText = "MIGRATE"
                });
            Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
            BackgroundJobSnapshot accepted =
                await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(createResponse);
            BackgroundJobSnapshot completed = await WaitForJobAsync(adminClient, accepted.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, completed.Status);
            Assert.Equal("stub-server-migration.edmmigration", completed.OutputPath);
            Assert.False(Path.IsPathRooted(completed.OutputPath));
            Assert.Equal(1, migrationService.CreatePackageCalls);
            Assert.Equal(PackagePassword, migrationService.LastPackagePassword);

            var ticketResponse = await adminClient.PostAsync(
                $"/api/jobs/{accepted.JobId}/download-ticket",
                content: null);
            Assert.Equal(HttpStatusCode.OK, ticketResponse.StatusCode);
            ApiDownloadTicket ticket =
                await ApiIntegrationTestHarness.ReadJsonAsync<ApiDownloadTicket>(ticketResponse);
            Assert.StartsWith("/downloads/jobs/", ticket.DownloadUrl, StringComparison.Ordinal);

            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, ticket.DownloadUrl);
            rangeRequest.Headers.Range = new RangeHeaderValue(0, 3);
            var rangeResponse = await anonymousClient.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
            Assert.Equal(new byte[] { 10, 20, 30, 40 }, await rangeResponse.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public void SensitiveOperationTickets_ShouldExpireBindActionAndRemainSingleUse()
        {
            var time = new MutableTimeProvider();
            var service = new ApiSensitiveOperationTicketService(time);

            ApiSensitiveOperationTicket database = service.Issue(
                7,
                ApiSensitiveOperationAction.RestoreDatabase);
            Assert.False(service.Consume(database.Token, 7, ApiSensitiveOperationAction.RestoreServer));
            Assert.False(service.Consume(database.Token, 7, ApiSensitiveOperationAction.RestoreDatabase));

            ApiSensitiveOperationTicket singleUse = service.Issue(
                7,
                ApiSensitiveOperationAction.RestoreDatabase);
            Assert.True(service.Consume(singleUse.Token, 7, ApiSensitiveOperationAction.RestoreDatabase));
            Assert.False(service.Consume(singleUse.Token, 7, ApiSensitiveOperationAction.RestoreDatabase));

            ApiSensitiveOperationTicket expired = service.Issue(
                7,
                ApiSensitiveOperationAction.RestoreDatabase);
            time.Advance(TimeSpan.FromMinutes(6));
            Assert.False(service.Consume(expired.Token, 7, ApiSensitiveOperationAction.RestoreDatabase));
        }

        [Fact]
        public void JobDownloadTickets_ShouldExpireWithoutChangingJobBinding()
        {
            var time = new MutableTimeProvider();
            var service = new ApiDownloadTicketService(time);
            ApiDownloadTicket ticket = service.Issue("background-job", "job-1", "/downloads/jobs");

            Assert.True(service.TryResolve(ticket.Token, "background-job", out string firstJobId));
            Assert.Equal("job-1", firstJobId);
            Assert.True(service.TryResolve(ticket.Token, "background-job", out string secondJobId));
            Assert.Equal("job-1", secondJobId);
            Assert.False(service.TryResolve(ticket.Token, "postgresql-physical-backup", out _));
            Assert.Throws<ArgumentException>(() => service.Issue(
                "background-job",
                "job-2",
                "//external.example/downloads"));

            time.Advance(TimeSpan.FromMinutes(5));
            Assert.False(service.TryResolve(ticket.Token, "background-job", out _));
        }

        private static async Task<ApiLoginResponse> SetAdminPasswordAndLoginAsync(
            ApiIntegrationTestHarness harness,
            HttpClient anonymousClient)
        {
            ApiLoginResponse initial = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var initialAdminClient = harness.CreateClient(initial.AccessToken);
            var updateResponse = await initialAdminClient.PutAsJsonAsync(
                $"/api/users/{initial.User.Id}",
                new
                {
                    username = "admin",
                    fullName = "Administrator",
                    role = UserRoleCatalog.Admin,
                    permissionTemplateId = (int?)null,
                    departmentId = string.Empty,
                    companyScope = string.Empty,
                    isActive = true,
                    resetPassword = AdminPassword
                });
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            return await harness.LoginAsync(anonymousClient, "admin", AdminPassword);
        }

        private static async Task<ApiSensitiveOperationAuthorizationResponse> AuthorizeAsync(
            HttpClient client,
            string action)
        {
            var response = await client.PostAsJsonAsync(
                "/api/server-migration/authorization",
                new { action, adminPassword = AdminPassword });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ApiIntegrationTestHarness
                .ReadJsonAsync<ApiSensitiveOperationAuthorizationResponse>(response);
        }

        private static HttpRequestMessage CreateBinaryUploadRequest(
            string path,
            byte[] content,
            string ticket)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new ByteArrayContent(content)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Headers.Add(
                ApiEndpointRouteBuilderExtensions.SensitiveOperationTicketHeader,
                ticket);
            return request;
        }

        private static async Task<BackgroundJobSnapshot> WaitForJobAsync(
            HttpClient client,
            string jobId)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var response = await client.GetAsync($"/api/jobs/{jobId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                BackgroundJobSnapshot job =
                    await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(response);
                if (job.Status is BackgroundJobStatusCatalog.Succeeded or
                    BackgroundJobStatusCatalog.Failed or
                    BackgroundJobStatusCatalog.Canceled)
                {
                    Assert.True(
                        string.Equals(job.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase),
                        string.IsNullOrWhiteSpace(job.ErrorMessage) ? job.DetailText : job.ErrorMessage);
                    return job;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("服务器迁移后台任务未在测试时限内完成。");
        }

        private sealed class StubServerMigrationService : IServerMigrationService
        {
            public string OutputRoot { get; set; } = string.Empty;
            public int CreatePackageCalls { get; private set; }
            public int ServerRestoreCalls { get; private set; }
            public int DatabaseRestoreCalls { get; private set; }
            public string LastPackagePassword { get; private set; } = string.Empty;
            public string LastDatabaseFileName { get; private set; } = string.Empty;
            public byte[] LastDatabaseBytes { get; private set; } = [];
            public ServerMigrationRequestContext LastRequestContext { get; private set; }

            public ServerMigrationStatus GetStatus() => new(
                true,
                true,
                true,
                false,
                OutputRoot,
                "ready",
                "runtime storage",
                string.Empty,
                string.Empty,
                null);

            public async Task<ServerMigrationPackageResult> CreatePackageAsync(
                string password,
                ServerMigrationRequestContext requestContext,
                CancellationToken cancellationToken = default)
            {
                CreatePackageCalls++;
                LastPackagePassword = password;
                LastRequestContext = requestContext;
                Directory.CreateDirectory(OutputRoot);
                string path = Path.Combine(OutputRoot, "stub-server-migration.edmmigration");
                await File.WriteAllBytesAsync(path, [10, 20, 30, 40, 50, 60], cancellationToken);
                return new ServerMigrationPackageResult(
                    true,
                    "created",
                    Path.GetFileName(path),
                    path,
                    new FileInfo(path).Length,
                    OutputRoot,
                    "runtime storage");
            }

            public async Task<ServerMigrationRestoreResult> StageRestoreAsync(
                Stream package,
                string packageFileName,
                string password,
                ServerMigrationRequestContext requestContext,
                CancellationToken cancellationToken = default)
            {
                ServerRestoreCalls++;
                LastPackagePassword = password;
                LastRequestContext = requestContext;
                await CopyToMemoryAsync(package, cancellationToken);
                return new ServerMigrationRestoreResult(
                    true,
                    true,
                    "scheduled",
                    packageFileName,
                    "safety",
                    "runtime storage");
            }

            public async Task<ServerMigrationRestoreResult> StageDatabaseRestoreAsync(
                Stream databaseBackup,
                string backupFileName,
                ServerMigrationRequestContext requestContext,
                CancellationToken cancellationToken = default)
            {
                DatabaseRestoreCalls++;
                LastDatabaseFileName = backupFileName;
                LastRequestContext = requestContext;
                LastDatabaseBytes = await CopyToMemoryAsync(databaseBackup, cancellationToken);
                return new ServerMigrationRestoreResult(
                    true,
                    true,
                    "scheduled",
                    backupFileName,
                    "safety",
                    "runtime storage");
            }

            private static async Task<byte[]> CopyToMemoryAsync(
                Stream source,
                CancellationToken cancellationToken)
            {
                await using var target = new MemoryStream();
                await source.CopyToAsync(target, cancellationToken);
                return target.ToArray();
            }
        }

        private sealed class MutableTimeProvider : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan duration) => _now = _now.Add(duration);
        }
    }
}
