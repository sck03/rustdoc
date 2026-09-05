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
                    "服装、面料", "独立供应商资料"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var supplier = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(createResponse);
            Assert.Equal("考察中", supplier.Status);

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
                    supplier.Website, supplier.MainProducts, "供应商资料已更新", supplier.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedSupplier = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(updateResponse);
            Assert.Equal("考察中", updatedSupplier.Status);
            var staleSupplierResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
                new ApiSupplierSaveRequest(supplier.Id, supplier.Name, supplier.CountryRegion, supplier.Category,
                    supplier.Website, supplier.MainProducts, "过期修改", supplier.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleSupplierResponse.StatusCode);
            var admitResponse = await client.PostAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/admit",
                new ApiSupplierLifecycleRequest(updatedSupplier.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, admitResponse.StatusCode);
            var admittedSupplier = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(admitResponse);
            Assert.Equal("合作中", admittedSupplier.Status);
            var staleDeactivateResponse = await client.PostAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/deactivate",
                new ApiSupplierLifecycleRequest(updatedSupplier.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleDeactivateResponse.StatusCode);

            var first = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts",
                new ApiSupplierContactSaveRequest(0, supplier.Id, "张三", "业务员", "a@example.com", "100", "wx-a"));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var firstContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierContactDto>(first);
            var second = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/contacts",
                new ApiSupplierContactSaveRequest(0, supplier.Id, "李四", "经理", "b@example.com", "200", "wx-b"));
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierContactDto>(second);

            var setPrimaryResponse = await client.PostAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}/set-primary",
                new ApiSupplierLifecycleRequest(secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, setPrimaryResponse.StatusCode);
            secondContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierContactDto>(setPrimaryResponse);
            var contacts = await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierContactDto>>(
                $"/api/suppliers/{supplier.Id}/contacts?pageNumber=1&pageSize=20");
            var contactItems = contacts!.Items;
            Assert.Single(contactItems, item => item.IsPrimary);
            Assert.True(contactItems.Single(item => item.Id == secondContact.Id).IsPrimary);
            var updateContactResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}",
                new ApiSupplierContactSaveRequest(secondContact.Id, supplier.Id, secondContact.Name, "采购总监",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateContactResponse.StatusCode);
            var updatedContact = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierContactDto>(updateContactResponse);
            var staleContactResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}",
                new ApiSupplierContactSaveRequest(secondContact.Id, supplier.Id, secondContact.Name, "过期修改",
                    secondContact.Email, secondContact.Phone, secondContact.InstantMessaging,
                    secondContact.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleContactResponse.StatusCode);

            var currentContacts = (await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierContactDto>>(
                $"/api/suppliers/{supplier.Id}/contacts?pageNumber=1&pageSize=20"))!.Items;
            var currentFirst = currentContacts.Single(item => item.Id == firstContact.Id);
            var currentSecond = currentContacts.Single(item => item.Id == secondContact.Id);
            var primarySwitches = await Task.WhenAll(
                client.PostAsJsonAsync(
                    $"/api/suppliers/{supplier.Id}/contacts/{currentFirst.Id}/set-primary",
                    new ApiSupplierLifecycleRequest(currentFirst.VersionNumber)),
                client.PostAsJsonAsync(
                    $"/api/suppliers/{supplier.Id}/contacts/{currentSecond.Id}/set-primary",
                    new ApiSupplierLifecycleRequest(currentSecond.VersionNumber)));
            Assert.All(primarySwitches, response => Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            Assert.Single(
                (await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierContactDto>>(
                    $"/api/suppliers/{supplier.Id}/contacts?pageNumber=1&pageSize=20"))!.Items,
                item => item.IsPrimary);

            var productOptions = await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierProductOptionDto>>(
                "/api/suppliers/product-options?keyword=FAB-001&pageNumber=1&pageSize=20");
            Assert.Equal(productId, Assert.Single(productOptions!.Items).Id);
            var createLinkResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/products",
                new ApiSupplierProductLinkSaveRequest(0, supplier.Id, productId, "SUP-FAB-9", 12.3456m, "USD", 21));
            Assert.Equal(HttpStatusCode.Created, createLinkResponse.StatusCode);
            var productLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(createLinkResponse);
            Assert.Equal("FAB-001", productLink.ProductCode);
            Assert.Equal(12.3456m, productLink.ReferencePrice);

            var duplicateLinkResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/products",
                new ApiSupplierProductLinkSaveRequest(0, supplier.Id, productId, "DUPLICATE", 1m, "CNY", 1));
            Assert.Equal(HttpStatusCode.Conflict, duplicateLinkResponse.StatusCode);

            var updateLinkResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/products/{productLink.Id}",
                new ApiSupplierProductLinkSaveRequest(productLink.Id, supplier.Id, productId, "SUP-FAB-10", 13m,
                    "CNY", 14, productLink.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateLinkResponse.StatusCode);
            var updatedLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(updateLinkResponse);
            Assert.Equal("SUP-FAB-10", updatedLink.SupplierProductCode);
            Assert.Equal("供货中", updatedLink.Status);
            var staleLinkResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/products/{productLink.Id}",
                new ApiSupplierProductLinkSaveRequest(productLink.Id, supplier.Id, productId, "STALE", 9m,
                    "USD", 30, productLink.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleLinkResponse.StatusCode);
            var deactivateLinkResponse = await client.PostAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/products/{productLink.Id}/deactivate",
                new ApiSupplierLifecycleRequest(updatedLink.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, deactivateLinkResponse.StatusCode);
            var deactivatedLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(deactivateLinkResponse);
            Assert.Equal("停用", deactivatedLink.Status);
            var restoreLinkResponse = await client.PostAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/products/{productLink.Id}/restore",
                new ApiSupplierLifecycleRequest(deactivatedLink.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, restoreLinkResponse.StatusCode);
            var restoredLink = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierProductLinkDto>(restoreLinkResponse);
            Assert.Equal("供货中", restoredLink.Status);
            Assert.Single((await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierProductLinkDto>>(
                $"/api/suppliers/{supplier.Id}/products?pageNumber=1&pageSize=20"))!.Items);
            var protectedProductDeleteResponse = await client.DeleteAsync($"/api/master-data/products/{productId}");
            Assert.Equal(HttpStatusCode.Conflict, protectedProductDeleteResponse.StatusCode);
            Assert.Contains("先解除供应商供货关联", await protectedProductDeleteResponse.Content.ReadAsStringAsync());

            var createAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                    "订单复盘", 5, 4, 5, 3, "优先合作", "交付稳定，价格仍可继续协商。"));
            Assert.Equal(HttpStatusCode.Created, createAssessmentResponse.StatusCode);
            var assessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(createAssessmentResponse);
            Assert.Equal(4.25m, assessment.AverageScore);
            Assert.Equal("admin", assessment.AssessedBy);

            var invalidAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow),
                    "订单复盘", 6, 4, 5, 3, "合格", string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, invalidAssessmentResponse.StatusCode);

            var updateAssessmentResponse = await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}",
                new ApiSupplierAssessmentSaveRequest(assessment.Id, supplier.Id, assessment.AssessmentDate,
                    "订单复盘", 5, 5, 5, 4, "优先合作", "复评后交期表现提升。",
                    assessment.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateAssessmentResponse.StatusCode);
            var updatedAssessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(updateAssessmentResponse);
            Assert.Equal(4.75m, updatedAssessment.AverageScore);
            var staleAssessmentResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}",
                new ApiSupplierAssessmentSaveRequest(assessment.Id, supplier.Id, assessment.AssessmentDate,
                    "订单复盘", 1, 1, 1, 1, "观察", "过期修改", assessment.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleAssessmentResponse.StatusCode);
            Assert.Single(await client.GetFromJsonAsync<List<ApiSupplierAssessmentDto>>($"/api/suppliers/{supplier.Id}/assessments") ?? []);

            var staleConfirmResponse = await client.PostAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}/confirm?expectedVersion={assessment.VersionNumber}",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, staleConfirmResponse.StatusCode);
            var confirmResponse = await client.PostAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}/confirm?expectedVersion={updatedAssessment.VersionNumber}",
                content: null);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
            var confirmedAssessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(confirmResponse);
            Assert.Equal(SupplierAssessmentStatusCatalog.Confirmed, confirmedAssessment.Status);
            Assert.Equal("admin", confirmedAssessment.ConfirmedBy);
            Assert.NotNull(confirmedAssessment.ConfirmedAt);

            var confirmedUpdateResponse = await client.PutAsJsonAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}",
                new ApiSupplierAssessmentSaveRequest(assessment.Id, supplier.Id, assessment.AssessmentDate,
                    "订单复盘", 4, 4, 4, 4, "合格", "不得覆盖已确认记录。",
                    confirmedAssessment.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, confirmedUpdateResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(
                $"/api/suppliers/{supplier.Id}/assessments/{assessment.Id}?expectedVersion={confirmedAssessment.VersionNumber}")).StatusCode);

            var temporaryAssessmentResponse = await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/assessments",
                new ApiSupplierAssessmentSaveRequest(0, supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow),
                    "样品评估", 3, 3, 4, 4, "观察", "临时样品评价。"));
            var temporaryAssessment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierAssessmentDto>(temporaryAssessmentResponse);
            Assert.Equal(HttpStatusCode.OK,
                (await client.DeleteAsync(
                    $"/api/suppliers/{supplier.Id}/assessments/{temporaryAssessment.Id}?expectedVersion={temporaryAssessment.VersionNumber}")).StatusCode);
            Assert.Single(await client.GetFromJsonAsync<List<ApiSupplierAssessmentDto>>($"/api/suppliers/{supplier.Id}/assessments") ?? []);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
                $"/api/suppliers/{supplier.Id}/products/{productLink.Id}?expectedVersion={restoredLink.VersionNumber}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierProductLinkDto>>(
                $"/api/suppliers/{supplier.Id}/products?pageNumber=1&pageSize=20"))!.Items);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/master-data/products/{productId}")).StatusCode);

            var page = await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierDto>>("/api/suppliers/page?keyword=Factory&pageNumber=1&pageSize=10");
            Assert.Equal(1, page?.TotalCount);
            Assert.Equal(supplier.Id, Assert.Single(page!.Items).Id);

            var crmPage = await client.GetFromJsonAsync<ApiPagedResponse<ApiCrmCustomerDto>>(
                "/api/crm/customers/page?pageNumber=1&pageSize=10");
            Assert.NotNull(crmPage);
            Assert.Empty(crmPage.Items);
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
            var importResponse = await client.PostAsJsonAsync("/api/suppliers/import", new ApiSupplierImportRequest(preview.PreviewId));
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            Assert.Equal(1, (await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierImportResultDto>(importResponse)).CreatedSuppliers);

            var allSuppliers = (await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierDto>>(
                "/api/suppliers/page?pageNumber=1&pageSize=100"))!.Items;
            foreach (var item in allSuppliers)
            {
                var deactivateResponse = await client.PostAsJsonAsync(
                    $"/api/suppliers/{item.Id}/deactivate",
                    new ApiSupplierLifecycleRequest(item.VersionNumber));
                Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
            }

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

            var exportResponse = await client.GetAsync("/api/suppliers/export?status=停用");
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            byte[] workbook = await exportResponse.Content.ReadAsByteArrayAsync();
            Assert.True(workbook.Length > 1000);
            Assert.Equal((byte)'P', workbook[0]);
            Assert.Equal((byte)'K', workbook[1]);

            var contactsBeforeDelete = (await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierContactDto>>(
                $"/api/suppliers/{supplier.Id}/contacts?pageNumber=1&pageSize=20"))!.Items;
            var contactToDelete = contactsBeforeDelete.Single(item => item.Id == secondContact.Id);
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
                $"/api/suppliers/{supplier.Id}/contacts/{secondContact.Id}?expectedVersion={contactToDelete.VersionNumber}")).StatusCode);
            var suppliersBeforeDelete = (await client.GetFromJsonAsync<ApiPagedResponse<ApiSupplierDto>>(
                "/api/suppliers/page?pageNumber=1&pageSize=100"))?.Items ?? [];
            var supplierBeforeDelete = suppliersBeforeDelete.Single(item => item.Id == supplier.Id);
            var protectedDeleteResponse = await client.DeleteAsync(
                $"/api/suppliers/{supplier.Id}?expectedVersion={supplierBeforeDelete.VersionNumber}");
            Assert.Equal(HttpStatusCode.Conflict, protectedDeleteResponse.StatusCode);
            Assert.Contains("请改为停用", await protectedDeleteResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var disposableResponse = await client.PostAsJsonAsync("/api/suppliers",
                new ApiSupplierSaveRequest(0, "Disposable Supplier", "CN", "临时", string.Empty,
                    string.Empty, string.Empty));
            var disposable = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSupplierDto>(disposableResponse);
            var disposableContactResponse = await client.PostAsJsonAsync($"/api/suppliers/{disposable.Id}/contacts",
                new ApiSupplierContactSaveRequest(0, disposable.Id, "临时联系人", string.Empty, string.Empty,
                    string.Empty, string.Empty));
            Assert.Equal(HttpStatusCode.Created, disposableContactResponse.StatusCode);
            var hardDeleteResponse = await client.DeleteAsync(
                $"/api/suppliers/{disposable.Id}?expectedVersion={disposable.VersionNumber}");
            Assert.Equal(HttpStatusCode.OK, hardDeleteResponse.StatusCode);
            Assert.True((await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(hardDeleteResponse)).Success);

            await using (var context = CreateContext(harness.DatabasePath))
            {
                Assert.Equal("停用", (await context.SupplierCompanies.SingleAsync(item => item.Id == supplier.Id)).Status);
                Assert.True(await context.SupplierAssessments.AnyAsync(item => item.SupplierCompanyId == supplier.Id));
                Assert.True(await context.SupplierContacts.AnyAsync(item => item.SupplierCompanyId == supplier.Id));
                Assert.False(await context.SupplierCompanies.AnyAsync(item => item.Id == disposable.Id));
                Assert.False(await context.SupplierContacts.AnyAsync(item => item.SupplierCompanyId == disposable.Id));
            }
        }

        private static AppDbContext CreateContext(string databasePath)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(DbHelper.BuildConnectionString(databasePath))
                .Options;
            return new AppDbContext(options);
        }
    }
}
