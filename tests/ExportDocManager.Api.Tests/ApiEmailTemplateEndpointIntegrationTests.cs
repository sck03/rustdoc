using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiEmailTemplateEndpointIntegrationTests
    {
        [Fact]
        public async Task EmailTemplateEndpoints_ShouldSupportOwnedCrudVariablesAndSafePreview()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync("email-templates", "email-templates.db");
            using var anonymous = harness.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/email-templates")).StatusCode);

            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);
            var variables = await client.GetFromJsonAsync<List<ApiEmailTemplateVariableDto>>("/api/email-templates/variables");
            Assert.Contains(variables!, item => item.Token == "{{CustomerName}}");

            var createResponse = await client.PostAsJsonAsync("/api/email-templates", new ApiEmailTemplateSaveRequest(
                0, "首次报价", "报价", "Hello {{CustomerName}}", "<p>Dear {{ContactName}}, {{Unknown}}</p>", true, true));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var template = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(createResponse);
            Assert.Equal(1, template.VersionNumber);

            var duplicateResponse = await client.PostAsJsonAsync("/api/email-templates", new ApiEmailTemplateSaveRequest(
                0, "首次报价", "报价", "Duplicate", "Duplicate", true, false));
            Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

            var list = await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates?keyword=报价&category=报价");
            Assert.Equal(template.Id, Assert.Single(list!).Id);
            Assert.True(template.IsShared);
            Assert.True(template.CanEdit);

            var previewResponse = await client.PostAsJsonAsync("/api/email-templates/preview", new ApiEmailTemplatePreviewRequest(
                template.Subject, template.BodyHtml, new Dictionary<string, string>
                {
                    ["CustomerName"] = "<Acme>",
                    ["ContactName"] = "Alice & Bob"
                }));
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplatePreviewDto>(previewResponse);
            Assert.Equal("Hello <Acme>", preview.Subject);
            Assert.Contains("Alice &amp; Bob", preview.BodyHtml, StringComparison.Ordinal);
            Assert.Contains("{{Unknown}}", preview.UnresolvedTokens);

            var updateResponse = await client.PutAsJsonAsync($"/api/email-templates/{template.Id}",
                new ApiEmailTemplateSaveRequest(template.Id, "首次报价", "报价", "Updated", "<p>Updated</p>",
                    false, true, template.VersionNumber));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(updateResponse);
            Assert.Equal(2, updated.VersionNumber);
            var staleUpdateResponse = await client.PutAsJsonAsync($"/api/email-templates/{template.Id}",
                new ApiEmailTemplateSaveRequest(template.Id, "首次报价", "报价", "Stale", "<p>Stale</p>",
                    true, true, template.VersionNumber));
            Assert.Equal(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates") ?? []);
            Assert.Single(await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates?includeInactive=true") ?? []);
            var versions = await client.GetFromJsonAsync<List<ApiEmailTemplateVersionDto>>($"/api/email-templates/{template.Id}/versions");
            Assert.Equal(new[] { 2, 1 }, versions!.Select(item => item.VersionNumber));
            var restoreResponse = await client.PostAsync($"/api/email-templates/{template.Id}/versions/1/restore", null);
            Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
            var restored = await ApiIntegrationTestHarness.ReadJsonAsync<ApiEmailTemplateDto>(restoreResponse);
            Assert.Equal(3, restored.VersionNumber);
            Assert.Equal(template.Subject, restored.Subject);
            Assert.Single(await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates") ?? []);

            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/email-templates/{template.Id}")).StatusCode);
            Assert.Empty(await client.GetFromJsonAsync<List<ApiEmailTemplateDto>>("/api/email-templates?includeInactive=true") ?? []);
        }
    }
}
