using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Reporting;
using ExportDocManager.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests;

public sealed class ApiReportTemplateSafetyTests
{
    [Fact]
    public async Task CatalogAndHistory_ShouldReturnMetadataPagesAndAuthorizeBodyReads()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("template-metadata-pages", "templates.db",
            configureServices: services => services.AddScoped(provider => new BusinessDataAccessScope(
                new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider },
                provider.GetRequiredService<ICurrentUserContext>())));
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        using var reader = await ApiUserReportTemplatePermissionIntegrationTests.CreateUserWithPermissionsAsync(
            harness, anonymous, client, "metadata-reader", "reader-pass", [PermissionAction.View], canViewResources: false);
        const string html = "<html><body>Private body</body></html>";
        ApiUserReportTemplateDto? selected = null;
        for (int i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/reports/user-templates", new ApiUserReportTemplateCreateRequest("ExportDocument", $"Paged {i}", html));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            selected = await ApiIntegrationTestHarness.ReadJsonAsync<ApiUserReportTemplateDto>(response);
        }
        var pageResponse = await client.GetAsync("/api/reports/user-templates?reportType=ExportDocument&pageNumber=2&pageSize=1");
        var page = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<UserReportTemplateSummaryRecord>>(pageResponse);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal("Paged 1", Assert.Single(page.Items).Name);
        Assert.DoesNotContain("contentHtml", await pageResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var detail = await client.GetFromJsonAsync<ApiUserReportTemplateDto>($"/api/reports/user-templates/{selected!.Id}");
        Assert.Equal(html, detail!.ContentHtml);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/reports/user-templates/{selected.Id}")).StatusCode);
        var versionsResponse = await client.GetAsync($"/api/reports/user-templates/{selected.Id}/versions?pageSize=1");
        var versions = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiUserReportTemplateVersionDto>>(versionsResponse);
        Assert.Single(versions.Items);
        Assert.DoesNotContain("contentHtml", await versionsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var broken = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/reports/templates/v3/resources/upload?mediaType=image/png", broken)).StatusCode);
    }

    [Fact]
    public async Task Images_ShouldShareReadAuthorizationAndRetainFileTemplateReferences()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("template-image-safety", "images.db");
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var admin = harness.CreateClient(login.AccessToken);
        using var designer = await ApiUserReportTemplatePermissionIntegrationTests.CreateUserWithPermissionsAsync(
            harness, anonymous, admin, "image-designer", "designer-pass", [PermissionAction.View, PermissionAction.Design], canViewResources: true);
        using var bytes = new ByteArrayContent(RasterImageFixtures.Read("png"));
        var upload = await admin.PostAsync("/api/reports/templates/v3/resources/upload?fileName=private.png&mediaType=image/png", bytes);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var image = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateImageResourceResponse>(upload);
        string imageUrl = $"/api/reports/templates/v3/resources/{image.Id}";
        string html = ImageTemplate(image);
        var resources = await admin.GetFromJsonAsync<ApiPagedResponse<ReportTemplateImageResourceListItem>>("/api/reports/templates/v3/resources?pageSize=1");
        Assert.True(Assert.Single(resources!.Items).CanRecycle);
        Assert.False(resources.Items[0].IsReferenced);
        Assert.Empty((await designer.GetFromJsonAsync<ApiPagedResponse<ReportTemplateImageResourceListItem>>("/api/reports/templates/v3/resources"))!.Items);

        Assert.Equal(HttpStatusCode.NotFound, (await designer.GetAsync(imageUrl)).StatusCode);
        var denied = await designer.PostAsJsonAsync("/api/reports/templates/preview", new { reportType = "ExportDocument", content = html });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.DoesNotContain("data:image/png", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var preview = await admin.PostAsJsonAsync("/api/reports/templates/preview", new { reportType = "ExportDocument", content = html });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("data:image/png", await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var file = await CreateFileAsync(admin, "resource-reference.html");
        var savedResponse = await admin.PutAsJsonAsync("/api/reports/templates/content", new
        {
            reportType = "ExportDocument",
            templatePath = file.TemplatePath,
            content = html,
            expectedRevision = file.Revision
        });
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(savedResponse);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(imageUrl)).StatusCode);
        var downloaded = await designer.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.True(downloaded.Headers.CacheControl?.NoStore);
        var referenced = Assert.Single((await designer.GetFromJsonAsync<ApiPagedResponse<ReportTemplateImageResourceListItem>>("/api/reports/templates/v3/resources"))!.Items);
        Assert.True(referenced.IsReferenced);
        Assert.False(referenced.CanRecycle);
        var retained = await admin.PostAsJsonAsync("/api/reports/templates/preview", new { reportType = "ExportDocument", content = html });
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(FileUrl(saved))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(imageUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(imageUrl)).StatusCode);
    }

    [Fact]
    public async Task FileMutations_ShouldRequireTheReadRevisionAndPreserveNewerContent()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("file-template-safety", "templates.db");
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var client = harness.CreateClient(login.AccessToken);
        var original = await CreateFileAsync(client, "concurrent.html");
        const string html = "<html><body>First editor saved</body></html>";
        var firstSave = await client.PutAsJsonAsync("/api/reports/templates/content", new
        {
            reportType = "ExportDocument",
            templatePath = original.TemplatePath,
            content = html,
            expectedRevision = original.Revision
        });
        Assert.Equal(HttpStatusCode.OK, firstSave.StatusCode);
        var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(firstSave);
        Assert.NotEqual(original.Revision, saved.Revision);
        foreach (string revision in new[] { original.Revision, string.Empty })
        {
            var stale = await client.PutAsJsonAsync("/api/reports/templates/content", new
            {
                reportType = "ExportDocument",
                templatePath = saved.TemplatePath,
                content = original.Content,
                expectedRevision = revision
            });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        using var replacement = new StringContent(original.Content, Encoding.UTF8, "text/html");
        var import = await client.PostAsync($"/api/reports/templates/file/upload?reportType=ExportDocument&templatePath={Uri.EscapeDataString(saved.TemplatePath)}&fileName=import.html&expectedRevision={original.Revision}", replacement);
        Assert.Equal(HttpStatusCode.Conflict, import.StatusCode);
        var rename = await client.PostAsJsonAsync("/api/reports/templates/rename", new
        {
            reportType = "ExportDocument",
            templatePath = saved.TemplatePath,
            newTemplatePath = "renamed.html",
            expectedRevision = original.Revision
        });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(FileUrl(original))).StatusCode);
        var current = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(await client.GetAsync(
            $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(saved.TemplatePath)}"));
        Assert.Equal(html, current.Content);
        Assert.Equal(saved.Revision, current.Revision);
        var renamedDisplayName = await client.PutAsJsonAsync("/api/reports/templates/display-name", new
        {
            reportType = "ExportDocument",
            templatePath = current.TemplatePath,
            displayName = "Updated name",
            expectedRevision = current.Revision
        });
        Assert.Equal(HttpStatusCode.OK, renamedDisplayName.StatusCode);
        var renamedContent = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(renamedDisplayName);
        Assert.NotEqual(current.Revision, renamedContent.Revision);
        var staleName = await client.PutAsJsonAsync("/api/reports/templates/display-name", new
        {
            reportType = "ExportDocument",
            templatePath = current.TemplatePath,
            displayName = "Stale name",
            expectedRevision = current.Revision
        });
        Assert.Equal(HttpStatusCode.Conflict, staleName.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(FileUrl(renamedContent))).StatusCode);
    }

    private static async Task<ApiReportTemplateContentDto> CreateFileAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync("/api/reports/templates", new { reportType = "ExportDocument", templatePath = path, displayName = "Safety test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(response);
    }

    private static string FileUrl(ApiReportTemplateContentDto file) =>
        $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(file.TemplatePath)}&expectedRevision={file.Revision}";

    private static string ImageTemplate(ApiReportTemplateImageResourceResponse image)
    {
        string schema = JsonSerializer.Serialize(new
        {
            version = 3,
            astKind = "ReportDocument",
            coordinateUnit = "hundredth-mm",
            contractVersion = "3.0",
            reportType = "ExportDocument",
            page = new { size = "A4", orientation = "Portrait", widthHundredthMm = 21000, heightHundredthMm = 29700, marginTopHundredthMm = 800, marginRightHundredthMm = 800, marginBottomHundredthMm = 800, marginLeftHundredthMm = 800, fontFamily = "Arial", fontSizePt = 9 },
            grid = new { enabled = true, sizeHundredthMm = 500, snap = true },
            resources = new[] { new { id = image.Id, mediaType = image.MediaType, byteLength = image.ByteLength, sha256 = image.Sha256 } },
            layers = new[] { new { id = "body", name = "主体", role = "Body", visible = true, locked = false,
                print = new { repeatOnEveryPage = false, keepTogether = false, pinToPageBottom = false, minHeightHundredthMm = 0 },
                elements = new[] { new { id = "image", type = "Image", sourceKind = "Resource", purpose = "Image", resourceId = image.Id,
                    xHundredthMm = 1000, yHundredthMm = 1000, widthHundredthMm = 1000, heightHundredthMm = 1000,
                    rotationDeg = 0, zIndex = 0, visible = true, locked = false, style = new { }, outputEnabled = true, hideWhenSourceEmpty = true } } } }
        });
        return $"<html><body><!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA {schema} --><img data-edm-v3-resource-id=\"{image.Id}\" alt=\"image\"></body></html>";
    }
}
