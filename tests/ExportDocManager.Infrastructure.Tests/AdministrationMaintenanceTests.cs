using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class AdministrationMaintenanceTests
{
    [Fact]
    public async Task MisenteredPersonnel_CanBeCorrectedAndDeleted_WithVersionAndAuditProtection()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var person = await env.People.CreateAsync(env.Request());
        Assert.True(person.CanCorrectRegistration);
        var correction = new PersonnelRegistrationCorrection("EMP-CORRECTED", "D2", "主管", person.HireDate.AddDays(1), false);
        var changed = (await env.People.UpdateAsync(person.Employee.Id, new(person.VersionNumber,
            person.Profile with { FullName = "修正姓名" }, person.EmploymentType, person.ProbationEndsOn, person.ContractEndsOn, correction))).Record;
        Assert.Equal("D2", changed.Employee.DepartmentId);
        Assert.Equal("EMP-CORRECTED", changed.Employee.EmployeeNumber);
        Assert.Equal(correction.HireDate, changed.HireDate);
        Assert.Equal(correction.HireDate, changed.LastEffectiveDate);
        Assert.Equal(correction.HireDate, changed.ConfirmedOn);
        Assert.True(changed.CanDelete);
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.People.DeleteAsync(person.Employee.Id, new(person.VersionNumber, "误录")));
        env.AsEmployee();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.DeleteAsync(person.Employee.Id, new(changed.VersionNumber, "越权")));
        env.AsAdmin();
        await env.People.DeleteAsync(person.Employee.Id, new(changed.VersionNumber, "重复登记"));
        await using var db = env.Database.CreateDbContext();
        Assert.False(await db.PersonnelEmployees.AnyAsync());
        Assert.False(await db.PersonnelEvents.AnyAsync());
        var audit = await db.AuditLogs.SingleAsync(item => item.Action == "Delete" && item.EntityName == nameof(PersonnelEmployee));
        Assert.Equal(person.Employee.Id.ToString(), audit.EntityId);
        Assert.DoesNotContain("private-phone", audit.NewValues ?? "");
    }

    [Fact]
    public async Task FormalPersonnelHistory_BlocksDeletionAndInitialRegistrationRewrite()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.People.CreateAsync(env.Request());
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Confirm,
            new(person.VersionNumber, env.Today, "试用通过"))).Record;
        Assert.False(person.CanCorrectRegistration);
        Assert.NotEmpty(person.DeleteRestriction);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.DeleteAsync(person.Employee.Id, new(person.VersionNumber, "误删尝试")));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.UpdateAsync(person.Employee.Id,
            new(person.VersionNumber, person.Profile, person.EmploymentType, person.ProbationEndsOn, person.ContractEndsOn,
                new("REWRITE", "D1", "岗位", person.HireDate, false))));
    }

    [Fact]
    public async Task OrganizationDeletion_ProtectsBusinessScopesAndChildren_AndAllowsUnusedEntries()
    {
        using var env = new PersonnelTestEnvironment();
        var service = new OrganizationDirectoryService(env.Database, env.Actor);
        var company = await service.SaveCompanyAsync(new("", "UNUSED", "误建公司", true));
        var department = await service.SaveDepartmentAsync(new("", "UNUSED-DEPT", company.Code, "误建部门", true));
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.DeleteCompanyAsync(company.Code, new(company.VersionNumber, "仍有部门")));
        await service.DeleteDepartmentAsync(department.Code, new(department.VersionNumber, "误建"));
        await service.DeleteCompanyAsync(company.Code, new(company.VersionNumber, "误建"));
        var scoped = await service.SaveDepartmentAsync(new("", "BUSINESS", "C1", "业务归属", true));
        await using (var db = env.Database.CreateDbContext())
        {
            db.Payments.Add(new Payment { DepartmentId = scoped.Code, CompanyScope = "C1", InvoiceNo = "SCOPE-REF" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.DeleteDepartmentAsync(scoped.Code, new(scoped.VersionNumber, "有付款引用")));
        await Assert.ThrowsAsync<BusinessConcurrencyException>(() => service.DeleteDepartmentAsync(scoped.Code, new(0, "过期版本")));
        env.AsEmployee();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.DeleteDepartmentAsync(scoped.Code, new(scoped.VersionNumber, "越权")));
    }

    [Fact]
    public async Task ResourceDeletion_OnlyRemovesUnusedRoomsAndSupplies()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var rooms = new MeetingRoomService(env.Office);
        var supplies = new OfficeSupplyService(env.Office);
        var room = await rooms.SaveRoomAsync(0, new("误建会议室", "", "", 10, 8, 30, false, true, 0));
        await rooms.DeleteRoomAsync(room.Id, new(room.VersionNumber, "误建"));
        var unused = await supplies.SaveSupplyAsync(0, new("误建物品", "件", "", "", false, true, 0, 0));
        await supplies.DeleteSupplyAsync(unused.Id, new(unused.VersionNumber, "误建"));
        var stock = await supplies.SaveSupplyAsync(0, new("有库存物品", "件", "", "", false, true, 0, 0));
        await supplies.ChangeStockAsync(stock.Id, new(Guid.NewGuid(), 10, stock.VersionNumber, "入库"), false);
        stock = (await supplies.QuerySuppliesAsync(new())).Items.Single();
        await Assert.ThrowsAsync<ResourceConflictException>(() => supplies.DeleteSupplyAsync(stock.Id, new(stock.VersionNumber, "不能抹除库存")));
        Assert.Empty((await rooms.QueryRoomsAsync(new())).Items);
    }

    [Fact]
    public async Task LocalSupplyEdits_AdjustReservedStockAtomically_AndKeepHandoverHistory()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var person = await env.People.CreateAsync(env.Request());
        var supplies = new OfficeSupplyService(env.Office);
        var supply = await supplies.SaveSupplyAsync(0, new("投影仪", "台", "", "", true, true, 0, 0));
        await supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 10, supply.VersionNumber, "入库"), false);
        var request = await supplies.CreateRequestAsync(new(Guid.NewGuid(), supply.Id, 2, "原用途", env.Today.AddDays(1), person.Employee.Id));
        var changed = await supplies.UpdateRequestAsync(request.Id, new(request.VersionNumber, 5, "修正用途", env.Today.AddDays(2)));
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => supplies.UpdateRequestAsync(request.Id, new(request.VersionNumber, 4, "过期", env.Today.AddDays(2))));
        await Assert.ThrowsAsync<ResourceConflictException>(() => supplies.UpdateRequestAsync(request.Id, new(changed.VersionNumber, 11, "超库存", env.Today.AddDays(2))));
        Assert.Equal(5, (await supplies.QuerySuppliesAsync(new())).Items.Single().ReservedQuantity);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.DeleteAsync(person.Employee.Id, new(person.VersionNumber, "已有领用")));
        changed = await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Issue, new(changed.VersionNumber, 0));
        await Assert.ThrowsAsync<ResourceConflictException>(() => supplies.UpdateRequestAsync(request.Id, new(changed.VersionNumber, 1, "已交接", env.Today.AddDays(2))));
        Assert.Contains((await supplies.HistoryAsync(request.Id)).Items, item => item.Action == "Edit");
    }

    [Fact]
    public async Task TeamBookingEdits_RequireFreshApproval_AndRejectOverlaps()
    {
        using var env = new PersonnelTestEnvironment();
        var service = new MeetingRoomService(env.Office);
        var room = await service.SaveRoomAsync(0, new("会议室", "", "", 10, 8, 30, false, true, 0));
        var start = env.Office.Clock.UtcNow.AddDays(1);
        env.AsEmployee();
        var booking = await service.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "会议", 3, start, start.AddHours(1)));
        env.AsAdmin();
        booking = await service.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber));
        env.AsEmployee();
        booking = await service.UpdateBookingAsync(booking.Id, new(booking.VersionNumber, "修改会议", 4, start, start.AddHours(2)));
        Assert.Equal(MeetingBookingStatus.Pending, booking.Status);
        var other = await service.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "另一会议", 2, start.AddHours(3), start.AddHours(4)));
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.UpdateBookingAsync(booking.Id,
            new(booking.VersionNumber, "时间重叠", 4, other.StartsAt, other.EndsAt)));
        env.AsAdmin();
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.DeleteRoomAsync(room.Id, new(room.VersionNumber, "仍有历史")));
    }
}
