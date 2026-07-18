using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiSupplierEndpointIntegrationTests
    {
        [Fact]
        public async Task SupplierEndpoints_ShouldSupportIndependentCrudAndSinglePrimaryContact()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync("suppliers", "suppliers.db");
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var createResponse = await client.PostAsJsonAsync("/api/suppliers",
                new ApiSupplierSaveRequest(0, "Ningbo Factory", "CN", "纺织", "https://supplier.example",
                    "合作中", "服装、面料", "独立供应商资料"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var supplier = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(createResponse);

            int productId;
            await using (var context = CreateContext(harness.DatabasePath))
            {
                var product = new Product { ProductCode = "FAB-001", NameCN = "测试面料", NameEN = "Test Fabric" };
                context.Products.Add(product);
                await context.SaveChangesAsync();
                productId = product.Id;
            }

            var updateResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
                new ApiSupplierSaveRequest(supplier.Id, supplier.Name, supplier.CountryRegion, supplier.Category,
                    supplier.Website, "考察中", supplier.MainProducts, supplier.Notes, supplier.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedSupplier = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(updateResponse);
            Assert.Equal("考察中", updatedSupplier.Status);
            var staleSupplierResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
                new ApiSupplierSaveRequest(supplier.Id, supplier.Name, supplier.CountryRegion, supplier.Category,
                    supplier.Website, "暂停", supplier.MainProducts, supplier.Notes, supplier.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleSupplierResponse.StatusCode);

            var first = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts",
                new ApiSupplierContactSaveRequest(0, supplier.Id, "张三", "业务员", "a@example.com", "100", "wx-a", true));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var second = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts",
                new ApiSupplierContactSaveRequest(0, supplier.Id, "李四", "经理", "b@example.com", "200", "wx-b", true));
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierContactDto>(second);

            var contacts = await client.GetFromJsonAsync<List<ApiSupplierContactDto>>($"/api/suppliers/{supplier.Id}/contacts");
            Assert.Single(contacts!, item => item.IsPrimary);
            Assert.True(contacts.Single(item => item.Id == secondContact.Id).IsPrimary);
            var updateContactResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}",
                new ApiSupplierContactSaveRequest(secondContact.Id, supplier.Id, secondContact.Name, "采购总监",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging, true,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateContactResponse.StatusCode);
            var staleContactResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}",
                new ApiSupplierContactSaveRequest(secondContact.Id, supplier.Id, secondContact.Name, "过期修改",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging, true,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleContactResponse.StatusCode);

            var productOptions = await client.GetFromJsonAsync<List<ApiSupplierProductOptionDto>>("/api/suppliers/product-options?keyword=FAB-001");
            Assert.Equal(productId, Assert.Single(productOptions!).Id);
            var createLinkResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/products",
                new ApiSupplierProductLinkSaveRequest(0, supplier.Id, productId, "SUP-FAB-9", 12.3456m, "USD", 21, "供货中"));
            Assert.Equal(HttpStatusCode.Created, createLinkResponse.StatusCode);
            var productLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(createLinkResponse);
            Assert.Equal("FAB-001", productLink.ProductCode);
            Assert.Equal(12.3456m, productLink.ReferencePrice);

            var duplicateLinkResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/products",
                new ApiSupplierProductLinkSaveRequest(0, supplier.Id, productId, "DUPLICATE", 1m, "CNY", 1, "备选"));
            Assert.Equal(HttpStatusCode.BadRequest, duplicateLinkResponse.StatusCode);

            var updateLinkResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/products/{productLink.Id}",
                new ApiSupplierProductLinkSaveRequest(productLink.Id, supplier.Id, productId, "SUP-FAB-10", 13m,
                    "CNY", 14, "备选", productLink.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateLinkResponse.StatusCode);
            var updatedLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(updateLinkResponse);
            Assert.Equal("SUP-FAB-10", updatedLink.SupplierProductCode);
            Assert.Equal("备选", updatedLink.Status);
            var staleLinkResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/products/{productLink.Id}",
                new ApiSupplierProductLinkSaveRequest(productLink.Id, supplier.Id, productId, "STALE", 9m,
                    "USD", 30, "暂停", productLink.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleLinkResponse.StatusCode);
            Assert.Single(await client.GetFromJsonAsync<List<ApiSupplierProductLinkDto>>($"/api/suppliers/{supplier.Id}/products") ?? []);

            var createAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateTimeOffset.UtcNow.AddDays(-1),
                    "订单复盘", 5, 4, 5, 3, "优先合作", "交付稳定，价格仍可继续协商。"));
            Assert.Equal(HttpStatusCode.Created, createAssessmentResponse.StatusCode);
            var assessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(createAssessmentResponse);
            Assert.Equal(4.25m, assessment.AverageScore);
            Assert.Equal("admin", assessment.AssessedBy);

            var invalidAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateTimeOffset.UtcNow,
                    "订单复盘", 6, 4, 5, 3, "合格", string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, invalidAssessmentResponse.StatusCode);

            var updateAssessmentResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}",
                new ApiSupplierAssessmentSaveRequest(assessment.Id, supplier.Id, assessment.AssessedAt,
                    "订单复盘", 5, 5, 5, 4, "优先合作", "复评后交期表现提升。",
                    assessment.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateAssessmentResponse.StatusCode);
            var updatedAssessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(updateAssessmentResponse);
            Assert.Equal(4.75m, updatedAssessment.AverageScore);
            var staleAssessmentResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}",
                new ApiSupplierAssessmentSaveRequest(assessment.Id, supplier.Id, assessment.AssessedAt,
                    "订单复盘", 1, 1, 1, 1, "观察", "过期修改", assessment.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleAssessmentResponse.StatusCode);
            Assert.Single(await client.GetFromJsonAsync<List<ApiSupplierAssessmentDto>>($"/api/suppliers/{supplier.Id}/assessments") ?? []);

            var temporaryAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateTimeOffset.UtcNow,
                    "样品评估", 3, 3, 4, 4, "观察", "临时样品评价。"));
            var temporaryAssessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(temporaryAssessmentResponse);
            Assert.Equal(HttpStatusCode.OK,
                (await client.DeleteAsync($"/api/suppliers/{supplier.Id}/assessments/{temporaryAssessment.Id}")).StatusCode);
            Assert.Single(await client.GetFromJsonAsync<List<ApiSupplierAssessmentDto>>($"/api/suppliers/{supplier.Id}/assessments") ?? []);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/suppliers/{supplier.Id}/products/{productLink.Id}")).StatusCode);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiSupplierProductLinkDto>>($"/api/suppliers/{supplier.Id}/products") ?? []);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/master-data/products/{productId}")).StatusCode);

            var page = await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierDto>>("/api/suppliers/page?keyword=Factory&pageNumber=1&pageSize=10");
            Assert.Equal(1, page?.TotalCount);
            Assert.Equal(supplier.Id, Assert.Single(page!.Items).Id);

            Assert.Empty(await client.GetFromJsonAsync<List<ApiCrmCustomerDto>>("/api/crm/customers") ?? []);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiCustomerDto>>("/api/master-data/customers") ?? []);

            const string csv = "供应商名称,国家/地区,分类,网站,状态,主要产品,联系人,职位,邮箱,电话\n" +
                "Ningbo Factory,CN,纺织,,合作中,面料,重复联系人,,,\n" +
                "Shanghai Parts,CN,机械,https://parts.example,考察中,零件,王五,经理,c@example.com,300\n";
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var previewResponse = await client.PostAsync("/api/suppliers/import/preview?fileName=suppliers.csv", content);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierImportPreviewDto>(previewResponse);
            Assert.Equal(1, preview.ValidRows);
            Assert.Equal(1, preview.DuplicateRows);
            var importResponse = await client.PostAsJsonAsync("/api/suppliers/import", new ApiSupplierImportRequest(preview.Rows));
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            Assert.Equal(1, (await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierImportResultDto>(importResponse)).CreatedSuppliers);

            var allSuppliers = await client.GetFromJsonAsync<List<ApiSupplierDto>>("/api/suppliers");
            var batchResponse = await client.PostAsJsonAsync("/api/suppliers/batch-status",
                new ApiSupplierBatchStatusRequest(allSuppliers!.Select(item => item.Id).ToArray(), "暂停"));
            Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);
            Assert.Equal(2, (await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierBatchStatusResult>(batchResponse)).AffectedCount);

            var assessmentOverview = await client.GetFromJsonAsync<ApiSupplierAssessmentOverviewDto>("/api/suppliers/assessment-overview");
            Assert.NotNull(assessmentOverview);
            Assert.Equal(2, assessmentOverview.TotalSuppliers);
            Assert.Equal(1, assessmentOverview.AssessedSuppliers);
            Assert.Equal(1, assessmentOverview.UnassessedSuppliers);
            Assert.Equal(1, assessmentOverview.PreferredCount);
            Assert.Equal(5m, assessmentOverview.AverageQualityScore);
            Assert.Equal(5m, assessmentOverview.AverageDeliveryScore);
            var overviewItem = Assert.Single(assessmentOverview.Items);
            Assert.Equal(supplier.Id, overviewItem.SupplierCompanyId);
            Assert.Equal(1, overviewItem.AssessmentCount);
            Assert.Equal(4.75m, overviewItem.AverageScore);

            var exportResponse = await client.GetAsync("/api/suppliers/export?status=暂停");
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            byte[] workbook = await exportResponse.Content.ReadAsByteArrayAsync();
            Assert.True(workbook.Length > 1000);
            Assert.Equal((byte)'P', workbook[0]);
            Assert.Equal((byte)'K', workbook[1]);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/suppliers/{supplier.Id}")).StatusCode);
            await using (var context = CreateContext(harness.DatabasePath))
                Assert.False(await context.SupplierAssessments.AnyAsync(item => item.SupplierCompanyId == supplier.Id));
        }

        private static AppDbContext CreateContext(string databasePath)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            return new AppDbContext(options);
        }
    }
}
