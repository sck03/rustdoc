using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiCrmEndpointIntegrationTests
    {
        [Fact]
        public async Task CrmEndpoints_ShouldKeepSalesDataIndependentAndPreserveHistoryRules()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync("crm", "crm.db");
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var createCustomerResponse = await client.PostAsJsonAsync("/api/crm/customers", new ApiCrmCustomerSaveRequest(
                0, "Acme Trading", "US", "https://example.com", "展会", "独立 CRM 客户", null));
            Assert.Equal(HttpStatusCode.Created, createCustomerResponse.StatusCode);
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(createCustomerResponse);

            var updateCustomerResponse = await client.PutAsJsonAsync($"/api/crm/customers/{customer.Id}",
                new ApiCrmCustomerSaveRequest(customer.Id, customer.Name, customer.CountryRegion, customer.Website,
                    customer.Source, "独立 CRM 客户（已更新）", customer.LinkedDocumentCustomerId,
                    customer.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateCustomerResponse.StatusCode);
            var updatedCustomer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(updateCustomerResponse);
            Assert.Equal(2, updatedCustomer.VersionNumber);
            var deactivatedCustomerResponse = await client.PostAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/deactivate",
                new ApiCrmLifecycleRequest(updatedCustomer.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, deactivatedCustomerResponse.StatusCode);
            var deactivatedCustomer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(deactivatedCustomerResponse);
            Assert.Equal("暂停", deactivatedCustomer.Status);
            var staleCustomerResponse = await client.PostAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/restore",
                new ApiCrmLifecycleRequest(updatedCustomer.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleCustomerResponse.StatusCode);
            var restoredCustomerResponse = await client.PostAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/restore",
                new ApiCrmLifecycleRequest(deactivatedCustomer.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, restoredCustomerResponse.StatusCode);

            var documentCustomers = await client.GetFromJsonAsync<List<ApiCustomerDto>>("/api/master-data/customers");
            Assert.Empty(documentCustomers ?? []);

            var firstContactResponse = await client.PostAsJsonAsync($"/api/crm/customers/{customer.Id}/contacts",
                new ApiCrmContactSaveRequest(0, customer.Id, "Alice", "Buyer", "alice@example.com", "100", "alice-im"));
            Assert.Equal(HttpStatusCode.Created, firstContactResponse.StatusCode);

            var secondContactResponse = await client.PostAsJsonAsync($"/api/crm/customers/{customer.Id}/contacts",
                new ApiCrmContactSaveRequest(0, customer.Id, "Bob", "Manager", "bob@example.com", "200", "bob-im"));
            Assert.Equal(HttpStatusCode.Created, secondContactResponse.StatusCode);
            var secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmContactDto>(secondContactResponse);

            var primaryContactResponse = await client.PostAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}/set-primary",
                new ApiCrmLifecycleRequest(secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, primaryContactResponse.StatusCode);
            secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmContactDto>(primaryContactResponse);
            var contacts = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmContactDto>>(
                $"/api/crm/customers/{customer.Id}/contacts?pageNumber=1&pageSize=20");
            var contactItems = contacts!.Items;
            Assert.Equal(2, contactItems.Count);
            Assert.Single(contactItems, item => item.IsPrimary);
            Assert.True(contactItems.Single(item => item.Id == secondContact.Id).IsPrimary);
            var contactUpdateResponse = await client.PutAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}",
                new ApiCrmContactSaveRequest(secondContact.Id, customer.Id, secondContact.Name, "Director",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, contactUpdateResponse.StatusCode);
            var updatedContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmContactDto>(contactUpdateResponse);
            var staleContactResponse = await client.PutAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}",
                new ApiCrmContactSaveRequest(secondContact.Id, customer.Id, secondContact.Name, "Stale",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleContactResponse.StatusCode);

            var followUpResponse = await client.PostAsJsonAsync("/api/crm/follow-ups", new ApiCrmFollowUpSaveRequest(
                0, customer.Id, secondContact.Id, "邮件", "客户等待新版报价", "发送报价", null,
                DateTimeOffset.UtcNow.AddDays(-1)));
            Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);
            var followUp = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmFollowUpDto>(followUpResponse);

            var updateFollowUpResponse = await client.PutAsJsonAsync($"/api/crm/follow-ups/{followUp.Id}",
                new ApiCrmFollowUpSaveRequest(followUp.Id, customer.Id, secondContact.Id, "电话",
                    "客户确认收到报价", "下周确认订单", followUp.FollowedUpAt,
                    DateTimeOffset.UtcNow.AddDays(2), followUp.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateFollowUpResponse.StatusCode);
            var updatedFollowUp = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmFollowUpDto>(updateFollowUpResponse);
            Assert.Equal("电话", updatedFollowUp.Type);
            var followUpPage = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmFollowUpDto>>(
                "/api/crm/follow-ups/page?includeCompleted=false&pageNumber=1&pageSize=20");
            Assert.Equal(1, followUpPage?.TotalCount);
            Assert.Equal(updatedFollowUp.Id, Assert.Single(followUpPage!.Items).Id);
            var staleFollowUpResponse = await client.PutAsJsonAsync($"/api/crm/follow-ups/{followUp.Id}",
                new ApiCrmFollowUpSaveRequest(followUp.Id, customer.Id, secondContact.Id, "邮件",
                    "过期修改", followUp.NextAction, followUp.FollowedUpAt, followUp.NextFollowUpAt,
                    followUp.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleFollowUpResponse.StatusCode);

            var dashboard = await client.GetFromJsonAsync<ApiCrmDashboardDto>("/api/crm/dashboard");
            Assert.Equal(1, dashboard?.CustomerCount);
            Assert.Equal(2, dashboard?.ContactCount);
            Assert.Equal(1, dashboard?.PendingFollowUpCount);
            Assert.Equal(0, dashboard?.OverdueFollowUpCount);
            Assert.Equal(1, dashboard?.DueNextSevenDaysCount);

            var variableDraft = await client.GetFromJsonAsync<ApiCrmEmailVariableDraftDto>($"/api/crm/customers/{customer.Id}/email-variable-draft");
            Assert.Equal("bob@example.com", variableDraft?.ToAddress);
            Assert.Equal("Acme Trading", variableDraft?.Variables["CustomerName"]);
            Assert.Equal("Bob", variableDraft?.Variables["ContactName"]);

            var batchStatusResponse = await client.PostAsJsonAsync("/api/crm/customers/batch-status",
                new ApiCrmCustomerBatchStatusRequest([customer.Id], "已成交"));
            Assert.Equal(HttpStatusCode.OK, batchStatusResponse.StatusCode);
            Assert.Equal(1, (await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerBatchStatusResult>(batchStatusResponse)).AffectedCount);
            var currentCustomerPage = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmCustomerDto>>(
                "/api/crm/customers/page?pageNumber=1&pageSize=20");
            var currentCustomer = Assert.Single(currentCustomerPage!.Items, item => item.Id == customer.Id);
            var exportResponse = await client.GetAsync("/api/crm/customers/export?status=已成交");
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            byte[] workbook = await exportResponse.Content.ReadAsByteArrayAsync();
            Assert.True(workbook.Length > 1000);
            Assert.Equal((byte)'P', workbook[0]);
            Assert.Equal((byte)'K', workbook[1]);

            var deleteContactResponse = await client.DeleteAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}?expectedVersion={updatedContact.VersionNumber}");
            Assert.Equal(HttpStatusCode.OK, deleteContactResponse.StatusCode);
            var followUpsAfterContactDelete = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmFollowUpDto>>(
                "/api/crm/follow-ups/page?includeCompleted=true&pageNumber=1&pageSize=20");
            Assert.Null(Assert.Single(followUpsAfterContactDelete!.Items).CrmContactId);

            var protectedDeleteResponse = await client.DeleteAsync(
                $"/api/crm/customers/{customer.Id}?expectedVersion={currentCustomer.VersionNumber}");
            Assert.Equal(HttpStatusCode.Conflict, protectedDeleteResponse.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
                $"/api/crm/follow-ups/{followUp.Id}?expectedVersion={updatedFollowUp.VersionNumber}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
                $"/api/crm/customers/{customer.Id}?expectedVersion={currentCustomer.VersionNumber}")).StatusCode);

            const string csv = "客户名称,国家/地区,网站,状态,来源,联系人,职位,邮箱,电话\n" +
                "Acme Trading,US,https://acme.example,潜在客户,展会,Alice,Buyer,alice@acme.example,100\n" +
                "Beta GmbH,DE,https://beta.example,跟进中,网站,Bernd,Manager,bernd@beta.example,200\n" +
                "Acme Trading,US,,潜在客户,重复,,,,\n";
            using var previewContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            previewContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var previewResponse = await client.PostAsync("/api/crm/import/preview?fileName=customers.csv", previewContent);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerImportPreviewDto>(previewResponse);
            Assert.Equal(2, preview.ValidRows);
            Assert.Equal(1, preview.DuplicateRows);

            var importResponse = await client.PostAsJsonAsync("/api/crm/import", new ApiCrmCustomerImportRequest(preview.PreviewId));
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            var importResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerImportResultDto>(importResponse);
            Assert.Equal(2, importResult.CreatedCustomers);
            Assert.Equal(2, importResult.CreatedContacts);
            Assert.Equal(1, importResult.SkippedDuplicates);

            var queryPage = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmCustomerDto>>(
                "/api/crm/customers/page?keyword=Beta&pageNumber=1&pageSize=10");
            Assert.Equal(1, queryPage?.TotalCount);
            Assert.Equal("Beta GmbH", Assert.Single(queryPage!.Items).Name);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiCustomerDto>>("/api/master-data/customers") ?? []);
        }

        [Fact]
        public async Task ImportPreview_ShouldRejectReplayForeignOwnerExpiryAndCorruption()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "crm-import-snapshot-security",
                "crm-import-snapshot-security.db");
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var replay = await CreatePreviewAsync(client, "Replay Customer");
            var replayRequest = new ApiCrmCustomerImportRequest(replay.PreviewId);
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync("/api/crm/import", replayRequest)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("/api/crm/import", replayRequest)).StatusCode);

            var foreign = await CreatePreviewAsync(client, "Foreign Customer");
            await UpdatePreviewAsync(harness.DatabasePath, foreign.PreviewId, preview =>
                preview.OwnerUserId += 10_000);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync(
                    "/api/crm/import",
                    new ApiCrmCustomerImportRequest(foreign.PreviewId))).StatusCode);

            var expired = await CreatePreviewAsync(client, "Expired Customer");
            await UpdatePreviewAsync(harness.DatabasePath, expired.PreviewId, preview =>
                preview.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync(
                    "/api/crm/import",
                    new ApiCrmCustomerImportRequest(expired.PreviewId))).StatusCode);

            var hashCorrupted = await CreatePreviewAsync(client, "Hash Corrupted Customer");
            await UpdatePreviewAsync(harness.DatabasePath, hashCorrupted.PreviewId, preview =>
                preview.PayloadSha256 = new string('0', 64));
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.PostAsJsonAsync(
                    "/api/crm/import",
                    new ApiCrmCustomerImportRequest(hashCorrupted.PreviewId))).StatusCode);

            var jsonCorrupted = await CreatePreviewAsync(client, "Json Corrupted Customer");
            await UpdatePreviewAsync(harness.DatabasePath, jsonCorrupted.PreviewId, preview =>
            {
                preview.PayloadJson = "{";
                preview.PayloadSha256 = ComputeSha256(preview.PayloadJson);
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.PostAsJsonAsync(
                    "/api/crm/import",
                    new ApiCrmCustomerImportRequest(jsonCorrupted.PreviewId))).StatusCode);

            var rowCountCorrupted = await CreatePreviewAsync(client, "Row Count Corrupted Customer");
            await UpdatePreviewAsync(harness.DatabasePath, rowCountCorrupted.PreviewId, preview =>
                preview.RowCount++);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.PostAsJsonAsync(
                    "/api/crm/import",
                    new ApiCrmCustomerImportRequest(rowCountCorrupted.PreviewId))).StatusCode);
        }

        private static async Task<ApiCrmCustomerImportPreviewDto> CreatePreviewAsync(
            HttpClient client,
            string customerName)
        {
            string csv = $"客户名称,国家/地区,状态\n{customerName},CN,潜在客户\n";
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var response = await client.PostAsync(
                "/api/crm/import/preview?fileName=customers.csv",
                content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerImportPreviewDto>(response);
        }

        private static async Task UpdatePreviewAsync(
            string databasePath,
            string previewId,
            Action<ExportDocManager.Models.Entities.BusinessImportPreview> update)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(DbHelper.BuildConnectionString(databasePath))
                .Options;
            await using var context = new AppDbContext(options);
            var preview = await context.BusinessImportPreviews.SingleAsync(item => item.Id == previewId);
            update(preview);
            await context.SaveChangesAsync();
        }

        private static string ComputeSha256(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
