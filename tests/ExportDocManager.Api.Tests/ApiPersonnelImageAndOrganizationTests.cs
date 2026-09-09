using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests;

public sealed class ApiPersonnelImageAndOrganizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4i8AAAAASUVORK5CYII=");

    [Fact]
    public async Task ImagesAndHierarchy_ShouldUseRealHttpPermissions_AndPreserveConcurrencyAndBinaryContracts()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("personnel-images-organization", "people.db",
            configureServices: ApiOfficeAccessTests.ConfigureTeamMode);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", "");
        using var admin = harness.CreateClient(login.AccessToken);
        var person = await PostAsync<PersonnelRecord>(admin, "/api/office/people", new PersonnelCreateRequest(Guid.NewGuid(), "PHOTO-001",
            OrganizationDirectoryDefaults.DepartmentCode, "人事测试", EmploymentType.FullTime, login.User.BusinessDate, false, null, null,
            new PersonnelProfile("图片示例", IdentityNumber: "11010519491231002X", IdentityValidFrom: new(2020, 1, 1), IdentityLongTerm: true)));
        int originalVersion = person.VersionNumber;
        foreach (var kind in Enum.GetValues<PersonnelImageKind>())
        {
            using var form = UploadForm(Png, "image/png", person.VersionNumber);
            using var uploaded = await admin.PostAsync($"/api/office/people/{person.Employee.Id}/images/{kind}", form);
            Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
            person = (await uploaded.Content.ReadFromJsonAsync<PersonnelRecord>(JsonOptions))!;
        }
        Assert.Equal(3, person.Images.Count);
        Assert.True(person.VersionNumber > originalVersion);
        using (var details = await admin.GetAsync($"/api/office/people/{person.Employee.Id}")) Assert.True(details.Headers.CacheControl?.NoStore);
        using (var avatar = await admin.GetAsync($"/api/office/people/{person.Employee.Id}/avatar"))
        {
            Assert.Equal(Png, await avatar.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/png", avatar.Content.Headers.ContentType?.MediaType);
            Assert.True(avatar.Headers.CacheControl?.NoStore);
            Assert.Equal("nosniff", avatar.Headers.GetValues("X-Content-Type-Options").Single());
        }
        using (var stale = await admin.DeleteAsync($"/api/office/people/{person.Employee.Id}/images/Avatar?expectedVersion={originalVersion}"))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var invalid = UploadForm("<svg/>"u8.ToArray(), "image/png", person.VersionNumber))
        using (var rejected = await admin.PostAsync($"/api/office/people/{person.Employee.Id}/images/Avatar", invalid))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var oversized = UploadForm(new byte[PersonnelImageLimits.MaxBytes + 1], "image/png", person.VersionNumber))
        using (var rejected = await admin.PostAsync($"/api/office/people/{person.Employee.Id}/images/Avatar", oversized))
            Assert.Contains(rejected.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });

        var directory = (await admin.GetFromJsonAsync<ApiOrganizationDirectoryResponse>("/api/organization-directory", JsonOptions))!;
        var root = directory.Departments.Single(item => item.Code == OrganizationDirectoryDefaults.DepartmentCode);
        var child = await PostAsync<ApiOrganizationDepartmentDto>(admin, "/api/organization-directory/departments",
            new ApiOrganizationDepartmentSaveRequest("PHOTO-DEPT", root.CompanyCode, "下级部门", true, ParentCode: root.Code, ManagerEmployeeId: person.Employee.Id));
        Assert.Equal(root.Code, child.ParentCode);
        Assert.Equal(person.Employee.Id, child.ManagerEmployeeId);
        Assert.Equal(person.Employee.FullName, child.ManagerName);
        using (var cycle = await admin.PutAsJsonAsync($"/api/organization-directory/departments/{root.Code}",
            new ApiOrganizationDepartmentSaveRequest(root.Code, root.CompanyCode, root.Name, true, root.VersionNumber, child.Code)))
            Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        var options = (await admin.GetFromJsonAsync<PersonnelOptions>("/api/office/people/options", JsonOptions))!;
        Assert.Contains(options.Departments, item => item.Code == child.Code && item.ParentCode == root.Code);
        var candidates = (await admin.GetFromJsonAsync<PagedResult<OrganizationManagerRecord>>(
            $"/api/organization-directory/managers?companyCode={root.CompanyCode}&keyword=PHOTO-001", JsonOptions))!;
        Assert.Equal(person.Employee.Id, Assert.Single(candidates.Items).Id);

        var catalog = (await admin.GetFromJsonAsync<ApiPermissionTemplateCatalogResponse>("/api/permission-templates", JsonOptions))!;
        var template = catalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.OfficeEmployee);
        await PostAsync<ApiUserSaveResponse>(admin, "/api/users", new ApiUserSaveRequest("photo-reader", "通讯录用户", "User", template.Id,
            root.Code, root.CompanyCode, true, "personnel-test-password"));
        var readerLogin = await harness.LoginAsync(anonymous, "photo-reader", "personnel-test-password");
        using var reader = harness.CreateClient(readerLogin.AccessToken);
        using (var avatar = await reader.GetAsync($"/api/office/people/{person.Employee.Id}/avatar")) Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        foreach (string url in new[] { $"/api/office/people/{person.Employee.Id}/images/IdentityFront",
            $"/api/office/people/{person.Employee.Id}/images/IdentityBack", $"/api/organization-directory/managers?companyCode={root.CompanyCode}" })
        {
            using var denied = await reader.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        string publicDirectory = await reader.GetStringAsync("/api/office/people");
        Assert.DoesNotContain("identity", publicDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("11010519491231002X", publicDirectory);
        using (var form = UploadForm(Png, "image/png", person.VersionNumber))
        using (var denied = await reader.PostAsync($"/api/office/people/{person.Employee.Id}/images/Avatar", form))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var removed = await admin.DeleteAsync($"/api/office/people/{person.Employee.Id}/images/IdentityBack?expectedVersion={person.VersionNumber}"))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var missing = await admin.GetAsync($"/api/office/people/{person.Employee.Id}/images/IdentityBack")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static MultipartFormDataContent UploadForm(byte[] bytes, string contentType, int version)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "personnel.png");
        form.Add(new StringContent(version.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");
        return form;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}
