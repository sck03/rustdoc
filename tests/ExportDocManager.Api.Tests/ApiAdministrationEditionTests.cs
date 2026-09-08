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

    [Theory]
    [InlineData(ProductEditionCatalog.Administration)]
    [InlineData(ProductEditionCatalog.Full)]
    public async Task LocalAdministration_ShouldWorkFromFirstLoginThroughHandoverAndDeparture(string edition)
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("administration-http", "office.db",
            productEdition: edition);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
        using var client = harness.CreateClient(login.AccessToken);
        Assert.True(login.User.Capabilities.UsesOfficeRegister);
        Assert.True(login.User.Capabilities.CanManageUsers);
        Assert.Equal(edition == ProductEditionCatalog.Full, login.User.Capabilities.CanUseDocumentWorkspace);
        Assert.Equal(edition == ProductEditionCatalog.Full, login.User.Capabilities.CanUseSalesWorkspace);
        foreach (string module in new[] { "common.email", "common.exchange-rates" })
            Assert.Equal(edition == ProductEditionCatalog.Full, login.User.Capabilities.EnabledModules.Contains(module));
        var catalog = await client.GetFromJsonAsync<ApiPermissionTemplateCatalogResponse>("/api/permission-templates");
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Resources, item => item.Key == PermissionResourceCatalog.OfficePeople);
        Assert.Equal(edition == ProductEditionCatalog.Full, catalog.Resources.Any(item => item.Workspace is "document" or "sales"));
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
        var supply = await PostAsync<OfficeSupplyRecord>(client, "/api/office/supplies", new OfficeSupplySaveRequest("投影仪", "台", "办公室", "", true, true, 0, 0));
        await PostAsync<OfficeStockMovementRecord>(client, $"/api/office/supplies/{supply.Id}/restock", new OfficeStockRequest(Guid.NewGuid(), 2, supply.VersionNumber, "设备入库"));
        var request = await PostAsync<OfficeSupplyRequestRecord>(client, "/api/office/supply-requests",
            new SupplyRequestCreateRequest(Guid.NewGuid(), supply.Id, 1, "会议借用", login.User.BusinessDate.AddDays(1), person.Employee.Id));
        Assert.Equal(SupplyRequestStatus.Approved, request.Status);
        Assert.Equal("登记人员", request.ApplicantName);
        request = await PostAsync<OfficeSupplyRequestRecord>(client, $"/api/office/supply-requests/{request.Id}/issue", new OfficeReturnRequest(request.VersionNumber, 0));
        using (var denied = await client.PostAsJsonAsync($"/api/office/people/{person.Employee.Id}/depart",
                   new PersonnelTransitionRequest(person.VersionNumber, login.User.BusinessDate, "尚有借用物品")))
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        request = await PostAsync<OfficeSupplyRequestRecord>(client, $"/api/office/supply-requests/{request.Id}/return", new OfficeReturnRequest(request.VersionNumber, 1));
        Assert.Equal(SupplyRequestStatus.Returned, request.Status);
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
