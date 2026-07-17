using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiAuditLogManagementEndpointIntegrationTests
    {
        [Fact]
        public async Task AuditLogManagementEndpoints_ShouldExportAndCleanupWithAdminGuard()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-audit-management",
                "api-audit-management.db",
                "audit-desktop-token");
            using var anonymousClient = harness.CreateClient(desktopAccessToken: "audit-desktop-token");

            var anonymousExportResponse = await anonymousClient.PostAsJsonAsync("/api/audit-logs/save-to-path", new
            {
                destinationPath = Path.Combine(harness.DataRoot, "Exports", "anonymous.xlsx")
            });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousExportResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken, "audit-desktop-token");

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "audit-operator",
                fullName = "Audit Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "audit-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken, "audit-desktop-token");
            var forbiddenExportResponse = await operatorClient.PostAsJsonAsync("/api/audit-logs/save-to-path", new
            {
                destinationPath = Path.Combine(harness.DataRoot, "Exports", "forbidden.xlsx")
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenExportResponse.StatusCode);

            await SeedAuditLogsAsync(harness.DatabasePath);
            string exportPath = Path.Combine(harness.DataRoot, "Exports", "audit-logs.xlsx");

            var invalidExportPathResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/save-to-path", new
            {
                entityName = "Invoice",
                userId = "audit-admin",
                destinationPath = Path.Combine(harness.DataRoot, "Exports", "audit-logs.txt")
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalidExportPathResponse.StatusCode);

            var exportResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/save-to-path", new
            {
                entityName = "Invoice",
                userId = "audit-admin",
                destinationPath = exportPath
            });
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            var exportResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiAuditLogCommandResponse>(exportResponse);
            Assert.True(exportResult.Success);
            Assert.Equal(2, exportResult.AffectedCount);
            Assert.Equal(Path.GetFullPath(exportPath), Path.GetFullPath(exportResult.DestinationPath));
            Assert.Contains("用户显式选择", exportResult.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取发票/报关或付款/报销业务表", exportResult.StoragePolicy, StringComparison.Ordinal);
            Assert.True(File.Exists(exportPath));

            using (var workbook = new XLWorkbook(exportPath))
            {
                var worksheet = workbook.Worksheet("AuditLogs");
                Assert.Equal("时间", worksheet.Cell(1, 1).GetString());
                Assert.Equal("Invoice", worksheet.Cell(2, 2).GetString());
                Assert.Equal("audit-admin", worksheet.Cell(2, 5).GetString());
                Assert.Equal("Invoice", worksheet.Cell(3, 2).GetString());
            }

            var downloadResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/download", new
            {
                entityName = "Invoice",
                userId = "audit-admin"
            });
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadResponse.Content.Headers.ContentType?.MediaType);
            string downloadFileName =
                downloadResponse.Content.Headers.ContentDisposition?.FileNameStar ??
                downloadResponse.Content.Headers.ContentDisposition?.FileName ??
                string.Empty;
            Assert.Contains("AuditLogs_", downloadFileName, StringComparison.Ordinal);
            byte[] downloadContent = await downloadResponse.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(downloadContent);
            using (var workbook = new XLWorkbook(new MemoryStream(downloadContent)))
            {
                var worksheet = workbook.Worksheet("AuditLogs");
                Assert.Equal("时间", worksheet.Cell(1, 1).GetString());
                Assert.Equal("Invoice", worksheet.Cell(2, 2).GetString());
            }

            var unconfirmedDeleteResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/delete", new
            {
                entityName = "Invoice",
                userId = "audit-admin",
                confirmed = false
            });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedDeleteResponse.StatusCode);

            var unfilteredDeleteResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/delete", new
            {
                confirmed = true
            });
            Assert.Equal(HttpStatusCode.BadRequest, unfilteredDeleteResponse.StatusCode);

            var deleteResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/delete", new
            {
                entityName = "Invoice",
                userId = "audit-admin",
                confirmed = true
            });
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            var deleteResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiAuditLogCommandResponse>(deleteResponse);
            Assert.Equal(2, deleteResult.AffectedCount);

            var listAfterDeleteResponse = await adminClient.GetAsync("/api/audit-logs?entityName=Invoice&userId=audit-admin");
            Assert.Equal(HttpStatusCode.OK, listAfterDeleteResponse.StatusCode);
            var listAfterDelete = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiAuditLogDto>>(listAfterDeleteResponse);
            Assert.Equal(0, listAfterDelete.TotalCount);

            var cleanupResponse = await adminClient.PostAsJsonAsync("/api/audit-logs/cleanup", new
            {
                daysToKeep = 180,
                confirmed = true
            });
            Assert.Equal(HttpStatusCode.OK, cleanupResponse.StatusCode);
            var cleanupResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiAuditLogCommandResponse>(cleanupResponse);
            Assert.Equal(1, cleanupResult.AffectedCount);

            var cleanupListResponse = await adminClient.GetAsync("/api/audit-logs?entityName=Cleanup");
            Assert.Equal(HttpStatusCode.OK, cleanupListResponse.StatusCode);
            var cleanupList = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiAuditLogDto>>(cleanupListResponse);
            Assert.Equal(1, cleanupList.TotalCount);
        }

        [Fact]
        public async Task AuditLogDownload_ShouldUseBrowserAttachmentWithoutServerPathAccess()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-audit-browser-download",
                "api-audit-browser-download.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var browserAdminClient = harness.CreateClient(adminLogin.AccessToken);
            await SeedAuditLogsAsync(harness.DatabasePath);

            var forbiddenPathExport = await browserAdminClient.PostAsJsonAsync("/api/audit-logs/save-to-path", new
            {
                entityName = "Invoice",
                destinationPath = Path.Combine(harness.DataRoot, "Exports", "browser-must-not-write.xlsx")
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenPathExport.StatusCode);
            Assert.False(File.Exists(Path.Combine(harness.DataRoot, "Exports", "browser-must-not-write.xlsx")));

            var downloadResponse = await browserAdminClient.PostAsJsonAsync("/api/audit-logs/download", new
            {
                entityName = "Invoice",
                userId = "audit-admin"
            });
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                downloadResponse.Content.Headers.ContentType?.MediaType);
            string downloadFileName =
                downloadResponse.Content.Headers.ContentDisposition?.FileNameStar ??
                downloadResponse.Content.Headers.ContentDisposition?.FileName ??
                string.Empty;
            Assert.Contains("AuditLogs_", downloadFileName, StringComparison.Ordinal);

            byte[] content = await downloadResponse.Content.ReadAsByteArrayAsync();
            using var workbook = new XLWorkbook(new MemoryStream(content));
            var worksheet = workbook.Worksheet("AuditLogs");
            Assert.Equal("Invoice", worksheet.Cell(2, 2).GetString());
            Assert.Equal("audit-admin", worksheet.Cell(2, 5).GetString());
        }

        private static async Task SeedAuditLogsAsync(string databasePath)
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();

            await InsertAuditLogAsync(
                connection,
                "Invoice",
                "Added",
                "INV-AUDIT-1",
                "audit-admin",
                DateTime.UtcNow.AddDays(-3),
                "{\"InvoiceNo\":\"INV-AUDIT-1\"}");
            await InsertAuditLogAsync(
                connection,
                "Invoice",
                "Modified",
                "INV-AUDIT-2",
                "audit-admin",
                DateTime.UtcNow.AddDays(-2),
                "{\"InvoiceNo\":\"INV-AUDIT-2\"}");
            await InsertAuditLogAsync(
                connection,
                "Payment",
                "Added",
                "PAY-AUDIT-1",
                "audit-admin",
                DateTime.UtcNow.AddDays(-1),
                "{\"PaymentNo\":\"PAY-AUDIT-1\"}");
            await InsertAuditLogAsync(
                connection,
                "Cleanup",
                "Added",
                "OLD-AUDIT-1",
                "cleanup-user",
                DateTime.UtcNow.AddDays(-300),
                "{}");
            await InsertAuditLogAsync(
                connection,
                "Cleanup",
                "Added",
                "RECENT-AUDIT-1",
                "cleanup-user",
                DateTime.UtcNow.AddDays(-2),
                "{}");
        }

        private static async Task InsertAuditLogAsync(
            SqliteConnection connection,
            string entityName,
            string action,
            string entityId,
            string userId,
            DateTime timestamp,
            string newValues)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO AuditLogs (EntityName, Action, EntityId, OldValues, NewValues, UserId, Timestamp)
                VALUES ($entityName, $action, $entityId, $oldValues, $newValues, $userId, $timestamp);
                """;
            command.Parameters.AddWithValue("$entityName", entityName);
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$entityId", entityId);
            command.Parameters.AddWithValue("$oldValues", string.Empty);
            command.Parameters.AddWithValue("$newValues", newValues);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$timestamp", timestamp);
            await command.ExecuteNonQueryAsync();
        }
    }
}
