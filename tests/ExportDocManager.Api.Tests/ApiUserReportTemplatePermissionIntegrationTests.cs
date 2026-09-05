using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests;

public sealed class ApiUserReportTemplatePermissionIntegrationTests
{
    [Fact]
    public async Task CreateAndClone_ShouldUseIndependentCapabilitiesAndServerResolvedSources()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync(
            "report-template-create-clone-permissions",
            "report-template-create-clone-permissions.db");
        using var anonymous = harness.CreateClient();
        var adminLogin = await harness.LoginAsync(anonymous, "admin", string.Empty);
        using var admin = harness.CreateClient(adminLogin.AccessToken);

        string builtInDirectory = Path.Combine(harness.AppRoot, "Templates", "Export");
        Directory.CreateDirectory(builtInDirectory);
        string builtInPath = Path.Combine(builtInDirectory, "permission_clone_source.html");
        const string expectedBuiltInContent = "<html>SERVER BUILT-IN {{ Invoice.InvoiceNo }}</html>";
        await File.WriteAllTextAsync(builtInPath, expectedBuiltInContent);
        var available = await admin.GetFromJsonAsync<ApiReportTemplateDto[]>(
            "/api/reports/templates?reportType=ExportDocument");
        var builtIn = Assert.Single(available!
            .Where(template => template.TemplatePath.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            .Take(1));
        Assert.EndsWith("permission_clone_source.html", builtIn.TemplatePath, StringComparison.Ordinal);

        using var designClient = await CreateUserWithPermissionsAsync(
            harness,
            anonymous,
            admin,
            "report-designer-only",
            "designer-pass",
            [PermissionAction.View, PermissionAction.Design]);
        var createResponse = await designClient.PostAsJsonAsync(
            "/api/reports/user-templates",
            new ApiUserReportTemplateCreateRequest(
                "ExportDocument",
                "空白设计草稿"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserReportTemplateDto>(createResponse);
        Assert.Contains("EXPORTDOC_REPORT_DESIGNER_SCHEMA", created.ContentHtml, StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await designClient.PostAsJsonAsync(
                "/api/reports/user-templates/clone",
                new ApiUserReportTemplateCloneRequest(
                    "ExportDocument",
                    "无权复制",
                    builtIn.TemplatePath))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await designClient.GetAsync(
                "/api/reports/user-templates?reportType=ExportDocument&includeArchived=true")).StatusCode);

        using var cloneClient = await CreateUserWithPermissionsAsync(
            harness,
            anonymous,
            admin,
            "report-cloner-only",
            "cloner-pass",
            [PermissionAction.View, PermissionAction.Clone]);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await cloneClient.PostAsJsonAsync(
                "/api/reports/user-templates",
                new ApiUserReportTemplateCreateRequest(
                    "ExportDocument",
                    "任意正文",
                    "<html>ATTACKER CONTENT</html>"))).StatusCode);

        var cloneResponse = await cloneClient.PostAsJsonAsync(
            "/api/reports/user-templates/clone",
            new
            {
                reportType = "ExportDocument",
                name = "正式内置模板副本",
                sourceTemplatePath = builtIn.TemplatePath,
                contentHtml = "<html>ATTACKER CONTENT</html>"
            });
        Assert.Equal(HttpStatusCode.Created, cloneResponse.StatusCode);
        var cloned = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserReportTemplateDto>(cloneResponse);
        Assert.Equal(expectedBuiltInContent, cloned.ContentHtml);
        Assert.DoesNotContain("ATTACKER CONTENT", cloned.ContentHtml, StringComparison.Ordinal);
        Assert.False(cloned.CanEdit);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await cloneClient.PostAsJsonAsync(
                "/api/reports/user-templates/clone",
                new ApiUserReportTemplateCloneRequest(
                    "ExportDocument",
                    "不存在来源",
                    "builtin:Export/not-in-template-catalog.html"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await cloneClient.PostAsJsonAsync(
                "/api/reports/user-templates/clone",
                new ApiUserReportTemplateCloneRequest(
                    "ExportDocument",
                    "禁止用户文件来源",
                    "user:Export/untrusted.html"))).StatusCode);

        using var exportClient = await CreateUserWithPermissionsAsync(
            harness,
            anonymous,
            admin,
            "report-exporter-only",
            "exporter-pass",
            [PermissionAction.View, PermissionAction.Export]);
        var exportResponse = await exportClient.PostAsync(
            "/api/reports/templates/package/download",
            content: null);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal("application/octet-stream", exportResponse.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpClient> CreateUserWithPermissionsAsync(
        ApiIntegrationTestHarness harness,
        HttpClient anonymous,
        HttpClient admin,
        string username,
        string password,
        string[] actions)
    {
        var templateResponse = await admin.PostAsJsonAsync(
            "/api/permission-templates",
            new
            {
                code = $"Template-{username}",
                name = $"报表权限 {username}",
                description = "报表模板动作隔离集成测试",
                isActive = true,
                grants = actions.Select(action => new
                {
                    resourceKey = PermissionResourceCatalog.ReportTemplates,
                    action,
                    dataScope = action == PermissionAction.View
                            ? PermissionDataScope.All
                            : PermissionDataScope.Own
                })
                    .Append(new
                    {
                        resourceKey = PermissionModuleCatalog.DocumentInvoices,
                        action = PermissionAction.View,
                        dataScope = PermissionDataScope.Own
                    })
            });
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var template = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPermissionTemplateDto>(templateResponse);

        var userResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username,
                fullName = username,
                role = UserRoleCatalog.User,
                permissionTemplateId = template.Id,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = password
            });
        Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);
        var login = await harness.LoginAsync(anonymous, username, password);
        return harness.CreateClient(login.AccessToken);
    }
}
