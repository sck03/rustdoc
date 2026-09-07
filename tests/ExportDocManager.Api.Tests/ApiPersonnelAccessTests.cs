using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests;

public sealed class ApiPersonnelAccessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task PersonnelHttpLifecycle_ShouldKeepDirectoryPrivate_AndRevokeCachedSessionsAfterDeparture()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("personnel-http", "people.db", configureServices: services =>
        {
            ApiOfficeAccessTests.ConfigureTeamMode(services);
            services.AddSingleton<IApiSessionTokenService, DatabaseApiSessionTokenService>();
        });
        using var anonymous = harness.CreateClient();
        var adminLogin = await harness.LoginAsync(anonymous, "admin", "");
        using var admin = harness.CreateClient(adminLogin.AccessToken);
        var catalog = await admin.GetFromJsonAsync<ApiPermissionTemplateCatalogResponse>("/api/permission-templates");
        Assert.NotNull(catalog);
        var employeeTemplate = catalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.OfficeEmployee);
        var hrTemplate = catalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.PersonnelManager);
        Assert.Contains(catalog.Resources, item => item.Key == PermissionResourceCatalog.OfficePeople && item.Actions.Any(action => action.Key == PermissionAction.ViewDetails));
        Assert.DoesNotContain(catalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.OfficeManager).Grants,
            item => item.ResourceKey == PermissionResourceCatalog.OfficePeople && item.Action != PermissionAction.View);
        var account = await PostAsync<ApiUserSaveResponse>(admin, "/api/users", new ApiUserSaveRequest("personnel-employee", "员工", "User", employeeTemplate.Id,
            OrganizationDirectoryDefaults.DepartmentCode, OrganizationDirectoryDefaults.CompanyCode, true, "personnel-test-password"));
        await PostAsync<ApiUserSaveResponse>(admin, "/api/users", new ApiUserSaveRequest("personnel-hr", "人事", "User", hrTemplate.Id,
            OrganizationDirectoryDefaults.DepartmentCode, OrganizationDirectoryDefaults.CompanyCode, true, "personnel-test-password"));
        var hrLogin = await harness.LoginAsync(anonymous, "personnel-hr", "personnel-test-password");
        using var hr = harness.CreateClient(hrLogin.AccessToken);
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var person = await PostAsync<PersonnelRecord>(hr, "/api/office/people", new PersonnelCreateRequest(Guid.NewGuid(), "EMP-HTTP-01",
            OrganizationDirectoryDefaults.DepartmentCode, "业务专员", EmploymentType.FullTime, hireDate, true, null, null,
            new("员工", "employee@example.test", "1001", "三楼", "private-phone", "紧急联系人", "emergency-phone", "private-note")));
        var binding = new PersonnelAccountRequest(person.VersionNumber, account.User.Id, account.User.VersionNumber);
        using var forbiddenBinding = await hr.PostAsJsonAsync($"/api/office/people/{person.Employee.Id}/account", binding);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenBinding.StatusCode);
        person = await PostAsync<PersonnelRecord>(admin, $"/api/office/people/{person.Employee.Id}/account", binding);
        var employeeLogin = await harness.LoginAsync(anonymous, "personnel-employee", "personnel-test-password");
        using var employee = harness.CreateClient(employeeLogin.AccessToken);
        string directory = await employee.GetStringAsync("/api/office/people");
        Assert.DoesNotContain("personalPhone", directory);
        Assert.DoesNotContain("private-phone", directory);
        Assert.DoesNotContain("private-note", directory);
        Assert.Contains("employee@example.test", directory);
        foreach (string suffix in new[] { "", "/history", "/clearance", "/accounts" })
        {
            using var forbidden = await employee.GetAsync($"/api/office/people/{person.Employee.Id}{suffix}");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var invalid = await hr.PostAsJsonAsync($"/api/office/people/{person.Employee.Id}/depart", new PersonnelTransitionRequest(0, hireDate, "过期界面"));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        person = await PostAsync<PersonnelRecord>(hr, $"/api/office/people/{person.Employee.Id}/depart", new PersonnelTransitionRequest(person.VersionNumber, hireDate, "交接完成"));
        Assert.Equal(EmploymentStatus.Departed, person.Employee.Status);
        Assert.False(person.Account!.IsActive);
        using var revoked = await employee.GetAsync("/api/office/people");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var loginDenied = await anonymous.PostAsJsonAsync("/api/auth/login", new { username = "personnel-employee", password = "personnel-test-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginDenied.StatusCode);
        var reactivation = new ApiUserSaveRequest(account.User.Username, "员工", "User", employeeTemplate.Id,
            OrganizationDirectoryDefaults.DepartmentCode, OrganizationDirectoryDefaults.CompanyCode, true, "", person.Account.VersionNumber);
        using var blocked = await admin.PutAsJsonAsync($"/api/users/{account.User.Id}", reactivation);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task PersonnelOpenApi_ShouldUseTypedWorkDirectoryAndDateContracts()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("personnel-openapi", "people.db");
        using var client = harness.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var directory = schemas.GetProperty(nameof(PersonnelDirectoryRecord)).GetProperty("properties");
        Assert.False(directory.TryGetProperty("personalPhone", out _));
        Assert.False(directory.TryGetProperty("notes", out _));
        var profile = schemas.GetProperty(nameof(PersonnelProfile)).GetProperty("properties");
        Assert.True(profile.TryGetProperty("personalPhone", out _));
        Assert.Equal("date", schemas.GetProperty(nameof(PersonnelTransitionRequest)).GetProperty("properties").GetProperty("effectiveDate").GetProperty("format").GetString());
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}
