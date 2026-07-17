using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiSingleWindowEndpointIntegrationTests
    {
        [Fact]
        public async Task SingleWindowEndpoints_ShouldRequireAuthenticationAndUseRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-single-window",
                "api-single-window.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousCatalogResponse = await anonymousClient.GetAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousCatalogResponse.StatusCode);
            var anonymousIssuingAuthorityResponse = await anonymousClient.GetAsync("/api/single-window/coo/issuing-authorities");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousIssuingAuthorityResponse.StatusCode);
            var anonymousEditorOptionsResponse = await anonymousClient.GetAsync("/api/single-window/coo/editor-options");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEditorOptionsResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var catalogResponse = await adminClient.GetAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            using (var catalogDocument = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync()))
            {
                var root = catalogDocument.RootElement;
                Assert.True(root.TryGetProperty("catalog", out var catalog));
                Assert.True(catalog.TryGetProperty("currencies", out var currencies));
                Assert.True(currencies.GetArrayLength() >= 0);
                Assert.DoesNotContain(@"C:\", root.GetProperty("storagePolicy").GetString() ?? string.Empty);
                Assert.Contains("运行数据根", root.GetProperty("storagePolicy").GetString() ?? string.Empty);
            }

            var issuingAuthorityResponse = await adminClient.GetAsync("/api/single-window/coo/issuing-authorities");
            Assert.Equal(HttpStatusCode.OK, issuingAuthorityResponse.StatusCode);
            var issuingAuthorities = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowIssuingAuthorityCatalogResponse>(issuingAuthorityResponse);
            Assert.NotEmpty(issuingAuthorities.Options);
            Assert.Contains(issuingAuthorities.Options, item =>
                item.Code == "3101" &&
                item.Label.Contains("宁波海关", StringComparison.Ordinal) &&
                item.ApplicationAddress == "NINGBO, CHINA");
            Assert.DoesNotContain(@"C:\", issuingAuthorities.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("只读候选", issuingAuthorities.StoragePolicy, StringComparison.Ordinal);

            var editorOptionsResponse = await adminClient.GetAsync("/api/single-window/coo/editor-options");
            Assert.Equal(HttpStatusCode.OK, editorOptionsResponse.StatusCode);
            var editorOptions = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomsCooEditorOptionsResponse>(editorOptionsResponse);
            Assert.Contains(editorOptions.CertTypeOptions, item => item.Value == "C" && item.Label.Contains("一般原产地证", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CertTypeOptions, item => item.Value == "G" && item.Label.Contains("普惠制原产地证", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CertTypeOptions, item => item.Value == "A" && item.Label.Contains("中国-澳大利亚证书", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CertTypeOptions, item => item.Value == "F" && item.Label.Contains("中国-智利证书", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CertTypeOptions, item => item.Value == "RC" && item.Label.Contains("RCEP", StringComparison.Ordinal));
            Assert.Contains(editorOptions.ApplyTypeOptions, item => item.Value == "1" && item.Label.Contains("申报", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CertStatusOptions, item => item.Value == "2" && item.Label.Contains("重发证", StringComparison.Ordinal));
            Assert.Contains(editorOptions.CooTradeModeOptions, item => item.Value == "1" && item.Label.Contains("一般贸易", StringComparison.Ordinal));
            Assert.DoesNotContain(editorOptions.CooTradeModeOptions, item => item.Value == "0110");
            Assert.Contains(editorOptions.CurrencyOptions, item => item.Value == "USD" && item.Label.Contains("美元", StringComparison.Ordinal));
            Assert.DoesNotContain(editorOptions.CurrencyOptions, item => item.Value == "502");
            Assert.Contains(editorOptions.OriginCriteriaOptionSets, set =>
                set.CertType == "RC" && set.Options.Any(option => option.Value == "RVC"));
            Assert.Contains(editorOptions.OriginCriteriaOptionSets, set =>
                set.CertType == "F" &&
                set.Options.Any(option => option.Value == "WO") &&
                set.Options.Any(option => option.Value == "WP") &&
                set.Options.Any(option => option.Value == "PSR"));
            Assert.Contains(editorOptions.PackUnitOptions, item => item.Value == "CTN");
            Assert.DoesNotContain(@"C:\", editorOptions.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("只读候选", editorOptions.StoragePolicy, StringComparison.Ordinal);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var operationCenterResponse = await adminClient.GetAsync(
                "/api/single-window/operation-center?pageNumber=1&pageSize=5");
            Assert.Equal(HttpStatusCode.OK, operationCenterResponse.StatusCode);
            using (var operationCenterDocument = JsonDocument.Parse(await operationCenterResponse.Content.ReadAsStringAsync()))
            {
                var root = operationCenterDocument.RootElement;
                Assert.Equal(1, root.GetProperty("pageNumber").GetInt32());
                Assert.Equal(5, root.GetProperty("pageSize").GetInt32());
                Assert.True(root.TryGetProperty("rows", out var rows));
                Assert.Equal(JsonValueKind.Array, rows.ValueKind);
            }
        }

        [Fact]
        public async Task SingleWindowReferenceCatalogEndpoints_ShouldSaveOverrideUnderRuntimeDataRootForAdminsOnly()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-single-window-reference-catalog",
                "api-single-window-reference-catalog.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "sw-catalog-operator",
                fullName = "Single Window Catalog Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "sw-catalog-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);

            var catalogResponse = await adminClient.GetAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalogPayload = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogResponse>(catalogResponse);
            var catalog = BuildMinimalReferenceCatalog(catalogPayload.Catalog);

            var operatorSaveResponse = await operatorClient.PutAsJsonAsync(
                "/api/single-window/reference-catalog",
                new ApiSingleWindowReferenceCatalogSaveRequest(catalog));
            Assert.Equal(HttpStatusCode.Forbidden, operatorSaveResponse.StatusCode);

            var invalidDuplicateCatalog = WithCountries(
                BuildMinimalReferenceCatalog(catalogPayload.Catalog),
                [
                    new SingleWindowReferenceCountryEntry
                    {
                        Code = "999",
                        EnglishName = "Test A",
                        ChineseName = "测试A"
                    },
                    new SingleWindowReferenceCountryEntry
                    {
                        Code = "999",
                        EnglishName = "Test B",
                        ChineseName = "测试B"
                    }
                ]);
            var invalidResponse = await adminClient.PutAsJsonAsync(
                "/api/single-window/reference-catalog",
                new ApiSingleWindowReferenceCatalogSaveRequest(invalidDuplicateCatalog));
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

            var saveResponse = await adminClient.PutAsJsonAsync(
                "/api/single-window/reference-catalog",
                new ApiSingleWindowReferenceCatalogSaveRequest(catalog));
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
            var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogSaveResponse>(saveResponse);
            Assert.True(saved.Success);
            Assert.Contains("覆盖文件已保存", saved.Message, StringComparison.Ordinal);
            Assert.Contains(saved.Catalog.Countries, item => item.Code == "999");

            string overridePath = Path.Combine(harness.DataRoot, "SingleWindow", "singlewindow_reference_catalogs.override.json");
            Assert.True(File.Exists(overridePath));
            Assert.StartsWith(Path.Combine(harness.DataRoot, "SingleWindow"), Path.GetDirectoryName(Path.GetFullPath(overridePath)) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(harness.AppRoot, "SingleWindow", "singlewindow_reference_catalogs.override.json")));

            var getAfterSaveResponse = await adminClient.GetAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.OK, getAfterSaveResponse.StatusCode);
            var afterSave = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogResponse>(getAfterSaveResponse);
            Assert.Contains(afterSave.Catalog.Countries, item => item.Code == "999" && item.Aliases.Contains("TEST-COUNTRY"));

            var operatorResetResponse = await operatorClient.DeleteAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.Forbidden, operatorResetResponse.StatusCode);

            var resetResponse = await adminClient.DeleteAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            var reset = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogSaveResponse>(resetResponse);
            Assert.True(reset.Success);
            Assert.False(File.Exists(overridePath));
        }

        [Fact]
        public async Task SingleWindowReferenceCatalogImportEndpoints_ShouldImportJsonAndPreviewExcelWithoutArbitraryPaths()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-single-window-reference-catalog-imports",
                "api-single-window-reference-catalog-imports.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "sw-catalog-import-operator",
                fullName = "Single Window Catalog Import Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "sw-catalog-import-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);

            var catalogResponse = await adminClient.GetAsync("/api/single-window/reference-catalog");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalogPayload = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogResponse>(catalogResponse);
            var catalog = BuildMinimalReferenceCatalog(catalogPayload.Catalog);
            string json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var operatorImportResponse = await operatorClient.PostAsync(
                "/api/single-window/reference-catalog/import-json",
                new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Forbidden, operatorImportResponse.StatusCode);

            var importResponse = await adminClient.PostAsync(
                "/api/single-window/reference-catalog/import-json",
                new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            var imported = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogSaveResponse>(importResponse);
            Assert.True(imported.Success);
            Assert.Contains("JSON", imported.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(imported.Catalog.Countries, item => item.Code == "999");

            string overridePath = Path.Combine(harness.DataRoot, "SingleWindow", "singlewindow_reference_catalogs.override.json");
            Assert.True(File.Exists(overridePath));

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("导入词典");
            worksheet.Cell(3, 1).Value = "代码";
            worksheet.Cell(3, 2).Value = "英文名";
            worksheet.Cell(3, 3).Value = "中文名";
            worksheet.Cell(3, 4).Value = "别名";
            worksheet.Cell(4, 1).Value = "888";
            worksheet.Cell(4, 2).Value = "Excel Country";
            worksheet.Cell(4, 3).Value = "Excel国家";
            worksheet.Cell(4, 4).Value = "EXCEL-COUNTRY; Excel Alias";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var excelContent = new ByteArrayContent(stream.ToArray());
            excelContent.Headers.ContentType = new("application/octet-stream");
            var excelPreviewResponse = await adminClient.PostAsync(
                "/api/single-window/reference-catalog/excel/preview?catalogKey=countries&fileName=countries.xlsx&headerRowNumber=3&dataStartRowNumber=4",
                excelContent);
            Assert.Equal(HttpStatusCode.OK, excelPreviewResponse.StatusCode);
            var excelPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSingleWindowReferenceCatalogExcelImportPreviewResponse>(excelPreviewResponse);
            Assert.True(excelPreview.Success);
            Assert.Equal("countries", excelPreview.CatalogKey);
            Assert.Equal("导入词典", excelPreview.SheetName);
            Assert.Equal(3, excelPreview.HeaderRowNumber);
            Assert.Equal(4, excelPreview.DataStartRowNumber);
            Assert.Equal(1, excelPreview.RowCount);
            Assert.Contains(excelPreview.SheetNames, item => item == "导入词典");
            Assert.Contains(excelPreview.ColumnMappings, item => item.FieldKey == "code" && item.ColumnNumber == 1);
            Assert.Contains(excelPreview.Catalog.Countries, item =>
                item.Code == "888" &&
                item.EnglishName == "Excel Country" &&
                item.Aliases.Contains("EXCEL-COUNTRY"));

            string overrideJson = await File.ReadAllTextAsync(overridePath);
            Assert.DoesNotContain("Excel Country", overrideJson, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "SingleWindow"),
                Path.GetDirectoryName(Path.GetFullPath(overridePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CustomsCooProducerProfileEndpoints_ShouldSupportCrudAndPersistUnderRuntimeDatabase()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-single-window-producer-profiles",
                "api-single-window-producer-profiles.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/single-window/coo/producer-profiles");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var invalidResponse = await adminClient.PostAsJsonAsync(
                "/api/single-window/coo/producer-profiles",
                new ApiCustomsCooProducerProfileSaveRequest(new ApiCustomsCooProducerProfileInputDto
                {
                    ProducerEmail = "invalid-email"
                }));
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/single-window/coo/producer-profiles",
                new ApiCustomsCooProducerProfileSaveRequest(new ApiCustomsCooProducerProfileInputDto
                {
                    CiqRegNo = "91330200test",
                    PrdcEtpsName = "Ningbo Maker",
                    PrdcEtpsConcEr = "Amy",
                    PrdcEtpsTel = "0574-1111",
                    Producer = "RCEP producer text",
                    ProducerTel = "0574-2222",
                    ProducerFax = "0574-3333",
                    ProducerEmail = "maker@example.com",
                    ProducerSertFlag = "Y",
                    LastInvoiceNo = "INV-PROFILE-1",
                    LastContractNo = "CON-PROFILE-1",
                    LastSourceStyleNo = "STYLE-PROFILE-1"
                }));
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomsCooProducerProfileSaveResponse>(createResponse);
            Assert.True(created.Success);
            Assert.True(created.Id > 0);
            Assert.Equal("91330200TEST", created.Profile.CiqRegNo);
            Assert.Equal("Ningbo Maker", created.Profile.PrdcEtpsName);
            Assert.Equal("Y", created.Profile.ProducerSertFlag);
            Assert.Contains("运行数据根数据库", created.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销单据", created.StoragePolicy, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\", created.StoragePolicy, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var searchResponse = await adminClient.GetAsync("/api/single-window/coo/producer-profiles?keyword=maker");
            Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
            var search = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomsCooProducerProfileListResponse>(searchResponse);
            Assert.Equal(1, search.TotalCount);
            Assert.Equal(created.Id, Assert.Single(search.Items).Id);
            Assert.Contains("COO", search.StoragePolicy, StringComparison.Ordinal);

            var updateProfile = created.Profile;

            var updateResponse = await adminClient.PutAsJsonAsync(
                $"/api/single-window/coo/producer-profiles/{created.Id}",
                new ApiCustomsCooProducerProfileSaveRequest(new ApiCustomsCooProducerProfileInputDto
                {
                    CiqRegNo = updateProfile.CiqRegNo,
                    PrdcEtpsName = updateProfile.PrdcEtpsName,
                    PrdcEtpsConcEr = updateProfile.PrdcEtpsConcEr,
                    PrdcEtpsTel = "0574-9999",
                    Producer = updateProfile.Producer,
                    ProducerTel = updateProfile.ProducerTel,
                    ProducerFax = updateProfile.ProducerFax,
                    ProducerEmail = "updated@example.com",
                    ProducerSertFlag = "N",
                    LastInvoiceNo = updateProfile.LastInvoiceNo,
                    LastContractNo = updateProfile.LastContractNo,
                    LastSourceStyleNo = updateProfile.LastSourceStyleNo
                }));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomsCooProducerProfileSaveResponse>(updateResponse);
            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("0574-9999", updated.Profile.PrdcEtpsTel);
            Assert.Equal("updated@example.com", updated.Profile.ProducerEmail);
            Assert.Equal("N", updated.Profile.ProducerSertFlag);

            var getResponse = await adminClient.GetAsync($"/api/single-window/coo/producer-profiles/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var detail = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomsCooProducerProfileResponse>(getResponse);
            Assert.Equal("Ningbo Maker", detail.Profile.PrdcEtpsName);
            Assert.Contains("CustomsCooProducerProfiles", detail.StoragePolicy, StringComparison.Ordinal);

            var deleteResponse = await adminClient.DeleteAsync($"/api/single-window/coo/producer-profiles/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            var deleteResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(deleteResponse);
            Assert.True(deleteResult.Success);

            var getAfterDeleteResponse = await adminClient.GetAsync($"/api/single-window/coo/producer-profiles/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
        }

        private static SingleWindowReferenceCatalogModel BuildMinimalReferenceCatalog(
            SingleWindowReferenceCatalogModel source)
        {
            return new SingleWindowReferenceCatalogModel
            {
                Countries =
                [
                    ..(source.Countries ?? []).Take(2),
                    new SingleWindowReferenceCountryEntry
                    {
                        Code = "999",
                        EnglishName = "Test Country",
                        ChineseName = "测试国家",
                        Aliases = ["TEST-COUNTRY"]
                    }
                ],
                AcdCountries = EnsureAtLeastOne(
                    source.AcdCountries,
                    new SingleWindowReferenceAcdCountryEntry
                    {
                        Code = "998",
                        ChineseName = "测试地区",
                        EnglishName = "Test Area",
                        Aliases = ["TEST-AREA"]
                    }),
                Currencies = EnsureAtLeastOne(
                    source.Currencies,
                    new SingleWindowReferenceCurrencyEntry
                    {
                        Code = "997",
                        AcdCode = "997",
                        AlphaCode = "TST",
                        Aliases = ["TEST-CURRENCY"]
                    }),
                AcdTradeModes = EnsureAtLeastOne(
                    source.AcdTradeModes,
                    new SingleWindowReferenceAcdTradeModeEntry
                    {
                        Code = "9999",
                        Name = "测试贸易",
                        Description = "Test trade mode",
                        Aliases = ["TEST-TRADE"]
                    }),
                TransportModes = EnsureAtLeastOne(
                    source.TransportModes,
                    new SingleWindowReferenceTransportModeEntry
                    {
                        Value = "测试运输",
                        Aliases = ["TEST-TRANSPORT"]
                    }),
                Ports = EnsureAtLeastOne(
                    source.Ports,
                    new SingleWindowReferencePortEntry
                    {
                        Value = "测试港",
                        Aliases = ["TEST-PORT"]
                    })
            };
        }

        private static SingleWindowReferenceCatalogModel WithCountries(
            SingleWindowReferenceCatalogModel source,
            IReadOnlyList<SingleWindowReferenceCountryEntry> countries)
        {
            return new SingleWindowReferenceCatalogModel
            {
                Countries = countries,
                AcdCountries = source.AcdCountries,
                Currencies = source.Currencies,
                AcdTradeModes = source.AcdTradeModes,
                TransportModes = source.TransportModes,
                Ports = source.Ports
            };
        }

        private static IReadOnlyList<T> EnsureAtLeastOne<T>(IEnumerable<T> source, T fallback)
        {
            var rows = (source ?? []).Take(2).ToList();
            if (rows.Count == 0)
            {
                rows.Add(fallback);
            }

            return rows;
        }
    }
}
