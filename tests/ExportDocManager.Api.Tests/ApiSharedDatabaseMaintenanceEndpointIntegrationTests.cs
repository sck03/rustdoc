using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiSharedDatabaseMaintenanceEndpointIntegrationTests
    {
        [Fact]
        public async Task SharedDatabaseMaintenanceEndpoints_ShouldTransferOwnershipAndExportSupportPackage()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-shared-maintenance",
                "api-shared-maintenance.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "shared-operator",
                fullName = "Shared Operator",
                role = "User",
                departmentId = "OPS",
                companyScope = "HQ",
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);
            var createdOperator = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(createOperatorResponse);

            var createTargetResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "shared-target",
                fullName = "Shared Target",
                role = "User",
                departmentId = "DOC",
                companyScope = "CN",
                isActive = true,
                resetPassword = "target-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createTargetResponse.StatusCode);
            var createdTarget = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(createTargetResponse);

            await SeedOwnedBusinessDataAsync(harness.DatabasePath, createdOperator.User.Id);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "shared-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var forbiddenSupportResponse = await operatorClient.PostAsJsonAsync("/api/support-package/download", new
            {
                includeLatestDatabaseBackup = false,
                includeSampleFiles = false,
                confirmationText = ""
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenSupportResponse.StatusCode);

            var summaryResponse = await adminClient.GetAsync("/api/shared-database/ownership");
            Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
            var summary = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSharedDatabaseOwnershipSummaryResponse>(summaryResponse);
            Assert.Equal(2, summary.TotalInvoices);
            Assert.Equal(1, summary.UnassignedInvoices);
            Assert.Equal(2, summary.TotalPayments);
            Assert.Equal(1, summary.UnassignedPayments);

            var transferResponse = await adminClient.PostAsJsonAsync("/api/shared-database/ownership/transfer", new
            {
                fromUserId = (int?)null,
                toUserId = createdTarget.User.Id,
                includeInvoices = true,
                includePayments = true,
                onlyUnassigned = true,
                departmentId = "",
                companyScope = "",
                confirmationText = "TRANSFER OWNERSHIP"
            });
            Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);
            var transfer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSharedDatabaseOwnershipTransferResponse>(transferResponse);
            Assert.Equal(1, transfer.UpdatedInvoices);
            Assert.Equal(1, transfer.UpdatedPayments);

            await AssertUnassignedRecordsTransferredAsync(harness.DatabasePath, createdTarget.User.Id);

            Directory.CreateDirectory(Path.Combine(harness.DataRoot, "Logs"));
            await File.WriteAllTextAsync(Path.Combine(harness.DataRoot, "Logs", "crash-test.log"), "sample crash diagnostic");
            await File.WriteAllTextAsync(
                Path.Combine(harness.AppRoot, "appsettings.json"),
                """
                {
                  "System": {
                    "DatabaseProvider": "PostgreSQL",
                    "PostgreSqlPassword": "secret-value"
                  },
                  "Email": {
                    "Password": "mail-secret"
                  }
                }
                """);

            var optionalSupportResponse = await adminClient.PostAsJsonAsync("/api/support-package/download", new
            {
                includeLatestDatabaseBackup = true,
                includeSampleFiles = false,
                confirmationText = ""
            });
            Assert.Equal(HttpStatusCode.BadRequest, optionalSupportResponse.StatusCode);

            var supportResponse = await adminClient.PostAsJsonAsync("/api/support-package/download", new
            {
                includeLatestDatabaseBackup = false,
                includeSampleFiles = false,
                confirmationText = ""
            });
            Assert.Equal(HttpStatusCode.OK, supportResponse.StatusCode);
            await using var supportStream = await supportResponse.Content.ReadAsStreamAsync();
            using var archive = new ZipArchive(supportStream, ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("diagnostics/runtime.json"));
            Assert.NotNull(archive.GetEntry("diagnostics/settings-redacted.json"));
            Assert.NotNull(archive.GetEntry("logs/crash-test.log"));
            var settingsEntry = archive.GetEntry("diagnostics/settings-redacted.json")!;
            await using var settingsStream = settingsEntry.Open();
            using var settingsDocument = await JsonDocument.ParseAsync(settingsStream);
            string settingsJson = settingsDocument.RootElement.GetRawText();
            Assert.DoesNotContain("secret-value", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("mail-secret", settingsJson, StringComparison.Ordinal);
            Assert.Contains("***", settingsJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PostgreSqlMaintenanceBackups_ShouldReportSqliteModeWithoutCreatingSystemPathOutput()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-postgresql-maintenance",
                "api-postgresql-maintenance.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var listResponse = await adminClient.GetAsync("/api/postgresql-maintenance/backups");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPostgreSqlPhysicalBackupListResponse>(listResponse);
            Assert.False(list.Status.PostgreSqlSelected);
            Assert.False(list.Status.PostgreSqlConfigured);
            Assert.Equal(Path.Combine(harness.DataRoot, "Backups", "PostgreSQL"), list.Status.BackupRoot);
            Assert.Empty(list.Backups);

            var createResponse = await adminClient.PostAsync("/api/postgresql-maintenance/backups", content: null);
            Assert.Equal(HttpStatusCode.Conflict, createResponse.StatusCode);
            Assert.True(Directory.Exists(list.Status.BackupRoot));
            Assert.Empty(Directory.EnumerateFiles(list.Status.BackupRoot));
        }

        private static async Task SeedOwnedBusinessDataAsync(string databasePath, int ownerUserId)
        {
            await using var context = CreateContext(databasePath);
            await context.Invoices.AddRangeAsync(
                CreateInvoice("UNASSIGNED-1", null, "", ""),
                CreateInvoice("OWNED-1", ownerUserId, "OPS", "HQ"));
            await context.Payments.AddRangeAsync(
                CreatePayment("UNASSIGNED-1", null, "", ""),
                CreatePayment("OWNED-1", ownerUserId, "OPS", "HQ"));
            await context.SaveChangesAsync();
        }

        private static async Task AssertUnassignedRecordsTransferredAsync(string databasePath, int targetUserId)
        {
            await using var context = CreateContext(databasePath);
            var transferredInvoice = await context.Invoices.AsNoTracking().SingleAsync(invoice => invoice.InvoiceNo == "UNASSIGNED-1");
            Assert.Equal(targetUserId, transferredInvoice.OwnerUserId);
            Assert.Equal("DOC", transferredInvoice.DepartmentId);
            Assert.Equal("CN", transferredInvoice.CompanyScope);

            var stillOwnedInvoice = await context.Invoices.AsNoTracking().SingleAsync(invoice => invoice.InvoiceNo == "OWNED-1");
            Assert.NotEqual(targetUserId, stillOwnedInvoice.OwnerUserId);

            var transferredPayment = await context.Payments.AsNoTracking().SingleAsync(payment => payment.InvoiceNo == "UNASSIGNED-1");
            Assert.Equal(targetUserId, transferredPayment.OwnerUserId);
            Assert.Equal("DOC", transferredPayment.DepartmentId);
            Assert.Equal("CN", transferredPayment.CompanyScope);
        }

        private static AppDbContext CreateContext(string databasePath)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(DbHelper.BuildConnectionString(databasePath))
                .Options;
            return new AppDbContext(options);
        }

        private static Invoice CreateInvoice(
            string invoiceNo,
            int? ownerUserId,
            string departmentId,
            string companyScope)
        {
            return new Invoice
            {
                InvoiceNo = invoiceNo,
                Type = "实际数据",
                InvoiceDate = DateTime.Today,
                ShipmentDate = DateTime.Today,
                OwnerUserId = ownerUserId,
                DepartmentId = departmentId,
                CompanyScope = companyScope,
                ContractNo = "",
                LetterOfCreditNo = "",
                LetterOfCreditSourcePath = "",
                LetterOfCreditContent = "",
                IssuingBank = "",
                CustomsBrokerName = "",
                CustomsBrokerCode = "",
                Spare1 = "",
                Spare2 = "",
                Spare3 = "",
                CustomFieldsJson = "",
                PaymentTerms = "",
                PortOfLoading = "",
                PortOfDestination = "",
                DestinationCountry = "",
                ShippingMarks = "",
                TradeTerms = "",
                TransportMode = "",
                Currency = "USD",
                SpecialTerms = "",
                SupervisionMode = "",
                CustomerNameEN = "",
                CustomerAddressEN = "",
                NotifyPartyName = "",
                NotifyPartyAddress = "",
                ExporterNameEN = "",
                ExporterNameCN = "",
                ExporterAddressEN = "",
                ExporterAddressCN = "",
                ExporterCreditCode = "",
                ExporterCustomsCode = "",
                BankName = "",
                BankAccount = "",
                SwiftCode = ""
            };
        }

        private static Payment CreatePayment(
            string invoiceNo,
            int? ownerUserId,
            string departmentId,
            string companyScope)
        {
            return new Payment
            {
                InvoiceNo = invoiceNo,
                OwnerUserId = ownerUserId,
                DepartmentId = departmentId,
                CompanyScope = companyScope,
                ShipmentDate = DateTime.Today,
                PaymentDate = DateTime.Today,
                ReceiptDate = DateTime.Today,
                Department = "",
                Project = "",
                PaymentMethod = "",
                PayeeName = "",
                PayerName = "",
                BankName = "",
                AccountNo = "",
                Notes = "",
                GoodsName = "",
                Quantity = "",
                ShipmentCountry = ""
            };
        }
    }
}
