using System.Text.Json;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class PersonnelServiceTests
{
    [Fact]
    public async Task Hire_ShouldBeIdempotent_AndUseCompanyUniqueCanonicalEmployeeNumbers()
    {
        using var env = new PersonnelTestEnvironment();
        var request = env.Request("e\u0301-01");
        var employee = await env.People.CreateAsync(request);
        Assert.Equal("é-01", employee.Employee.EmployeeNumber);
        Assert.Equal(employee.Employee.Id, (await env.People.CreateAsync(request)).Employee.Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.CreateAsync(request with { JobTitle = "不同内容" }));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.CreateAsync(env.Request("É-01")));
        Assert.Single((await env.People.HistoryAsync(employee.Employee.Id, 1, 24)).Items);
        Assert.Single((await env.People.QueryAsync(new())).Items);
    }

    [Fact]
    public async Task Departure_ShouldRequirePhysicalClearance_ThenDisableAccountAndRevokeSessionsAtomically()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.LinkedAsync();
        var rooms = new MeetingRoomService(env.Office);
        var room = await rooms.SaveRoomAsync(0, new("交接会议室", "三楼", "", 8, 4, 30, true, true, 0));
        env.AsEmployee();
        var booking = await rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "项目会", 3, env.Office.Clock.UtcNow.AddMinutes(15), env.Office.Clock.UtcNow.AddHours(1)));
        env.AsAdmin();
        booking = await rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber));
        booking = await rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Issue, new(booking.VersionNumber));
        var clearance = await env.People.ClearanceAsync(person.Employee.Id);
        Assert.False(clearance.IsClear);
        Assert.Equal(1, Assert.Single(clearance.Items).OutstandingQuantity);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "离职交接")));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Transfer, new(person.VersionNumber, env.Today, "部门调动", "D2", "运营专员")));
        Assert.Equal(EmploymentStatus.Probation, (await env.People.GetAsync(person.Employee.Id)).Employee.Status);
        await rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Return, new(booking.VersionNumber));
        await using (var db = env.Database.CreateDbContext())
        {
            db.ApiUserSessions.Add(new() { UserId = env.Account.Id, TokenHash = new('a', 64), ExpiresAt = env.Office.Clock.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var departed = await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "交接完成"));
        Assert.Equal(env.Account.Id, departed.ChangedAccountUserId);
        Assert.Equal(EmploymentStatus.Departed, departed.Record.Employee.Status);
        Assert.False(departed.Record.Account!.IsActive);
        Assert.Empty((await env.People.QueryAsync(new())).Items);
        Assert.Single((await env.People.QueryAsync(new(Status: "Departed"))).Items);
        await using (var db = env.Database.CreateDbContext()) Assert.NotNull((await db.ApiUserSessions.SingleAsync()).RevokedAt);
        env.AsEmployee();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "离职后申请", 2,
            env.Office.Clock.UtcNow.AddHours(2), env.Office.Clock.UtcNow.AddHours(3))));
    }

    [Fact]
    public async Task BorrowedSuppliesAndReservedRequests_ShouldRemainInClearance_UntilReturnedOrCancelled()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.LinkedAsync();
        var supplies = new OfficeSupplyService(env.Office);
        var supply = await supplies.SaveSupplyAsync(0, new("电脑", "台", "办公室", "", true, true, 0, 0));
        await supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 2, supply.VersionNumber, "首次入库"), false);
        env.AsEmployee();
        var request = await supplies.CreateRequestAsync(new(Guid.NewGuid(), supply.Id, 2, "办公", env.Today.AddDays(10)));
        env.AsAdmin();
        request = await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Approve, new(request.VersionNumber, 0));
        Assert.False((await env.People.ClearanceAsync(person.Employee.Id)).IsClear);
        request = await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Issue, new(request.VersionNumber, 0));
        request = await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(request.VersionNumber, 1));
        Assert.Equal(1, (await env.People.ClearanceAsync(person.Employee.Id)).Items.Single().OutstandingQuantity);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "离职")));
        await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(request.VersionNumber, 1));
        Assert.True((await env.People.ClearanceAsync(person.Employee.Id)).IsClear);
    }

    [Fact]
    public async Task EmploymentChanges_ShouldPreserveHistory_RejectStaleVersions_AndKeepRehiredAccountDisabled()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.LinkedAsync();
        int initialVersion = person.VersionNumber;
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Confirm, new(person.VersionNumber, env.Today, "试用通过"))).Record;
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(initialVersion, env.Today, "过期界面")));
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Transfer, new(person.VersionNumber, env.Today, "组织调整", "D2", "运营专员"))).Record;
        Assert.Equal("D2", person.Account!.DepartmentId);
        Assert.Equal("运营部", person.Employee.DepartmentName);
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today.AddDays(-1), "倒序日期")));
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "交接结清"))).Record;
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.UpdateAsync(person.Employee.Id, new(person.VersionNumber, person.Profile, person.EmploymentType, null, null)));
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Rehire, new(person.VersionNumber, env.Today, "再次入职", "D1", "业务主管", false))).Record;
        Assert.Equal(EmploymentStatus.Active, person.Employee.Status);
        Assert.Null(person.DepartedOn);
        Assert.False(person.Account!.IsActive);
        Assert.Equal(["Rehire", "Depart", "Transfer", "Confirm", "LinkAccount", "Hire"], (await env.People.HistoryAsync(person.Employee.Id, 1, 24)).Items.Select(item => item.Action));
    }

    [Fact]
    public async Task Directory_ShouldNotDisclosePersonnelFields_AndDepartmentPermissions_ShouldApplyPerAction()
    {
        using var env = new PersonnelTestEnvironment();
        var first = await env.People.CreateAsync(env.Request());
        var other = await env.People.CreateAsync(env.Request("EMP-002", "D2"));
        env.AsEmployee();
        string publicJson = JsonSerializer.Serialize(await env.People.QueryAsync(new()));
        Assert.DoesNotContain("private-phone", publicJson);
        Assert.DoesNotContain("Emergency", publicJson);
        Assert.DoesNotContain("仅人事可见", publicJson);
        Assert.All((await env.People.QueryAsync(new())).Items, row => Assert.False(row.CanViewDetails));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.GetAsync(first.Employee.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.HistoryAsync(first.Employee.Id, 1, 24));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.QueryAsync(new(Status: "Departed")));
        env.Actor.CurrentUser!.EffectivePermissionGrants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.View)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.ViewDetails)] = PermissionDataScope.Department,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.Edit)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.Transition)] = PermissionDataScope.Department
        };
        Assert.True((await env.People.GetAsync(first.Employee.Id)).CanEdit);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.GetAsync(other.Employee.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.UpdateAsync(other.Employee.Id, new(other.VersionNumber, other.Profile, other.EmploymentType, null, null)));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.TransitionAsync(first.Employee.Id, PersonnelAction.Transfer, new(first.VersionNumber, env.Today, "跨部门", "D2", "新岗位")));
        env.Actor.CurrentUser.EffectivePermissionGrants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.View)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.Create)] = PermissionDataScope.Own,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.ViewDetails)] = PermissionDataScope.Company
        };
        Assert.False((await env.People.OptionsAsync()).CanCreate);
        env.Actor.CurrentUser = new User { Id = 99, Username = "other-company", CompanyScope = "C2", Role = UserRoleCatalog.Admin };
        Assert.Empty((await env.People.QueryAsync(new())).Items);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.People.GetAsync(first.Employee.Id));
    }

    [Fact]
    public async Task AccountBinding_ShouldRequireAdministratorAndCurrentVersion_AndRejectSelfOrDuplicateBinding()
    {
        using var env = new PersonnelTestEnvironment();
        var first = await env.People.CreateAsync(env.Request());
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.People.LinkAccountAsync(first.Employee.Id, new(first.VersionNumber, env.Account.Id, 0)));
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.LinkAccountAsync(first.Employee.Id, new(first.VersionNumber, env.Admin.Id, env.Admin.VersionNumber)));
        first = (await env.People.LinkAccountAsync(first.Employee.Id, new(first.VersionNumber, env.Account.Id, env.Account.VersionNumber))).Record;
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.LinkAccountAsync(first.Employee.Id, new(first.VersionNumber, env.Account.Id, first.Account!.VersionNumber)));
        env.AsEmployee();
        env.Actor.CurrentUser!.EffectivePermissionGrants = PermissionResourceCatalog.ExpandDependencies(
            [new(PermissionResourceCatalog.OfficePeople, PermissionAction.Assign, PermissionDataScope.Company)])
            .ToDictionary(item => PermissionResourceCatalog.CreateGrantKey(item.ResourceKey, item.Action), item => item.DataScope);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.AccountOptionsAsync(first.Employee.Id, null, 1, 24));
    }

    [Fact]
    public async Task AccountAndOrganizationAdministration_ShouldNotBypassPersonnelLifecycle()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.LinkedAsync();
        var accounts = new UserService(env.Database, env.Settings, env.Actor);
        await using var db = env.Database.CreateDbContext();
        var account = await db.Users.AsNoTracking().SingleAsync(item => item.Id == env.Account.Id);
        account.DepartmentId = "D2";
        await Assert.ThrowsAsync<ResourceConflictException>(() => accounts.SaveUserAsync(account));
        await Assert.ThrowsAsync<ResourceConflictException>(() => accounts.DeleteUserAsync(account.Id, expectedVersion: account.VersionNumber));
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "交接完成"))).Record;
        account.DepartmentId = "D1";
        account.VersionNumber = person.Account!.VersionNumber;
        account.IsActive = true;
        await Assert.ThrowsAsync<ResourceConflictException>(() => accounts.SaveUserAsync(account));
        var unlinked = await env.People.CreateAsync(env.Request("EMP-002", "D2"));
        var organizations = new OrganizationDirectoryService(env.Database, env.Actor);
        await Assert.ThrowsAsync<ResourceConflictException>(() => organizations.SaveDepartmentAsync(new("D2", "D2", "C1", "运营部", false, 1)));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("department")]
    [InlineData("dates")]
    [InlineData("enum")]
    [InlineData("emergency")]
    public async Task Hire_ShouldRejectInvalidEmploymentData(string kind)
    {
        using var env = new PersonnelTestEnvironment();
        var request = env.Request();
        request = kind switch
        {
            "future" => request with { HireDate = env.Today.AddDays(1) },
            "department" => request with { DepartmentId = "D3" },
            "dates" => request with { ContractEndsOn = request.HireDate.AddDays(-1) },
            "enum" => request with { EmploymentType = (EmploymentType)999 },
            _ => request with { Profile = request.Profile with { EmergencyPhone = "" } }
        };
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.CreateAsync(request));
        Assert.Empty((await env.People.QueryAsync(new())).Items);
    }

    [Fact]
    public async Task PagingAndExpiryFilters_ShouldBeBounded_AndCancelledWritesShouldLeaveNoRecords()
    {
        using var env = new PersonnelTestEnvironment();
        await env.People.CreateAsync(env.Request());
        await env.People.CreateAsync(env.Request("EMP-002") with { ProbationEndsOn = null });
        Assert.Equal(2, (await env.People.QueryAsync(new(PageSize: 1))).TotalCount);
        Assert.Single((await env.People.QueryAsync(new(AttentionOnly: true))).Items);
        Assert.Empty((await env.People.QueryAsync(new(Keyword: "private-phone"))).Items);
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.QueryAsync(new(PageSize: 101)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.People.CreateAsync(env.Request("EMP-003"), cancellation.Token));
        Assert.Equal(2, (await env.People.QueryAsync(new())).TotalCount);
    }
}
