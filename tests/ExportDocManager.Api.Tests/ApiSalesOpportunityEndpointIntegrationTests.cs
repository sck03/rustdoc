using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiSalesOpportunityEndpointIntegrationTests
    {
        [Fact]
        public async Task SalesOpportunityEndpoints_ShouldTrackQuotesWithoutReplacingCustomersProductsOrInvoices()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync("sales-opportunities", "sales-opportunities.db");
            using var anonymous = harness.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/crm/opportunities")).StatusCode);
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var customerResponse = await client.PostAsJsonAsync("/api/crm/customers", new ApiCrmCustomerSaveRequest(
                0, "Opportunity Customer", "US", string.Empty, "展会", string.Empty, null));
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(customerResponse);
            int productId;
            await using (var context = CreateContext(harness.DatabasePath))
            {
                var product = new Product { ProductCode = "OPP-001", NameCN = "商机产品", NameEN = "Opportunity Product" };
                context.Products.Add(product); await context.SaveChangesAsync(); productId = product.Id;
            }

            var createResponse = await client.PostAsJsonAsync("/api/crm/opportunities", new ApiSalesOpportunitySaveRequest(
                0, customer.Id, productId, "秋季订单", "QT-OPP-001", 12500m, "USD", 60,
                DateOnly.FromDateTime(DateTime.Today.AddDays(30)), "确认样品", "只做销售跟踪", "首次报价"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var opportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(createResponse);
            Assert.Equal("OPP-001", opportunity.ProductCode);
            Assert.Equal("线索", opportunity.Stage);
            Assert.Equal(["需求确认"], opportunity.AllowedNextStages);

            var duplicateResponse = await client.PostAsJsonAsync("/api/crm/opportunities", new ApiSalesOpportunitySaveRequest(
                0, customer.Id, null, "重复报价", "QT-OPP-001", 1m, "USD", 10, null, string.Empty, string.Empty, string.Empty));
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

            var invalidTransitionResponse = await client.PostAsJsonAsync(
                $"/api/crm/opportunities/{opportunity.Id}/transition",
                new ApiSalesOpportunityTransitionRequest("谈判中", "不得跳级", opportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.BadRequest, invalidTransitionResponse.StatusCode);
            var qualificationResponse = await client.PostAsJsonAsync(
                $"/api/crm/opportunities/{opportunity.Id}/transition",
                new ApiSalesOpportunityTransitionRequest("需求确认", "需求已确认", opportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, qualificationResponse.StatusCode);
            var qualifiedOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(qualificationResponse);
            var quotedResponse = await client.PostAsJsonAsync(
                $"/api/crm/opportunities/{opportunity.Id}/transition",
                new ApiSalesOpportunityTransitionRequest("已报价", "报价已发出", qualifiedOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, quotedResponse.StatusCode);
            var quotedOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(quotedResponse);

            var updateResponse = await client.PutAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}",
                new ApiSalesOpportunitySaveRequest(opportunity.Id, customer.Id, productId, "秋季订单", "QT-OPP-001",
                    13000m, "USD", 75, opportunity.ExpectedCloseDate, "确认付款条件", "报价仍不是正式发票",
                    "客户要求调整付款条件", quotedOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(updateResponse);
            Assert.Equal("已报价", updatedOpportunity.Stage);
            var negotiatingResponse = await client.PostAsJsonAsync(
                $"/api/crm/opportunities/{opportunity.Id}/transition",
                new ApiSalesOpportunityTransitionRequest("谈判中", "进入付款条件谈判", updatedOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, negotiatingResponse.StatusCode);
            var negotiatingOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(negotiatingResponse);
            Assert.Equal("谈判中", negotiatingOpportunity.Stage);
            var staleResponse = await client.PutAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}",
                new ApiSalesOpportunitySaveRequest(opportunity.Id, customer.Id, productId, "过期修改",
                    opportunity.QuotationNo, opportunity.EstimatedAmount, opportunity.Currency,
                    opportunity.ProbabilityPercent, opportunity.ExpectedCloseDate, opportunity.NextAction,
                    opportunity.Notes, "过期版本", updatedOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            var noteResponse = await client.PutAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}",
                new ApiSalesOpportunitySaveRequest(opportunity.Id, customer.Id, productId, negotiatingOpportunity.Title,
                    negotiatingOpportunity.QuotationNo, negotiatingOpportunity.EstimatedAmount,
                    negotiatingOpportunity.Currency, negotiatingOpportunity.ProbabilityPercent, negotiatingOpportunity.ExpectedCloseDate,
                    negotiatingOpportunity.NextAction, negotiatingOpportunity.Notes, "补充客户会议记录",
                    negotiatingOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, noteResponse.StatusCode);
            var notedOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(noteResponse);
            var history = await client.GetFromJsonAsync<List<ApiSalesOpportunityHistoryDto>>($"/api/crm/opportunities/{opportunity.Id}/history");
            Assert.Equal(6, history?.Count);
            Assert.Equal("进展备注", history![0].ChangeType);
            Assert.Equal("补充客户会议记录", history[0].ChangeNote);
            Assert.Equal("阶段变更", history[1].ChangeType);
            Assert.Equal("报价更新", history[2].ChangeType);
            Assert.Equal("创建", history[5].ChangeType);

            var dashboard = await client.GetFromJsonAsync<ApiCrmDashboardDto>("/api/crm/dashboard");
            Assert.Equal(1, dashboard?.OpportunityStages.Single(item => item.Stage == "谈判中").Count);
            var usd = Assert.Single(dashboard!.OpportunityCurrencies);
            Assert.Equal("USD", usd.Currency);
            Assert.Equal(13000m, usd.EstimatedAmount);
            Assert.Equal(9750m, usd.WeightedAmount);
            Assert.Equal(opportunity.Id, Assert.Single(dashboard.UpcomingOpportunityClosings).Id);

            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(
                $"/api/crm/customers/{customer.Id}?expectedVersion={customer.VersionNumber}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/master-data/products/{productId}")).StatusCode);
            var page = await client.GetFromJsonAsync<ApiPagedResponse<ApiSalesOpportunityDto>>("/api/crm/opportunities?keyword=秋季&pageNumber=1&pageSize=10");
            var preserved = Assert.Single(page!.Items);
            Assert.Null(preserved.ProductId);
            var invoices = await client.GetFromJsonAsync<ApiPagedResponse<ApiInvoiceListItemDto>>("/api/invoices?pageNumber=1&pageSize=10");
            Assert.Empty(invoices?.Items ?? []);

            var archiveResponse = await client.PostAsJsonAsync(
                $"/api/crm/opportunities/{opportunity.Id}/archive",
                new ApiSalesOpportunityLifecycleRequest(notedOpportunity.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<ApiPagedResponse<ApiSalesOpportunityDto>>(
                "/api/crm/opportunities?pageNumber=1&pageSize=10"))!.Items);
            var archivedHistory = await client.GetFromJsonAsync<List<ApiSalesOpportunityHistoryDto>>(
                $"/api/crm/opportunities/{opportunity.Id}/history");
            Assert.Equal("归档", archivedHistory![0].ChangeType);
            Assert.Equal(7, archivedHistory.Count);
            var dashboardAfterArchive = await client.GetFromJsonAsync<ApiCrmDashboardDto>("/api/crm/dashboard");
            Assert.All(dashboardAfterArchive!.OpportunityStages, stage => Assert.Equal(0, stage.Count));
            Assert.Empty(dashboardAfterArchive.OpportunityCurrencies);
            Assert.Empty(dashboardAfterArchive.UpcomingOpportunityClosings);
            var archivedCustomerDelete = await client.DeleteAsync(
                $"/api/crm/customers/{customer.Id}?expectedVersion={customer.VersionNumber}");
            Assert.Equal(HttpStatusCode.Conflict, archivedCustomerDelete.StatusCode);
            Assert.Contains("商机历史", await archivedCustomerDelete.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task SalesOpportunityDashboard_ShouldPreserveDecimalFinancialPrecision()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "sales-opportunity-decimal-dashboard",
                "sales-opportunity-decimal-dashboard.db");
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var customerResponse = await client.PostAsJsonAsync("/api/crm/customers", new ApiCrmCustomerSaveRequest(
                0, "Precision Customer", "CN", string.Empty, "测试", string.Empty, null));
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(customerResponse);
            const decimal estimatedAmount = 99999999999999.1234m;
            const int probabilityPercent = 37;
            var createResponse = await client.PostAsJsonAsync("/api/crm/opportunities", new ApiSalesOpportunitySaveRequest(
                0, customer.Id, null, "高精度金额商机", "QT-PRECISION-001", estimatedAmount,
                "usd", probabilityPercent, null, string.Empty, string.Empty, "验证金额精度"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var dashboard = await client.GetFromJsonAsync<ApiCrmDashboardDto>("/api/crm/dashboard");
            var usd = Assert.Single(dashboard!.OpportunityCurrencies);
            Assert.Equal("USD", usd.Currency);
            Assert.Equal(estimatedAmount, usd.EstimatedAmount);
            Assert.Equal(estimatedAmount * probabilityPercent / 100m, usd.WeightedAmount);
        }

        private static AppDbContext CreateContext(string databasePath) => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(DbHelper.BuildConnectionString(databasePath)).Options);
    }
}
