using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiUserManagementIntegrationTests
    {
        [Fact]
        public async Task PermissionTemplateEndpoints_ShouldPersistAssignmentsAndApplyOnNextLogin()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-permission-templates",
                "api-permission-templates.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var catalogResponse = await adminClient.GetAsync("/api/permission-templates");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalog = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateCatalogResponse>(catalogResponse);
            var adminTemplate = Assert.Single(catalog.Templates, template => template.Code == BuiltInPermissionTemplateCatalog.Admin);

            var immutableAdminResponse = await adminClient.PutAsJsonAsync(
                $"/api/permission-templates/{adminTemplate.Id}",
                new
                {
                    id = adminTemplate.Id,
                    code = adminTemplate.Code,
                    name = adminTemplate.Name,
                    description = adminTemplate.Description,
                    isActive = true,
                    modules = adminTemplate.Modules
                });
            Assert.Equal(HttpStatusCode.Conflict, immutableAdminResponse.StatusCode);

            var createTemplateResponse = await adminClient.PostAsJsonAsync(
                "/api/permission-templates",
                new
                {
                    code = "CustomAbout",
                    name = "仅关于",
                    description = "最小只读账号",
                    isActive = true,
                    modules = new[]
                    {
                        new { moduleKey = PermissionModuleCatalog.SystemAbout, accessLevel = PermissionAccessLevel.View }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, createTemplateResponse.StatusCode);
            var createdTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateDto>(createTemplateResponse);

            var createUserResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                new
                {
                    username = "template-user",
                    fullName = "Template User",
                    role = UserRoleCatalog.User,
                    permissionTemplateId = createdTemplate.Id,
                    departmentId = string.Empty,
                    companyScope = string.Empty,
                    isActive = true,
                    resetPassword = "template-pass"
                });
            Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);
            var createdUser = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(createUserResponse);
            Assert.Equal(createdTemplate.Id, createdUser.User.PermissionTemplateId);

            var firstLogin = await harness.LoginAsync(anonymousClient, "template-user", "template-pass");
            Assert.Equal(new[] { PermissionModuleCatalog.SystemAbout }, firstLogin.User.Capabilities.EnabledModules);

            var updateTemplateResponse = await adminClient.PutAsJsonAsync(
                $"/api/permission-templates/{createdTemplate.Id}",
                new
                {
                    id = createdTemplate.Id,
                    code = createdTemplate.Code,
                    name = createdTemplate.Name,
                    description = createdTemplate.Description,
                    isActive = true,
                    modules = new[]
                    {
                        new { moduleKey = PermissionModuleCatalog.CommonEmail, accessLevel = PermissionAccessLevel.Operate }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, updateTemplateResponse.StatusCode);
            Assert.Equal(new[] { PermissionModuleCatalog.SystemAbout }, firstLogin.User.Capabilities.EnabledModules);

            var secondLogin = await harness.LoginAsync(anonymousClient, "template-user", "template-pass");
            Assert.Equal(new[] { PermissionModuleCatalog.CommonEmail }, secondLogin.User.Capabilities.EnabledModules);

            var inUseDeleteResponse = await adminClient.DeleteAsync($"/api/permission-templates/{createdTemplate.Id}");
            Assert.Equal(HttpStatusCode.Conflict, inUseDeleteResponse.StatusCode);

            var deleteUserResponse = await adminClient.DeleteAsync($"/api/users/{createdUser.User.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteUserResponse.StatusCode);
            var deleteTemplateResponse = await adminClient.DeleteAsync($"/api/permission-templates/{createdTemplate.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteTemplateResponse.StatusCode);
        }

        [Fact]
        public async Task PermissionTemplateEndpoint_ShouldExpandTechnicalDependenciesFromBusinessModules()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-permission-dependencies",
                "api-permission-dependencies.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var catalogResponse = await adminClient.GetAsync("/api/permission-templates");
            var catalog = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateCatalogResponse>(catalogResponse);
            Assert.All(
                catalog.Modules.Where(module =>
                    module.Key == PermissionModuleCatalog.DocumentReferenceData ||
                    module.Key == PermissionModuleCatalog.DocumentCustomOptions ||
                    module.Key == PermissionModuleCatalog.DocumentPaymentReports),
                module => Assert.True(module.IsTechnical));

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/permission-templates",
                new
                {
                    code = "PaymentOperator",
                    name = "付款经办",
                    description = "仅配置业务模块",
                    isActive = true,
                    modules = new[]
                    {
                        new { moduleKey = PermissionModuleCatalog.DocumentPayments, accessLevel = PermissionAccessLevel.Operate },
                        new { moduleKey = PermissionModuleCatalog.DocumentInvoices, accessLevel = PermissionAccessLevel.View },
                        new { moduleKey = PermissionModuleCatalog.SalesOpportunities, accessLevel = PermissionAccessLevel.Manage },
                        new { moduleKey = PermissionModuleCatalog.SalesSuppliers, accessLevel = PermissionAccessLevel.Operate }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateDto>(createResponse);
            var grants = created.Modules.ToDictionary(module => module.ModuleKey, module => module.AccessLevel);

            Assert.Equal(PermissionAccessLevel.Operate, grants[PermissionModuleCatalog.DocumentPayments]);
            Assert.Equal(PermissionAccessLevel.Operate, grants[PermissionModuleCatalog.DocumentPaymentReports]);
            Assert.Equal(PermissionAccessLevel.Operate, grants[PermissionModuleCatalog.DocumentCustomOptions]);
            Assert.Equal(PermissionAccessLevel.View, grants[PermissionModuleCatalog.DocumentReferenceData]);
            Assert.Equal(PermissionAccessLevel.View, grants[PermissionModuleCatalog.CommonProductReference]);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentMasterData, grants.Keys);

            var deleteResponse = await adminClient.DeleteAsync($"/api/permission-templates/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        }

        [Fact]
        public async Task FinanceTemplate_ShouldAllowFinanceModulesAndRejectHiddenWorkspaces()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-finance-template",
                "api-finance-template.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);
            var createUserResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                CreateSaveRequest("finance-template-user", UserRoleCatalog.Finance, "finance-pass"));
            Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

            var financeLogin = await harness.LoginAsync(anonymousClient, "finance-template-user", "finance-pass");
            using var financeClient = harness.CreateClient(financeLogin.AccessToken);

            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/payments?pageNumber=1&pageSize=10")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/query/invoices?pageNumber=1&pageSize=10")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/master-data/payees")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/master-data/customers")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/tools/email/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await financeClient.GetAsync("/api/reports/templates?reportType=ExportDocument")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.GetAsync("/api/reports/templates?reportType=PaymentVoucher")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await financeClient.PostAsync("/api/reports/templates/storage-check", null)).StatusCode);

            string[] forbiddenPaths =
            [
                "/api/dashboard",
                "/api/invoices",
                "/api/single-window/operation-center",
                "/api/tools/excel/template/download",
                "/api/tools/container-packing/projects",
                "/api/crm/dashboard"
            ];
            foreach (string path in forbiddenPaths)
            {
                var response = await financeClient.GetAsync(path);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{path} returned {response.StatusCode}.");
            }
        }

        [Fact]
        public async Task SalesTemplate_ShouldReadSharedProductsWithoutGrantingMasterDataMaintenance()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-sales-product-reference",
                "api-sales-product-reference.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);
            var createUserResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                CreateSaveRequest("sales-product-user", UserRoleCatalog.Sales, "sales-pass"));
            Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

            var salesLogin = await harness.LoginAsync(anonymousClient, "sales-product-user", "sales-pass");
            using var salesClient = harness.CreateClient(salesLogin.AccessToken);

            Assert.Contains(PermissionModuleCatalog.CommonProductReference, salesLogin.User.Capabilities.EnabledModules);
            Assert.Equal(HttpStatusCode.OK, (await salesClient.GetAsync("/api/master-data/products")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await salesClient.PostAsJsonAsync("/api/master-data/products", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await salesClient.GetAsync("/api/master-data/hs-codes")).StatusCode);
        }

        [Fact]
        public async Task UserManagementEndpoints_ShouldEnforceAdminAndPersistAccountsInRuntimeDatabase()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-users",
                "api-users.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            Assert.True(adminLogin.User.Capabilities.CanManageUsers);

            using var adminClient = harness.CreateClient(adminLogin.AccessToken);
            var initialUsers = await GetUsersAsync(adminClient);
            Assert.Contains(initialUsers.Users, user => user.Username == "admin");

            var missingPasswordResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                CreateSaveRequest("no-password", UserRoleCatalog.User, resetPassword: string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, missingPasswordResponse.StatusCode);

            var shortPasswordResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                CreateSaveRequest("short-password", UserRoleCatalog.User, resetPassword: "short"));
            Assert.Equal(HttpStatusCode.BadRequest, shortPasswordResponse.StatusCode);
            Assert.Contains(
                "至少需要 8 个字符",
                await shortPasswordResponse.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/users",
                CreateSaveRequest("finance-api", UserRoleCatalog.Finance, "finance-pass"));
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            string createJson = await createResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("PasswordHash", createJson, StringComparison.OrdinalIgnoreCase);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(createResponse);

            Assert.True(created.Success);
            Assert.True(created.User.Id > 0);
            Assert.Equal("finance-api", created.User.Username);
            Assert.Equal(UserRoleCatalog.Finance, created.User.Role);

            var listAfterCreate = await GetUsersAsync(adminClient);
            Assert.Contains(listAfterCreate.Users, user => user.Id == created.User.Id);

            var updateResponse = await adminClient.PutAsJsonAsync(
                $"/api/users/{created.User.Id}",
                new
                {
                    username = "finance-api",
                    fullName = "Finance Operator",
                    role = UserRoleCatalog.User,
                    departmentId = "FIN",
                    companyScope = "HQ",
                    isActive = true,
                    resetPassword = string.Empty
            });
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserSaveResponse>(updateResponse);
            Assert.Equal(created.User.Id, updated.User.Id);
            Assert.Equal(UserRoleCatalog.User, updated.User.Role);
            Assert.Equal("FIN", updated.User.DepartmentId);
            Assert.Equal("HQ", updated.User.CompanyScope);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "finance-api", "finance-pass");
            Assert.False(operatorLogin.User.Capabilities.CanManageUsers);
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var forbiddenListResponse = await operatorClient.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenListResponse.StatusCode);
            var forbiddenAuditLogResponse = await operatorClient.GetAsync("/api/audit-logs");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenAuditLogResponse.StatusCode);

            var deleteResponse = await adminClient.DeleteAsync($"/api/users/{created.User.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

            var listAfterDelete = await GetUsersAsync(adminClient);
            Assert.DoesNotContain(listAfterDelete.Users, user => user.Id == created.User.Id);
            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetFullPath(harness.DatabasePath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static object CreateSaveRequest(
            string username,
            string role,
            string resetPassword)
        {
            return new
            {
                username,
                fullName = username,
                role,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword
            };
        }

        private static async Task<ApiUserListResponse> GetUsersAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserListResponse>(response);
        }
    }
}
