using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiEmailTemplateEndpointIntegrationTests
    {
        [Fact]
        public async Task EmailTemplateEndpoints_ShouldExposeIndependentLifecycleCommands()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync("email-templates", "email-templates.db");
            using var anonymous = harness.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/email-templates")).StatusCode);

            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);
            var variables = await client.GetFromJsonAsync<List<ApiEmailTemplateVariableDto>>("/api/email-templates/variables");
            Assert.Contains(variables!, item => item.Token == "{{CustomerName}}");

            var createResponse = await client.PostAsJsonAsync(
                "/api/email-templates",
                new ApiEmailTemplateDraftRequest(
                    "首次报价",
                    "报价",
                    "Hello {{CustomerName}}",
                    "<p>Dear {{ContactName}}, {{Unknown}}</p>"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var template = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(createResponse);
            Assert.Equal(1, template.VersionNumber);
            Assert.Equal("Draft", template.Status);
            Assert.Equal("Private", template.ShareScope);

            var duplicateResponse = await client.PostAsJsonAsync(
                "/api/email-templates",
                new ApiEmailTemplateDraftRequest("首次报价", "报价", "Duplicate", "Duplicate"));
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

            var list = await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates?keyword=报价&category=报价");
            Assert.Equal(template.Id, Assert.Single(list!).Id);
            Assert.True(template.CanEdit);
            Assert.True(template.CanPublish);

            var previewResponse = await client.PostAsJsonAsync(
                "/api/email-templates/preview",
                new ApiEmailTemplatePreviewRequest(
                    template.Subject,
                    template.BodyHtml,
                    new Dictionary<string, string>
                    {
                        ["CustomerName"] = "<Acme>",
                        ["ContactName"] = "Alice & Bob"
                    }));
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplatePreviewDto>(previewResponse);
            Assert.Equal("Hello <Acme>", preview.Subject);
            Assert.Contains("Alice &amp; Bob", preview.BodyHtml, StringComparison.Ordinal);
            Assert.Contains("{{Unknown}}", preview.UnresolvedTokens);

            var publishResponse = await client.PostAsJsonAsync(
                $"/api/email-templates/{template.Id}/publish",
                new ApiEmailTemplateLifecycleRequest(template.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(publishResponse);
            Assert.Equal("Published", published.Status);

            var shareResponse = await client.PostAsJsonAsync(
                $"/api/email-templates/{template.Id}/share",
                new ApiEmailTemplateShareRequest("All", published.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, shareResponse.StatusCode);
            var shared = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(shareResponse);
            Assert.Equal("All", shared.ShareScope);

            var updateResponse = await client.PutAsJsonAsync(
                $"/api/email-templates/{template.Id}/draft",
                new ApiEmailTemplateDraftRequest(
                    "首次报价",
                    "报价",
                    "Updated",
                    "<p>Updated</p>",
                    shared.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(updateResponse);
            Assert.Equal("Draft", updated.Status);
            Assert.Equal("Private", updated.ShareScope);

            var staleUpdateResponse = await client.PutAsJsonAsync(
                $"/api/email-templates/{template.Id}/draft",
                new ApiEmailTemplateDraftRequest(
                    "首次报价",
                    "报价",
                    "Stale",
                    "<p>Stale</p>",
                    shared.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);

            var versions = await client.GetFromJsonAsync<List<ApiEmailTemplateVersionDto>>(
                $"/api/email-templates/{template.Id}/versions");
            Assert.Equal(new[] { updated.VersionNumber, shared.VersionNumber, published.VersionNumber, 1 },
                versions!.Select(item => item.VersionNumber));

            var restoreResponse = await client.PostAsJsonAsync(
                $"/api/email-templates/{template.Id}/versions/1/restore",
                new ApiEmailTemplateLifecycleRequest(updated.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
            var restored = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(restoreResponse);
            Assert.Equal("Draft", restored.Status);
            Assert.Equal("Private", restored.ShareScope);
            Assert.Equal(template.Subject, restored.Subject);

            var archiveResponse = await client.DeleteAsync(
                $"/api/email-templates/{template.Id}?expectedVersion={restored.VersionNumber}");
            Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates") ?? []);
            var archived = Assert.Single(
                await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates?includeArchived=true") ?? []);
            Assert.Equal("Archived", archived.Status);

            var viewTemplateResponse = await client.PostAsJsonAsync(
                "/api/permission-templates",
                new
                {
                    code = "EmailTemplateViewOnly",
                    name = "邮件模板只读",
                    description = "只能查看当前有效邮件模板",
                    isActive = true,
                    grants = new[]
                    {
                        new
                        {
                            resourceKey = PermissionResourceCatalog.EmailTemplates,
                            action = PermissionAction.View,
                            dataScope = PermissionDataScope.All
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, viewTemplateResponse.StatusCode);
            var viewTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateDto>(viewTemplateResponse);
            var viewUserResponse = await client.PostAsJsonAsync(
                "/api/users",
                new
                {
                    username = "email-template-viewer",
                    fullName = "Email Template Viewer",
                    role = UserRoleCatalog.User,
                    permissionTemplateId = viewTemplate.Id,
                    departmentId = string.Empty,
                    companyScope = string.Empty,
                    isActive = true,
                    resetPassword = "email-view-pass"
                });
            Assert.Equal(HttpStatusCode.OK, viewUserResponse.StatusCode);
            var viewLogin = await harness.LoginAsync(anonymous, "email-template-viewer", "email-view-pass");
            using var viewClient = harness.CreateClient(viewLogin.AccessToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await viewClient.GetAsync("/api/email-templates")).StatusCode);
            var forbiddenResponse = await viewClient.GetAsync("/api/email-templates?includeArchived=true");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            var forbiddenError = await ApiIntegrationTestHarness.ReadJsonAsync<ApiErrorResponse>(forbiddenResponse);
            Assert.Contains("恢复权限", forbiddenError.Message, StringComparison.Ordinal);
        }
    }
}
