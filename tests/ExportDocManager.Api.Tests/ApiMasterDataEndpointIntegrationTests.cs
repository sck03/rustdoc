using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public class ApiMasterDataEndpointIntegrationTests
    {
        [Fact]
        public async Task MasterDataEndpoints_ShouldSupportAuthenticatedCustomerCrudAndPersistUnderRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-master-data",
                "api-master-data.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/master-data/customers");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/customers",
                CreateCustomer("API Master Buyer"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomerDto>(createResponse);
            Assert.True(created.Id > 0);
            Assert.Equal("API Master Buyer", created.CustomerNameEN);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var getResponse = await adminClient.GetAsync($"/api/master-data/customers/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var customer = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomerDto>(getResponse);
            Assert.Equal("API Master Buyer", customer.CustomerNameEN);
            Assert.False(string.IsNullOrWhiteSpace(customer.RowVersion));

            var listResponse = await adminClient.GetAsync("/api/master-data/customers?keyword=Master");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using (var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
            {
                Assert.Contains(
                    listDocument.RootElement.EnumerateArray(),
                    item => string.Equals(
                        item.GetProperty("customerNameEN").GetString(),
                        "API Master Buyer",
                        StringComparison.Ordinal));
            }

            var updateResponse = await adminClient.PutAsJsonAsync(
                $"/api/master-data/customers/{created.Id}",
                CreateCustomer("API Master Buyer Updated", created.Id, customer.RowVersion));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomerDto>(updateResponse);
            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("API Master Buyer Updated", updated.CustomerNameEN);
            Assert.Equal("Updated from API test", updated.Notes);

            var deleteResponse = await adminClient.DeleteAsync($"/api/master-data/customers/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

            var getAfterDeleteResponse = await adminClient.GetAsync($"/api/master-data/customers/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
        }

        private static ApiCustomerDto CreateCustomer(
            string name,
            int id = 0,
            string rowVersion = "")
        {
            return new ApiCustomerDto(
                id,
                name,
                name,
                "API Notify Party",
                "1 Master Data Road",
                "2 Notify Road",
                "API Contact",
                "13800000000",
                "api-master@example.com",
                "TAX-API-001",
                id == 0 ? "Created from API test" : "Updated from API test",
                rowVersion);
        }

        [Fact]
        public async Task HsCodeImportEndpoints_ShouldImportExplicitPathAndUploadedWorkbookUnderRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-hs-code-import",
                "api-hs-code-import.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousImportResponse = await anonymousClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/import-path",
                new ApiHsCodeImportPathRequest(Path.Combine(harness.DataRoot, "Imports", "anonymous.xlsx")));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousImportResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            string importPath = Path.Combine(harness.DataRoot, "Imports", "hs-codes-path.xlsx");
            CreateHsCodeWorkbook(
                importPath,
                ("62019300", "化纤制夹克", "035 千克", "13%", "A", "M", "品牌类型; 出口享惠情况", "Synthetic jackets"));

            var pathImportResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/import-path",
                new ApiHsCodeImportPathRequest(importPath));
            Assert.Equal(HttpStatusCode.OK, pathImportResponse.StatusCode);
            var pathImport = await ApiIntegrationTestHarness.ReadJsonAsync<ApiHsCodeImportResponse>(pathImportResponse);
            Assert.True(pathImport.Success);
            Assert.Equal("hs-codes-path.xlsx", pathImport.FileName);
            Assert.True(pathImport.TotalCount >= 1);
            Assert.Contains("运行数据根数据库", pathImport.StoragePolicy, StringComparison.Ordinal);

            using var uploadStream = new MemoryStream();
            CreateHsCodeWorkbook(
                uploadStream,
                ("64039900", "其他鞋靴", "双", "13%", "B", string.Empty, "材质; 用途", "Other footwear"));
            uploadStream.Position = 0;
            using var uploadContent = new ByteArrayContent(uploadStream.ToArray());
            uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var uploadResponse = await adminClient.PostAsync(
                "/api/master-data/hs-codes/import-upload?fileName=hs-codes-upload.xlsx",
                uploadContent);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
            var uploadImport = await ApiIntegrationTestHarness.ReadJsonAsync<ApiHsCodeImportResponse>(uploadResponse);
            Assert.True(uploadImport.Success);
            Assert.Equal("hs-codes-upload.xlsx", uploadImport.FileName);
            Assert.True(uploadImport.TotalCount >= 2);
            Assert.Contains("Cache/HsCodeImports", uploadImport.StoragePolicy, StringComparison.Ordinal);

            var listResponse = await adminClient.GetAsync("/api/master-data/hs-codes?keyword=64039900");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiHsCodeDto>>(listResponse);
            var imported = Assert.Single(page.Items);
            Assert.Equal("64039900", imported.NormalizedCode);
            Assert.Equal("其他鞋靴", imported.Name);
            Assert.Equal("双", imported.Unit);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            string uploadCacheRoot = Path.Combine(harness.DataRoot, "Cache", "HsCodeImports");
            if (Directory.Exists(uploadCacheRoot))
            {
                Assert.Empty(Directory.GetDirectories(uploadCacheRoot));
            }
        }

        [Fact]
        public async Task HsCodeClearAllEndpoint_ShouldRequireConfirmationAndClearCurrentRuntimeDatabaseOnly()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-hs-code-clear-all",
                "api-hs-code-clear-all.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousClearResponse = await anonymousClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/clear-all",
                new ApiHsCodeClearAllRequest("CLEAR"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousClearResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createFirstResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes",
                CreateHsCode("8504401400", "稳压电源"));
            Assert.Equal(HttpStatusCode.Created, createFirstResponse.StatusCode);

            var createSecondResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes",
                CreateHsCode("9403200000", "金属家具"));
            Assert.Equal(HttpStatusCode.Created, createSecondResponse.StatusCode);

            var invalidConfirmResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/clear-all",
                new ApiHsCodeClearAllRequest("清空"));
            Assert.Equal(HttpStatusCode.BadRequest, invalidConfirmResponse.StatusCode);

            var beforeClearResponse = await adminClient.GetAsync("/api/master-data/hs-codes?pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, beforeClearResponse.StatusCode);
            var beforeClear = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiHsCodeDto>>(beforeClearResponse);
            Assert.True(beforeClear.TotalCount >= 2);

            var clearResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/clear-all",
                new ApiHsCodeClearAllRequest("CLEAR"));
            Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
            var command = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(clearResponse);
            Assert.True(command.Success);
            Assert.Contains("本地HS编码库已清空", command.Message, StringComparison.Ordinal);

            var afterClearResponse = await adminClient.GetAsync("/api/master-data/hs-codes?pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, afterClearResponse.StatusCode);
            var afterClear = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiHsCodeDto>>(afterClearResponse);
            Assert.Equal(0, afterClear.TotalCount);
            Assert.Empty(afterClear.Items);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task HsCodeBatchDeleteEndpoint_ShouldDeleteSelectedLocalRowsOnly()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-hs-code-batch-delete",
                "api-hs-code-batch-delete.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousDeleteResponse = await anonymousClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/delete-batch",
                new ApiHsCodeBatchDeleteRequest(new[] { 1 }));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDeleteResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var firstResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes",
                CreateHsCode("3926909090", "塑料制品"));
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            var first = await ApiIntegrationTestHarness.ReadJsonAsync<ApiHsCodeDto>(firstResponse);

            var secondResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes",
                CreateHsCode("4202920000", "箱包"));
            Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
            var second = await ApiIntegrationTestHarness.ReadJsonAsync<ApiHsCodeDto>(secondResponse);

            var thirdResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes",
                CreateHsCode("4821100000", "纸标签"));
            Assert.Equal(HttpStatusCode.Created, thirdResponse.StatusCode);
            var third = await ApiIntegrationTestHarness.ReadJsonAsync<ApiHsCodeDto>(thirdResponse);

            var invalidDeleteResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/delete-batch",
                new ApiHsCodeBatchDeleteRequest(Array.Empty<int>()));
            Assert.Equal(HttpStatusCode.BadRequest, invalidDeleteResponse.StatusCode);

            var deleteResponse = await adminClient.PostAsJsonAsync(
                "/api/master-data/hs-codes/delete-batch",
                new ApiHsCodeBatchDeleteRequest(new[] { first.Id, second.Id, second.Id, -1 }));
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            var command = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(deleteResponse);
            Assert.True(command.Success);
            Assert.Contains("2 条", command.Message, StringComparison.Ordinal);

            var listResponse = await adminClient.GetAsync("/api/master-data/hs-codes?pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiHsCodeDto>>(listResponse);
            var remaining = Assert.Single(page.Items);
            Assert.Equal(third.Id, remaining.Id);
            Assert.Equal("4821100000", remaining.NormalizedCode);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static ApiHsCodeDto CreateHsCode(string code, string name)
        {
            return new ApiHsCodeDto(
                0,
                code,
                string.Empty,
                name,
                "个",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty);
        }

        private static void CreateHsCodeWorkbook(string path, params (string Code, string Name, string Unit, string Rebate, string Supervision, string Inspection, string Elements, string Description)[] rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var workbook = BuildHsCodeWorkbook(rows);
            workbook.SaveAs(path);
        }

        private static void CreateHsCodeWorkbook(Stream stream, params (string Code, string Name, string Unit, string Rebate, string Supervision, string Inspection, string Elements, string Description)[] rows)
        {
            using var workbook = BuildHsCodeWorkbook(rows);
            workbook.SaveAs(stream);
        }

        private static XLWorkbook BuildHsCodeWorkbook(params (string Code, string Name, string Unit, string Rebate, string Supervision, string Inspection, string Elements, string Description)[] rows)
        {
            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("HS");
            worksheet.Cell(1, 1).Value = "HS编码";
            worksheet.Cell(1, 2).Value = "名称";
            worksheet.Cell(1, 3).Value = "单位";
            worksheet.Cell(1, 4).Value = "退税率";
            worksheet.Cell(1, 5).Value = "监管条件";
            worksheet.Cell(1, 6).Value = "检验检疫";
            worksheet.Cell(1, 7).Value = "申报要素";
            worksheet.Cell(1, 8).Value = "描述";

            for (int index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                int rowNumber = index + 2;
                worksheet.Cell(rowNumber, 1).Value = row.Code;
                worksheet.Cell(rowNumber, 2).Value = row.Name;
                worksheet.Cell(rowNumber, 3).Value = row.Unit;
                worksheet.Cell(rowNumber, 4).Value = row.Rebate;
                worksheet.Cell(rowNumber, 5).Value = row.Supervision;
                worksheet.Cell(rowNumber, 6).Value = row.Inspection;
                worksheet.Cell(rowNumber, 7).Value = row.Elements;
                worksheet.Cell(rowNumber, 8).Value = row.Description;
            }

            return workbook;
        }
    }
}
