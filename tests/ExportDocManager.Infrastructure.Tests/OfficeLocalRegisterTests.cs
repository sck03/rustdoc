using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class OfficeLocalRegisterTests
{
    [Fact]
    public async Task BookingRegister_ShouldTrackTheEmployeeAndBlockDepartureUntilKeyReturn()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var employee = await env.People.CreateAsync(env.Request());
        var other = await env.People.CreateAsync(env.Request("EMP-002"));
        Assert.Null(employee.Account);
        Assert.False(employee.CanLinkAccount);
        var rooms = new MeetingRoomService(env.Office);
        var room = await rooms.SaveRoomAsync(0, new("会议室", "三层", "", 8, 8, 30, true, true, 0));
        var request = new MeetingBookingCreateRequest(Guid.NewGuid(), room.Id, "部门例会", 4,
            env.Office.Clock.UtcNow.AddMinutes(15), env.Office.Clock.UtcNow.AddHours(1), employee.Employee.Id);
        var booking = await rooms.CreateBookingAsync(request);
        Assert.Equal(MeetingBookingStatus.Approved, booking.Status);
        Assert.Equal(employee.Employee.FullName, booking.ApplicantName);
        Assert.Equal(booking.Id, (await rooms.CreateBookingAsync(request)).Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => rooms.CreateBookingAsync(request with { EmployeeId = other.Employee.Id }));
        Assert.Equal("Register", Assert.Single((await rooms.HistoryAsync(booking.Id)).Items).Action);
        Assert.False((await env.People.ClearanceAsync(employee.Employee.Id)).IsClear);
        Assert.True((await env.People.ClearanceAsync(other.Employee.Id)).IsClear);
        Assert.Equal(booking.Id, Assert.Single((await rooms.QueryBookingsAsync(new(MineOnly: false, EmployeeId: employee.Employee.Id))).Items).Id);
        Assert.Empty((await rooms.QueryBookingsAsync(new(MineOnly: false, EmployeeId: other.Employee.Id))).Items);
        var departure = new PersonnelTransitionRequest(employee.VersionNumber, env.Today, "完成交接后离职");
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Depart, departure));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Transfer,
            departure with { DepartmentId = "D2", JobTitle = "运营专员" }));
        booking = await rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Issue, new(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.InUse, booking.Status);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Depart, departure));
        booking = await rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Return, new(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.Completed, booking.Status);
        Assert.True((await env.People.ClearanceAsync(employee.Employee.Id)).IsClear);
        var result = await env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Depart, departure);
        Assert.Equal(EmploymentStatus.Departed, result.Record.Employee.Status);
        Assert.Null(result.ChangedAccountUserId);
        await Assert.ThrowsAsync<ServiceValidationException>(() => rooms.CreateBookingAsync(request with { RequestKey = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SupplyRegister_ShouldReserveOnceAndRequireEveryBorrowedItemBeforeDeparture()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var employee = await env.People.CreateAsync(env.Request());
        var supplies = new OfficeSupplyService(env.Office);
        var supply = await supplies.SaveSupplyAsync(0, new("投影仪", "台", "三层", "", true, true, 1, 0));
        await supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 3, supply.VersionNumber, "采购入库"), false);
        var request = new SupplyRequestCreateRequest(Guid.NewGuid(), supply.Id, 2, "会议使用", env.Today, employee.Employee.Id);
        var issued = await supplies.CreateRequestAsync(request);
        Assert.Equal(SupplyRequestStatus.Approved, issued.Status);
        Assert.Equal(issued.Id, (await supplies.CreateRequestAsync(request)).Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => supplies.CreateRequestAsync(request with { RequestKey = Guid.NewGuid() }));
        var inventory = Assert.Single((await supplies.QuerySuppliesAsync(new())).Items);
        Assert.Equal(3, inventory.StockQuantity);
        Assert.Equal(2, inventory.ReservedQuantity);
        issued = await supplies.TransitionAsync(issued.Id, OfficeWorkflowAction.Issue, new(issued.VersionNumber, 0));
        issued = await supplies.TransitionAsync(issued.Id, OfficeWorkflowAction.Return, new(issued.VersionNumber, 1));
        var clearance = await env.People.ClearanceAsync(employee.Employee.Id);
        Assert.Equal(1, Assert.Single(clearance.Items).OutstandingQuantity);
        var departure = new PersonnelTransitionRequest(employee.VersionNumber, env.Today, "离职交接");
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Depart, departure));
        await supplies.TransitionAsync(issued.Id, OfficeWorkflowAction.Return, new(issued.VersionNumber, 1));
        inventory = Assert.Single((await supplies.QuerySuppliesAsync(new())).Items);
        Assert.Equal(3, inventory.StockQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.True((await env.People.ClearanceAsync(employee.Employee.Id)).IsClear);
        await env.People.TransitionAsync(employee.Employee.Id, PersonnelAction.Depart, departure);
    }

    [Fact]
    public async Task CancellingRegister_ShouldReleaseStockWithoutChangingTheLedger()
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var employee = await env.People.CreateAsync(env.Request());
        var supplies = new OfficeSupplyService(env.Office);
        var supply = await supplies.SaveSupplyAsync(0, new("签字笔", "支", "", "", false, true, 0, 0));
        await supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 5, supply.VersionNumber, "入库"), false);
        var request = await supplies.CreateRequestAsync(new(Guid.NewGuid(), supply.Id, 3, "办公", null, employee.Employee.Id));
        await supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Cancel, new(request.VersionNumber, 0, "无需领取"));
        Assert.Equal(5, Assert.Single((await supplies.QuerySuppliesAsync(new())).Items).AvailableQuantity);
        Assert.Single((await supplies.StockHistoryAsync(supply.Id, 1, 50)).Items);
        Assert.True((await env.People.ClearanceAsync(employee.Employee.Id)).IsClear);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(9999)]
    public async Task Register_ShouldRejectMissingOrUnknownEmployee(int? employeeId)
    {
        using var env = new PersonnelTestEnvironment(OfficeOperatingMode.LocalRegister);
        var rooms = new MeetingRoomService(env.Office);
        await Assert.ThrowsAsync<ServiceValidationException>(() => rooms.CreateBookingAsync(new(Guid.NewGuid(), 1, "例会", 1,
            env.Office.Clock.UtcNow.AddHours(1), env.Office.Clock.UtcNow.AddHours(2), employeeId)));
    }

    [Fact]
    public async Task TeamMode_ShouldRejectApplicantImpersonation()
    {
        using var env = new PersonnelTestEnvironment();
        var rooms = new MeetingRoomService(env.Office);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => rooms.CreateBookingAsync(new(Guid.NewGuid(), 1, "例会", 1,
            env.Office.Clock.UtcNow.AddHours(1), env.Office.Clock.UtcNow.AddHours(2), 1)));
        var supplies = new OfficeSupplyService(env.Office);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => supplies.CreateRequestAsync(new(Guid.NewGuid(), 1, 1, "办公", null, 1)));
    }
}
