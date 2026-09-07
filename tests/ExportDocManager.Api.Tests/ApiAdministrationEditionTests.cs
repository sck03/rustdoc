using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests;

public sealed class ApiAdministrationEditionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task LocalAdministration_ShouldWorkFromFirstLoginThroughHandoverAndDeparture()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("administration-http", "office.db",
            productEdition: ProductEditionCatalog.Administration);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
        using var client = harness.CreateClient(login.AccessToken);
        Assert.True(login.User.Capabilities.UsesOfficeRegister);
        Assert.True(login.User.Capabilities.CanManageUsers);
        Assert.DoesNotContain(login.User.Capabilities.EnabledModules, item => item.StartsWith("document.", StringComparison.Ordinal) ||
            item.StartsWith("sales.", StringComparison.Ordinal) || item is "common.email" or "common.exchange-rates");
        var catalog = await client.GetFromJsonAsync<ApiPermissionTemplateCatalogResponse>("/api/permission-templates");
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Resources, item => item.Key == PermissionResourceCatalog.OfficePeople);
        Assert.DoesNotContain(catalog.Resources, item => item.Workspace is "document" or "sales");
        Assert.DoesNotContain(catalog.Resources.Where(item => item.Workspace == "office").SelectMany(item => item.Actions),
            item => item.Key is PermissionAction.Approve or PermissionAction.Assign);
        var options = await client.GetFromJsonAsync<PersonnelOptions>("/api/office/people/options");
        Assert.NotNull(options);
        Assert.True(options.CanCreate);
        Assert.Contains(options.Departments, item => item.Code == OrganizationDirectoryDefaults.DepartmentCode);
        var person = await PostAsync<PersonnelRecord>(client, "/api/office/people", new PersonnelCreateRequest(Guid.NewGuid(), "A001",
            OrganizationDirectoryDefaults.DepartmentCode, "行政专员", EmploymentType.FullTime, login.User.BusinessDate, false, null, null,
            new("登记人员")));
        Assert.Null(person.Account);
        Assert.False(person.CanLinkAccount);
        var room = await PostAsync<MeetingRoomRecord>(client, "/api/office/rooms", new MeetingRoomSaveRequest("会议室", "办公室", "", 8, 8, 30, true, true, 0));
        var booking = await PostAsync<MeetingBookingRecord>(client, "/api/office/bookings", new MeetingBookingCreateRequest(Guid.NewGuid(), room.Id,
            "周例会", 3, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddHours(1), person.Employee.Id));
        Assert.Equal(MeetingBookingStatus.Approved, booking.Status);
        Assert.Equal("登记人员", booking.ApplicantName);
        booking = await PostAsync<MeetingBookingRecord>(client, $"/api/office/bookings/{booking.Id}/issue-key", new OfficeDecisionRequest(booking.VersionNumber));
        using (var denied = await client.PostAsJsonAsync($"/api/office/people/{person.Employee.Id}/depart",
                   new PersonnelTransitionRequest(person.VersionNumber, login.User.BusinessDate, "离职交接")))
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await PostAsync<MeetingBookingRecord>(client, $"/api/office/bookings/{booking.Id}/return-key", new OfficeDecisionRequest(booking.VersionNumber));
        person = await PostAsync<PersonnelRecord>(client, $"/api/office/people/{person.Employee.Id}/depart",
            new PersonnelTransitionRequest(person.VersionNumber, login.User.BusinessDate, "已完成交接"));
        Assert.Equal(EmploymentStatus.Departed, person.Employee.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}
