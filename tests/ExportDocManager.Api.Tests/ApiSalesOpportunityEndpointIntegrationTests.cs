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
                0, "Opportunity Customer", "US", string.Empty, "跟进中", "展会", string.Empty, null));
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCrmCustomerDto>(customerResponse);
            int productId;
            await using (var context = CreateContext(harness.DatabasePath))
            {
                var product = new Product { ProductCode = "OPP-001", NameCN = "商机产品", NameEN = "Opportunity Product" };
                context.Products.Add(product); await context.SaveChangesAsync(); productId = product.Id;
            }

            var createResponse = await client.PostAsJsonAsync("/api/crm/opportunities", new ApiSalesOpportunitySaveRequest(
                0, customer.Id, productId, "秋季订单", "已报价", "QT-OPP-001", 12500m, "USD", 60,
                DateTimeOffset.UtcNow.AddDays(30), "确认样品", "只做销售跟踪", "首次报价"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var opportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(createResponse);
            Assert.Equal("OPP-001", opportunity.ProductCode);

            var duplicateResponse = await client.PostAsJsonAsync("/api/crm/opportunities", new ApiSalesOpportunitySaveRequest(
                0, customer.Id, null, "重复报价", "线索", "QT-OPP-001", 1m, "USD", 10, null, string.Empty, string.Empty, string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

            var updateResponse = await client.PutAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}",
                new ApiSalesOpportunitySaveRequest(opportunity.Id, customer.Id, productId, "秋季订单", "谈判中", "QT-OPP-001",
                    13000m, "USD", 75, opportunity.ExpectedCloseAt, "确认付款条件", "报价仍不是正式发票", "客户要求调整付款条件"));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedOpportunity = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSalesOpportunityDto>(updateResponse);
            Assert.Equal("谈判中", updatedOpportunity.Stage);
            var noteResponse = await client.PutAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}",
                new ApiSalesOpportunitySaveRequest(opportunity.Id, customer.Id, productId, updatedOpportunity.Title,
                    updatedOpportunity.Stage, updatedOpportunity.QuotationNo, updatedOpportunity.EstimatedAmount,
                    updatedOpportunity.Currency, updatedOpportunity.ProbabilityPercent, updatedOpportunity.ExpectedCloseAt,
                    updatedOpportunity.NextAction, updatedOpportunity.Notes, "补充客户会议记录"));
            Assert.Equal(HttpStatusCode.OK, noteResponse.StatusCode);
            var history = await client.GetFromJsonAsync<List<ApiSalesOpportunityHistoryDto>>($"/api/crm/opportunities/{opportunity.Id}/history");
            Assert.Equal(3, history?.Count);
            Assert.Equal("进展备注", history![0].ChangeType);
            Assert.Equal("补充客户会议记录", history[0].ChangeNote);
            Assert.Equal("阶段与报价更新", history[1].ChangeType);
            Assert.Equal("创建", history[2].ChangeType);

            var dashboard = await client.GetFromJsonAsync<ApiCrmDashboardDto>("/api/crm/dashboard");
            Assert.Equal(1, dashboard?.OpportunityStages.Single(item => item.Stage == "谈判中").Count);
            var usd = Assert.Single(dashboard!.OpportunityCurrencies);
            Assert.Equal("USD", usd.Currency);
            Assert.Equal(13000m, usd.EstimatedAmount);
            Assert.Equal(9750m, usd.WeightedAmount);
            Assert.Equal(opportunity.Id, Assert.Single(dashboard.UpcomingOpportunityClosings).Id);

            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/crm/customers/{customer.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/master-data/products/{productId}")).StatusCode);
            var page = await client.GetFromJsonAsync<ApiPagedResponse<ApiSalesOpportunityDto>>("/api/crm/opportunities?keyword=秋季&pageNumber=1&pageSize=10");
            var preserved = Assert.Single(page!.Items);
            Assert.Null(preserved.ProductId);
            var invoices = await client.GetFromJsonAsync<ApiPagedResponse<ApiInvoiceListItemDto>>("/api/invoices?pageNumber=1&pageSize=10");
            Assert.Empty(invoices?.Items ?? []);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/crm/opportunities/{opportunity.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/crm/customers/{customer.Id}")).StatusCode);
        }

        private static AppDbContext CreateContext(string databasePath) => new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={databasePath}").Options);
    }
}
