using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using ExportDocManager.Api.Hosting;

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
                0, "Acme Trading", "US", "https://example.com", "潜在客户", "展会", "独立 CRM 客户", null));
            Assert.Equal(HttpStatusCode.Created, createCustomerResponse.StatusCode);
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(createCustomerResponse);

            var updateCustomerResponse = await client.PutAsJsonAsync($"/api/crm/customers/{customer.Id}",
                new ApiCrmCustomerSaveRequest(customer.Id, customer.Name, customer.CountryRegion, customer.Website,
                    "跟进中", customer.Source, customer.Notes, customer.LinkedDocumentCustomerId,
                    customer.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateCustomerResponse.StatusCode);
            var updatedCustomer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(updateCustomerResponse);
            Assert.Equal(2, updatedCustomer.VersionNumber);
            var staleCustomerResponse = await client.PutAsJsonAsync($"/api/crm/customers/{customer.Id}",
                new ApiCrmCustomerSaveRequest(customer.Id, customer.Name, customer.CountryRegion, customer.Website,
                    "暂停", customer.Source, customer.Notes, customer.LinkedDocumentCustomerId,
                    customer.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleCustomerResponse.StatusCode);

            var documentCustomers = await client.GetFromJsonAsync<List<ApiCustomerDto>>("/api/master-data/customers");
            Assert.Empty(documentCustomers ?? []);

            var firstContactResponse = await client.PostAsJsonAsync($"/api/crm/customers/{customer.Id}/contacts",
                new ApiCrmContactSaveRequest(0, customer.Id, "Alice", "Buyer", "alice@example.com", "100", "alice-im", true));
            Assert.Equal(HttpStatusCode.Created, firstContactResponse.StatusCode);

            var secondContactResponse = await client.PostAsJsonAsync($"/api/crm/customers/{customer.Id}/contacts",
                new ApiCrmContactSaveRequest(0, customer.Id, "Bob", "Manager", "bob@example.com", "200", "bob-im", true));
            Assert.Equal(HttpStatusCode.Created, secondContactResponse.StatusCode);
            var secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmContactDto>(secondContactResponse);

            var contacts = await client.GetFromJsonAsync<List<ApiCrmContactDto>>($"/api/crm/customers/{customer.Id}/contacts");
            Assert.Equal(2, contacts?.Count);
            Assert.Single(contacts!, item => item.IsPrimary);
            Assert.True(contacts.Single(item => item.Id == secondContact.Id).IsPrimary);
            var contactUpdateResponse = await client.PutAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}",
                new ApiCrmContactSaveRequest(secondContact.Id, customer.Id, secondContact.Name, "Director",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging, true,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, contactUpdateResponse.StatusCode);
            var staleContactResponse = await client.PutAsJsonAsync(
                $"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}",
                new ApiCrmContactSaveRequest(secondContact.Id, customer.Id, secondContact.Name, "Stale",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging, true,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleContactResponse.StatusCode);

            var followUpResponse = await client.PostAsJsonAsync("/api/crm/follow-ups", new ApiCrmFollowUpSaveRequest(
                0, customer.Id, secondContact.Id, "邮件", "客户等待新版报价", "发送报价", null,
                DateTimeOffset.UtcNow.AddDays(-1), false));
            Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);
            var followUp = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmFollowUpDto>(followUpResponse);

            var updateFollowUpResponse = await client.PutAsJsonAsync($"/api/crm/follow-ups/{followUp.Id}",
                new ApiCrmFollowUpSaveRequest(followUp.Id, customer.Id, secondContact.Id, "电话",
                    "客户确认收到报价", "下周确认订单", followUp.FollowedUpAt,
                    DateTimeOffset.UtcNow.AddDays(2), false, followUp.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateFollowUpResponse.StatusCode);
            var updatedFollowUp = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmFollowUpDto>(updateFollowUpResponse);
            Assert.Equal("电话", updatedFollowUp.Type);
            var staleFollowUpResponse = await client.PutAsJsonAsync($"/api/crm/follow-ups/{followUp.Id}",
                new ApiCrmFollowUpSaveRequest(followUp.Id, customer.Id, secondContact.Id, "邮件",
                    "过期修改", followUp.NextAction, followUp.FollowedUpAt, followUp.NextFollowUpAt,
                    false, followUp.VersionNumber));
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
            var exportResponse = await client.GetAsync("/api/crm/customers/export?status=已成交");
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            byte[] workbook = await exportResponse.Content.ReadAsByteArrayAsync();
            Assert.True(workbook.Length > 1000);
            Assert.Equal((byte)'P', workbook[0]);
            Assert.Equal((byte)'K', workbook[1]);

            var deleteContactResponse = await client.DeleteAsync($"/api/crm/customers/{customer.Id}/contacts/{secondContact.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteContactResponse.StatusCode);
            var followUpsAfterContactDelete = await client.GetFromJsonAsync<List<ApiCrmFollowUpDto>>("/api/crm/follow-ups?includeCompleted=true");
            Assert.Null(Assert.Single(followUpsAfterContactDelete!).CrmContactId);

            var protectedDeleteResponse = await client.DeleteAsync($"/api/crm/customers/{customer.Id}");
            Assert.Equal(HttpStatusCode.Conflict, protectedDeleteResponse.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/crm/follow-ups/{followUp.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/crm/customers/{customer.Id}")).StatusCode);

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

            var importResponse = await client.PostAsJsonAsync("/api/crm/import", new ApiCrmCustomerImportRequest(preview.Rows));
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
    }
}
