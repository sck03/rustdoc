using ExportDocManager.Services.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests;

public sealed class ApiOfficeAccessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task OfficeHttpEndpoints_ShouldExposePermissionModules_AndEnforceTheApprovalWorkflow()
    {
        // Exercise real HTTP/auth/EF handlers locally; the separate PostgreSQL
        // scenarios exercise provider locking and concurrent transactions.
        await using var harness = await ApiIntegrationTestHarness.StartAsync("office-http", "office.db", configureServices: ConfigureTeamMode);
        using var anonymous = harness.CreateClient();
        var adminSession = await harness.LoginAsync(anonymous, "admin", string.Empty);
        using var admin = harness.CreateClient(adminSession.AccessToken);
        var catalog = await admin.GetFromJsonAsync<ApiPermissionTemplateCatalogResponse>("/api/permission-templates");
        Assert.NotNull(catalog);
        Assert.Contains(catalog.Resources, resource => resource.Key == PermissionResourceCatalog.OfficeRooms && resource.Actions.Any(action => action.Key == PermissionAction.Approve));
        Assert.Contains(catalog.Resources, resource => resource.Key == PermissionResourceCatalog.OfficeSupplies && resource.Actions.Any(action => action.Key == PermissionAction.Restock));
        var employeeTemplate = catalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.OfficeEmployee);
        await PostAsync<ApiUserSaveResponse>(admin, "/api/users", new ApiUserSaveRequest("office-employee", "办公员工", "User", employeeTemplate.Id,
            OrganizationDirectoryDefaults.DepartmentCode, OrganizationDirectoryDefaults.CompanyCode, true, "office-test-password"));
        var employeeSession = await harness.LoginAsync(anonymous, "office-employee", "office-test-password");
        using var employee = harness.CreateClient(employeeSession.AccessToken);
        var room = await PostAsync<MeetingRoomRecord>(admin, "/api/office/rooms", new MeetingRoomSaveRequest("会议室", "三楼", "投影", 8, 8, 90, true, true, 0));
        var request = new MeetingBookingCreateRequest(Guid.NewGuid(), room.Id, "项目沟通", 3, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddHours(1));
        var booking = await PostAsync<MeetingBookingRecord>(employee, "/api/office/bookings", request);
        var denied = await employee.PostAsJsonAsync($"/api/office/bookings/{booking.Id}/approve", new OfficeDecisionRequest(booking.VersionNumber));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        booking = await PostAsync<MeetingBookingRecord>(admin, $"/api/office/bookings/{booking.Id}/approve", new OfficeDecisionRequest(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.Approved, booking.Status);
        booking = await PostAsync<MeetingBookingRecord>(admin, $"/api/office/bookings/{booking.Id}/issue-key", new OfficeDecisionRequest(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.InUse, booking.Status);
        booking = await PostAsync<MeetingBookingRecord>(admin, $"/api/office/bookings/{booking.Id}/return-key", new OfficeDecisionRequest(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.Completed, booking.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/office/rooms/0", new MeetingRoomSaveRequest("无效", "", "", 8, 8, 90, true, true, 0))).StatusCode);

        var supply = await PostAsync<OfficeSupplyRecord>(admin, "/api/office/supplies", new OfficeSupplySaveRequest("签字笔", "支", "办公室", "", false, true, 3, 0));
        await PostAsync<OfficeStockMovementRecord>(admin, $"/api/office/supplies/{supply.Id}/restock", new OfficeStockRequest(Guid.NewGuid(), 10, supply.VersionNumber, "首次入库"));
        var application = await PostAsync<OfficeSupplyRequestRecord>(employee, "/api/office/supply-requests", new SupplyRequestCreateRequest(Guid.NewGuid(), supply.Id, 2, "日常办公", null));
        application = await PostAsync<OfficeSupplyRequestRecord>(admin, $"/api/office/supply-requests/{application.Id}/approve", new OfficeReturnRequest(application.VersionNumber, 0));
        application = await PostAsync<OfficeSupplyRequestRecord>(admin, $"/api/office/supply-requests/{application.Id}/issue", new OfficeReturnRequest(application.VersionNumber, 0));
        Assert.Equal(SupplyRequestStatus.Issued, application.Status);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    [Theory]
    [InlineData(ProductEditionCatalog.Document)]
    [InlineData(ProductEditionCatalog.Sales)]
    [InlineData(ProductEditionCatalog.Full)]
    [InlineData(ProductEditionCatalog.Administration)]
    public void OfficeCapabilities_ShouldFollowProductEditionAndOperatingMode(string edition)
    {
        var user = new User { Id = 1, Role = UserRoleCatalog.Admin, CompanyScope = "DEFAULT" };
        foreach (bool networkMode in new[] { false, true })
        {
            bool enabled = networkMode || edition is ProductEditionCatalog.Full or ProductEditionCatalog.Administration;
            var authorization = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = edition, NetworkMode = networkMode }, new RuntimeCapabilitySet(CapabilityModuleKeys.All));
            foreach (string resource in new[] { PermissionResourceCatalog.OfficeRooms, PermissionResourceCatalog.OfficeSupplies })
            {
                Assert.Equal(enabled, authorization.CanUsePermission(user, resource, PermissionAction.Manage));
                Assert.Equal(enabled, authorization.GetEnabledModules(user).Contains(resource));
            }
            Assert.Equal(enabled, authorization.CanUsePermission(user, PermissionResourceCatalog.OfficePeople, PermissionAction.ViewDetails));
            Assert.Equal(enabled, authorization.GetEnabledModules(user).Contains(PermissionResourceCatalog.OfficePeople));
        }
    }

    [Theory]
    [InlineData(ProductEditionCatalog.Document)]
    [InlineData(ProductEditionCatalog.Sales)]
    public async Task SpecializedDesktopEditions_ShouldRejectDirectOfficeEndpoints_EvenForAdministrator(string edition)
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("office-desktop", "office.db", productEdition: edition);
        using var anonymous = harness.CreateClient();
        var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
        using var client = harness.CreateClient(login.AccessToken);
        Assert.DoesNotContain(login.User.Capabilities.EnabledModules, item => item.StartsWith("office.", StringComparison.Ordinal));
        Assert.False(login.User.Capabilities.UsesOfficeRegister);
        foreach (string route in new[] { "/api/office/rooms", "/api/office/bookings", "/api/office/supplies", "/api/office/supply-requests", "/api/office/people", "/api/office/people/1", "/api/office/people/options" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public void OfficeEmployeeTemplate_ShouldHaveNoApprovalOrStockPrivileges()
    {
        var template = BuiltInPermissionTemplateCatalog.Templates.Single(item => item.Code == BuiltInPermissionTemplateCatalog.OfficeEmployee);
        Assert.DoesNotContain(template.Grants, grant => grant.Action is PermissionAction.Approve or PermissionAction.Manage or PermissionAction.Restock or PermissionAction.Issue);
        Assert.All(template.Grants.Where(grant => grant.ResourceKey is PermissionResourceCatalog.OfficeRooms or PermissionResourceCatalog.OfficeSupplies), grant => Assert.Equal(PermissionDataScope.Own, grant.DataScope));
        Assert.Contains(template.Grants, grant => grant.ResourceKey == PermissionResourceCatalog.OfficePeople && grant.Action == PermissionAction.View && grant.DataScope == PermissionDataScope.Company);
        Assert.DoesNotContain(template.Grants, grant => grant.ResourceKey == PermissionResourceCatalog.OfficePeople && grant.Action == PermissionAction.ViewDetails);
    }

    internal static void ConfigureTeamMode(IServiceCollection services)
    {
        var runtime = (ApiRuntimeOptions)services.Single(item => item.ServiceType == typeof(ApiRuntimeOptions)).ImplementationInstance!;
        services.AddSingleton(new ApiRuntimeOptions { AppRoot = runtime.AppRoot, DataRoot = runtime.DataRoot, ListenUrls = runtime.ListenUrls, NetworkMode = true });
        services.AddScoped(provider => new BusinessDataAccessScope(
            new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider }, provider.GetRequiredService<ICurrentUserContext>()));
    }
}
