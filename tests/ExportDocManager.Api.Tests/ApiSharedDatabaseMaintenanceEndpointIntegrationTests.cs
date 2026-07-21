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

            var createInactiveResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "shared-inactive",
                fullName = "Shared Inactive",
                role = "User",
                departmentId = "OLD",
                companyScope = "CN",
                isActive = false,
                resetPassword = "inactive-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createInactiveResponse.StatusCode);
            var createdInactive = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(createInactiveResponse);

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
            Assert.Equal(515, summary.TotalOtherBusinessData);
            Assert.Equal(508, summary.UnassignedOtherBusinessData);
            var operatorSummary = Assert.Single(summary.Owners, owner => owner.UserId == createdOperator.User.Id);
            Assert.True(operatorSummary.IsActive);
            Assert.Equal(7, operatorSummary.OtherBusinessDataCount);
            Assert.False(Assert.Single(summary.Owners, owner => owner.UserId == createdInactive.User.Id).IsActive);

            var transferResponse = await adminClient.PostAsJsonAsync("/api/shared-database/ownership/transfer", new
            {
                fromUserId = (int?)null,
                toUserId = createdTarget.User.Id,
                includeInvoices = true,
                includePayments = true,
                includeOtherBusinessData = true,
                onlyUnassigned = true,
                departmentId = "",
                companyScope = "",
                confirmationText = "TRANSFER OWNERSHIP"
            });
            Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);
            var transfer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSharedDatabaseOwnershipTransferResponse>(transferResponse);
            Assert.Equal(1, transfer.UpdatedInvoices);
            Assert.Equal(1, transfer.UpdatedPayments);
            Assert.Equal(508, transfer.UpdatedOtherBusinessData);

            await AssertUnassignedRecordsTransferredAsync(harness.DatabasePath, createdTarget.User.Id);

            var sameUserResponse = await adminClient.PostAsJsonAsync("/api/shared-database/ownership/transfer", new
            {
                fromUserId = createdTarget.User.Id,
                toUserId = createdTarget.User.Id,
                includeInvoices = true,
                includePayments = false,
                includeOtherBusinessData = false,
                onlyUnassigned = false,
                departmentId = "",
                companyScope = "",
                confirmationText = "TRANSFER OWNERSHIP"
            });
            Assert.Equal(HttpStatusCode.Conflict, sameUserResponse.StatusCode);

            var inactiveTargetResponse = await adminClient.PostAsJsonAsync("/api/shared-database/ownership/transfer", new
            {
                fromUserId = createdOperator.User.Id,
                toUserId = createdInactive.User.Id,
                includeInvoices = false,
                includePayments = false,
                includeOtherBusinessData = true,
                onlyUnassigned = false,
                departmentId = "",
                companyScope = "",
                confirmationText = "TRANSFER OWNERSHIP"
            });
            Assert.Equal(HttpStatusCode.Conflict, inactiveTargetResponse.StatusCode);

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
            var unassignedCustomer = CreateCrmCustomer("UNASSIGNED CRM", null, "", "");
            var ownedCustomer = CreateCrmCustomer("OWNED CRM", ownerUserId, "OPS", "HQ");
            await context.CrmCustomers.AddRangeAsync(unassignedCustomer, ownedCustomer);
            await context.SaveChangesAsync();
            await context.CrmFollowUps.AddRangeAsync(
                CreateCrmFollowUp(unassignedCustomer.Id, "UNASSIGNED FOLLOWUP", null, "", ""),
                CreateCrmFollowUp(ownedCustomer.Id, "OWNED FOLLOWUP", ownerUserId, "OPS", "HQ"));
            await context.SupplierCompanies.AddRangeAsync(
                CreateSupplier("UNASSIGNED SUPPLIER", null, "", ""),
                CreateSupplier("OWNED SUPPLIER", ownerUserId, "OPS", "HQ"));
            await context.SalesOpportunities.AddRangeAsync(
                CreateOpportunity(unassignedCustomer.Id, "UNASSIGNED OPPORTUNITY", null, "", ""),
                CreateOpportunity(ownedCustomer.Id, "OWNED OPPORTUNITY", ownerUserId, "OPS", "HQ"));
            await context.EmailTemplates.AddRangeAsync(
                CreateEmailTemplate("UNASSIGNED EMAIL", null, "", ""),
                CreateEmailTemplate("OWNED EMAIL", ownerUserId, "OPS", "HQ"));
            await context.UserReportTemplates.AddRangeAsync(
                CreateReportTemplate("UNASSIGNED REPORT", null, "", ""),
                CreateReportTemplate("OWNED REPORT", ownerUserId, "OPS", "HQ"));
            await context.ContainerProjects.AddRangeAsync(
                CreateContainerProject("UNASSIGNED CONTAINER", null, "", ""),
                CreateContainerProject("OWNED CONTAINER", ownerUserId, "OPS", "HQ"));
            await context.ContainerProjects.AddRangeAsync(
                Enumerable.Range(1, 501)
                    .Select(index => CreateContainerProject($"BATCH CONTAINER {index:000}", null, "", "")));
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

            AssertTransferred(await context.CrmCustomers.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED CRM"), targetUserId);
            AssertTransferred(await context.CrmFollowUps.AsNoTracking().SingleAsync(item => item.Summary == "UNASSIGNED FOLLOWUP"), targetUserId);
            AssertTransferred(await context.SupplierCompanies.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED SUPPLIER"), targetUserId);
            AssertTransferred(await context.SalesOpportunities.AsNoTracking().SingleAsync(item => item.Title == "UNASSIGNED OPPORTUNITY"), targetUserId);
            AssertTransferred(await context.EmailTemplates.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED EMAIL"), targetUserId);
            AssertTransferred(await context.UserReportTemplates.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED REPORT"), targetUserId);
            AssertTransferred(await context.ContainerProjects.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED CONTAINER"), targetUserId);
            Assert.Equal(
                501,
                await context.ContainerProjects.AsNoTracking().CountAsync(item => item.Name.StartsWith("BATCH CONTAINER ") && item.OwnerUserId == targetUserId));

            Assert.Equal(targetUserId, (await context.CrmCustomers.AsNoTracking().SingleAsync(item => item.Name == "UNASSIGNED CRM")).OwnerUserId);
            Assert.NotEqual(targetUserId, (await context.CrmCustomers.AsNoTracking().SingleAsync(item => item.Name == "OWNED CRM")).OwnerUserId);
        }

        private static void AssertTransferred(CrmCustomer item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(CrmFollowUp item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(SupplierCompany item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(SalesOpportunity item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(EmailTemplate item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(UserReportTemplate item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
        }

        private static void AssertTransferred(ContainerProject item, int targetUserId)
        {
            Assert.Equal(targetUserId, item.OwnerUserId);
            Assert.Equal("DOC", item.DepartmentId);
            Assert.Equal("CN", item.CompanyScope);
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

        private static CrmCustomer CreateCrmCustomer(string name, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            Name = name,
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static CrmFollowUp CreateCrmFollowUp(int customerId, string summary, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            CrmCustomerId = customerId,
            Summary = summary,
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static SupplierCompany CreateSupplier(string name, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            Name = name,
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static SalesOpportunity CreateOpportunity(int customerId, string title, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            CrmCustomerId = customerId,
            Title = title,
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static EmailTemplate CreateEmailTemplate(string name, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            Name = name,
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static UserReportTemplate CreateReportTemplate(string name, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            Name = name,
            ContentHtml = "<html></html>",
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope
        };

        private static ContainerProject CreateContainerProject(string name, int? ownerUserId, string departmentId, string companyScope) => new()
        {
            Name = name,
            ContainerType = "40HQ",
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            CompanyScope = companyScope,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
