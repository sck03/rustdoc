using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class OfficeServicesTests
{
    [Fact]
    public async Task Booking_ApprovalAndKeyReturn_ShouldBeVersionedAndAudited()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var booking = await env.BookAsync(room.Id);
        env.AsAdmin();
        var approved = await env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber));
        Assert.Equal(MeetingBookingStatus.Approved, approved.Status);
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Issue, new(booking.VersionNumber)));
        var issued = await env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Issue, new(approved.VersionNumber, "本人到场核验"));
        Assert.Equal(env.Clock.UtcNow, issued.IssuedAt);
        Assert.True((await env.Rooms.QueryRoomsAsync(new())).Items.Single().InUse);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Cancel, new(issued.VersionNumber, "不可取消")));
        var returned = await env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Return, new(issued.VersionNumber));
        Assert.Equal(MeetingBookingStatus.Completed, returned.Status);
        Assert.Equal(env.Clock.UtcNow, returned.ReturnedAt);
        Assert.False((await env.Rooms.QueryRoomsAsync(new())).Items.Single().InUse);
        var history = await env.Rooms.HistoryAsync(booking.Id);
        Assert.Equal(["Return", "Issue", "Approve", "Submit"], history.Items.Select(item => item.Action));
    }

    [Fact]
    public async Task PendingBooking_ShouldReserveTime_AndAdjacentBookingsShouldBeAllowed()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var first = await env.BookAsync(room.Id);
        env.Actor.CurrentUser = env.Other;
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "重叠", 2,
            first.StartsAt.ToOffset(TimeSpan.FromHours(8)), first.EndsAt)));
        var adjacent = await env.Rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "下一场", 2, first.EndsAt, first.EndsAt.AddHours(1)));
        Assert.Equal(MeetingBookingStatus.Pending, adjacent.Status);
        var slots = await env.Rooms.AvailabilityAsync(room.Id, env.Clock.UtcNow, env.Clock.UtcNow.AddDays(1));
        Assert.Equal(2, slots.Count);
        var own = await env.Rooms.QueryBookingsAsync(new(MineOnly: false));
        Assert.Equal(adjacent.Id, Assert.Single(own.Items).Id);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.Rooms.HistoryAsync(first.Id));
        env.AsAdmin();
        await env.Rooms.TransitionAsync(first.Id, OfficeWorkflowAction.Reject, new(first.VersionNumber, "调整时间"));
        env.Actor.CurrentUser = env.Employee;
        await env.BookAsync(room.Id);
    }

    [Fact]
    public async Task OverdueKey_ShouldBlockNextHandover_UntilActualReturn()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var first = await env.BookAsync(room.Id);
        var second = await env.Rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "下一场", 2, first.EndsAt, first.EndsAt.AddHours(1)));
        env.AsAdmin();
        first = await env.Rooms.TransitionAsync(first.Id, OfficeWorkflowAction.Approve, new(first.VersionNumber));
        second = await env.Rooms.TransitionAsync(second.Id, OfficeWorkflowAction.Approve, new(second.VersionNumber));
        first = await env.Rooms.TransitionAsync(first.Id, OfficeWorkflowAction.Issue, new(first.VersionNumber));
        env.Time.UtcNow = first.EndsAt.AddMinutes(1);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.TransitionAsync(second.Id, OfficeWorkflowAction.Issue, new(second.VersionNumber)));
        await env.Rooms.TransitionAsync(first.Id, OfficeWorkflowAction.Return, new(first.VersionNumber));
        Assert.Equal(MeetingBookingStatus.InUse, (await env.Rooms.TransitionAsync(second.Id, OfficeWorkflowAction.Issue, new(second.VersionNumber))).Status);
    }

    [Theory]
    [InlineData(-1, 60)]
    [InlineData(5, 5)]
    [InlineData(5, 600)]
    [InlineData(200000, 60)]
    public async Task Booking_ShouldRejectInvalidTimeWindows(int startMinutes, int durationMinutes)
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        env.Actor.CurrentUser = env.Employee;
        var start = env.Clock.UtcNow.AddMinutes(startMinutes);
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.Rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "会议", 2, start, start.AddMinutes(durationMinutes))));
    }

    [Fact]
    public async Task Requests_ShouldBeIdempotent_AndRejectChangedPayloads()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        env.Actor.CurrentUser = env.Employee;
        var request = new MeetingBookingCreateRequest(Guid.NewGuid(), room.Id, "项目会议", 2, env.Clock.UtcNow.AddMinutes(15), env.Clock.UtcNow.AddHours(1));
        var first = await env.Rooms.CreateBookingAsync(request);
        Assert.Equal(first.Id, (await env.Rooms.CreateBookingAsync(request)).Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.CreateBookingAsync(request with { Title = "其他会议" }));
        Assert.Single((await env.Rooms.HistoryAsync(first.Id)).Items);
    }

    [Fact]
    public async Task CompanyBoundary_ShouldApplyEvenToAllScopeAndAdministrators()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var booking = await env.BookAsync(room.Id);
        var supply = await env.SupplyAsync();
        env.Actor.CurrentUser = env.Outsider;
        Assert.Empty((await env.Rooms.QueryRoomsAsync(new())).Items);
        Assert.Empty((await env.Rooms.QueryBookingsAsync(new(MineOnly: false))).Items);
        Assert.Empty((await env.Supplies.QuerySuppliesAsync(new())).Items);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.Rooms.AvailabilityAsync(room.Id, env.Clock.UtcNow, env.Clock.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.Supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 10, supply.VersionNumber, "补充"), false));
    }

    [Fact]
    public async Task Employee_ShouldNotManageResourcesOrApprove_AndAdminCannotSelfApprove()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var booking = await env.BookAsync(room.Id);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber)));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.Rooms.SaveRoomAsync(0, OfficeTestEnvironment.RoomRequest()));
        env.AsAdmin();
        var own = await env.Rooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "本人会议", 2, booking.EndsAt, booking.EndsAt.AddHours(1)));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.Rooms.TransitionAsync(own.Id, OfficeWorkflowAction.Approve, new(own.VersionNumber)));
    }

    [Fact]
    public async Task Room_ShouldNotBeDisabledWhileBookingsOrKeysRemain()
    {
        using var env = new OfficeTestEnvironment();
        var room = await env.RoomAsync();
        var booking = await env.BookAsync(room.Id);
        env.AsAdmin();
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.SaveRoomAsync(room.Id,
            OfficeTestEnvironment.RoomRequest() with { IsActive = false, ExpectedVersion = room.VersionNumber }));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Rooms.SaveRoomAsync(room.Id,
            OfficeTestEnvironment.RoomRequest() with { Capacity = 1, ExpectedVersion = room.VersionNumber }));
        await env.Rooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Cancel, new(booking.VersionNumber, "会议取消"));
        Assert.False((await env.Rooms.SaveRoomAsync(room.Id,
            OfficeTestEnvironment.RoomRequest() with { IsActive = false, ExpectedVersion = room.VersionNumber })).IsActive);
    }

    [Fact]
    public async Task StockApproval_ShouldReserve_AndCancelShouldRelease_WithoutNegativeStock()
    {
        using var env = new OfficeTestEnvironment();
        var supply = await env.SupplyAsync();
        var first = await env.RequestAsync(supply.Id, 7);
        var second = await env.RequestAsync(supply.Id, 6);
        env.AsAdmin();
        first = await env.Supplies.TransitionAsync(first.Id, OfficeWorkflowAction.Approve, new(first.VersionNumber, 0));
        supply = (await env.Supplies.QuerySuppliesAsync(new())).Items.Single();
        Assert.Equal(10, supply.StockQuantity);
        Assert.Equal(3, supply.AvailableQuantity);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.TransitionAsync(second.Id, OfficeWorkflowAction.Approve, new(second.VersionNumber, 0)));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 6, supply.VersionNumber, "盘点"), true));
        env.Actor.CurrentUser = env.Employee;
        await env.Supplies.TransitionAsync(first.Id, OfficeWorkflowAction.Cancel, new(first.VersionNumber, 0, "不再需要"));
        env.AsAdmin();
        second = await env.Supplies.TransitionAsync(second.Id, OfficeWorkflowAction.Approve, new(second.VersionNumber, 0));
        await env.Supplies.TransitionAsync(second.Id, OfficeWorkflowAction.Issue, new(second.VersionNumber, 0));
        supply = (await env.Supplies.QuerySuppliesAsync(new())).Items.Single();
        Assert.Equal(4, supply.StockQuantity);
        Assert.Equal(0, supply.ReservedQuantity);
        Assert.Equal([-6, 10], (await env.Supplies.StockHistoryAsync(supply.Id, 1, 20)).Items.Select(item => item.QuantityDelta));
    }

    [Fact]
    public async Task ReturnableSupplies_ShouldSupportPartialReturns_WithoutDuplicateCredits()
    {
        using var env = new OfficeTestEnvironment();
        var supply = await env.SupplyAsync(returnable: true);
        var request = await env.RequestAsync(supply.Id, 4, env.Clock.Today.AddDays(7));
        env.AsAdmin();
        request = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Approve, new(request.VersionNumber, 0));
        request = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Issue, new(request.VersionNumber, 0));
        var partial = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(request.VersionNumber, 1));
        Assert.Equal(SupplyRequestStatus.Issued, partial.Status);
        Assert.Equal(1, partial.ReturnedQuantity);
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(request.VersionNumber, 1)));
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(partial.VersionNumber, 4)));
        var completed = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(partial.VersionNumber, 3));
        Assert.Equal(SupplyRequestStatus.Returned, completed.Status);
        Assert.Equal(10, (await env.Supplies.QuerySuppliesAsync(new())).Items.Single().StockQuantity);
        Assert.Equal(4, (await env.Supplies.StockHistoryAsync(supply.Id, 1, 20)).TotalCount);
    }

    [Fact]
    public async Task Restock_ShouldBeIdempotent_AndStocktakeShouldWriteACompensatingLedgerEntry()
    {
        using var env = new OfficeTestEnvironment();
        var supply = await env.SupplyAsync();
        var operation = new OfficeStockRequest(Guid.NewGuid(), 5, supply.VersionNumber, "采购补充");
        var movement = await env.Supplies.ChangeStockAsync(supply.Id, operation, false);
        Assert.Equal(movement.Id, (await env.Supplies.ChangeStockAsync(supply.Id, operation, false)).Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.ChangeStockAsync(supply.Id, operation with { Quantity = 8 }, false));
        supply = (await env.Supplies.QuerySuppliesAsync(new())).Items.Single();
        Assert.Equal(15, supply.StockQuantity);
        var adjusted = await env.Supplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 12, supply.VersionNumber, "破损盘减"), true);
        Assert.Equal(-3, adjusted.QuantityDelta);
        Assert.Equal(12, adjusted.StockAfter);
    }

    [Fact]
    public async Task ExpiredBorrowingRequest_ShouldNotBeIssued_AndMayReleaseItsReservation()
    {
        using var env = new OfficeTestEnvironment();
        var supply = await env.SupplyAsync(returnable: true);
        var request = await env.RequestAsync(supply.Id, 2, env.Clock.Today);
        env.AsAdmin();
        request = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Approve, new(request.VersionNumber, 0));
        env.Time.UtcNow = env.Time.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Issue, new(request.VersionNumber, 0)));
        await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Cancel, new(request.VersionNumber, 0, "归还期限已过"));
        Assert.Equal(10, (await env.Supplies.QuerySuppliesAsync(new())).Items.Single().AvailableQuantity);
    }

    [Fact]
    public async Task SupplyRequest_ShouldBeIdempotent_AndProtectHistoricalUnitAndType()
    {
        using var env = new OfficeTestEnvironment();
        var supply = await env.SupplyAsync();
        env.Actor.CurrentUser = env.Employee;
        var input = new SupplyRequestCreateRequest(Guid.NewGuid(), supply.Id, 2, "日常办公", null);
        var request = await env.Supplies.CreateRequestAsync(input);
        Assert.Equal(request.Id, (await env.Supplies.CreateRequestAsync(input)).Id);
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.CreateRequestAsync(input with { Quantity = 3 }));
        env.AsAdmin();
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.SaveSupplyAsync(supply.Id,
            new(supply.Name, "箱", "", "", false, true, 2, supply.VersionNumber)));
        request = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Approve, new(request.VersionNumber, 0));
        request = await env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Issue, new(request.VersionNumber, 0));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.Supplies.TransitionAsync(request.Id, OfficeWorkflowAction.Return, new(request.VersionNumber, 1)));
    }

    [Fact]
    public async Task Queries_ShouldBePaged_RejectUnknownStatus_AndRespectCancellation()
    {
        using var env = new OfficeTestEnvironment();
        await env.RoomAsync();
        await env.Rooms.SaveRoomAsync(0, OfficeTestEnvironment.RoomRequest() with { Name = "小会议室" });
        var page = await env.Rooms.QueryRoomsAsync(new(PageSize: 1));
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.True(page.HasNextPage);
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.Rooms.QueryBookingsAsync(new(Status: "all-unsafe")));
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.Supplies.QueryRequestsAsync(new(PageNumber: 0)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Rooms.QueryRoomsAsync(new(), cancelled.Token));
    }

    [Fact]
    public async Task DesktopOrMissingCompany_ShouldFailClosed()
    {
        using var env = new OfficeTestEnvironment();
        var desktop = new MeetingRoomService(new OfficeServiceContext(env.Database,
            new BusinessDataAccessScope(new DatabaseConnectionSettings(), env.Actor), env.Clock, OfficeOperatingMode.Disabled));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => desktop.QueryRoomsAsync(new()));
        env.Actor.CurrentUser = new User { Id = 9, Role = UserRoleCatalog.Admin };
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.Rooms.QueryRoomsAsync(new()));
    }

    private sealed class OfficeTestEnvironment : IDisposable
    {
        public SqliteTestDatabase Database { get; } = new();
        public MutableTimeProvider Time { get; } = new();
        public IBusinessClock Clock { get; }
        public MutableUserContext Actor { get; } = new();
        public User Employee { get; } = new() { Id = 1, Username = "employee", FullName = "员工一", CompanyScope = "C1", DepartmentId = "D1" };
        public User Other { get; } = new() { Id = 3, Username = "other", FullName = "员工二", CompanyScope = "C1", DepartmentId = "D2" };
        public User Admin { get; } = new() { Id = 2, Username = "admin", CompanyScope = "C1", DepartmentId = "D1", Role = UserRoleCatalog.Admin };
        public User Outsider { get; } = new() { Id = 4, Username = "other-admin", CompanyScope = "C2", Role = UserRoleCatalog.Admin };
        public MeetingRoomService Rooms { get; }
        public OfficeSupplyService Supplies { get; }

        public OfficeTestEnvironment()
        {
            Clock = new BusinessClock(Time, "Asia/Shanghai");
            var scope = new BusinessDataAccessScope(new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider }, Actor);
            var office = new OfficeServiceContext(Database, scope, Clock, OfficeOperatingMode.Team);
            Rooms = new MeetingRoomService(office);
            Supplies = new OfficeSupplyService(office);
            using var db = Database.CreateDbContext();
            db.OrganizationCompanies.AddRange(new OrganizationCompany { Code = "C1", Name = "公司一" }, new OrganizationCompany { Code = "C2", Name = "公司二" });
            db.Users.AddRange(Employee, Other, Admin, Outsider);
            db.SaveChanges();
            AsAdmin();
        }

        public void AsAdmin() => Actor.CurrentUser = Admin;
        public static MeetingRoomSaveRequest RoomRequest() => new("第一会议室", "三楼", "投影、白板", 6, 8, 90, true, true, 0);
        public Task<MeetingRoomRecord> RoomAsync() { AsAdmin(); return Rooms.SaveRoomAsync(0, RoomRequest()); }
        public Task<MeetingBookingRecord> BookAsync(int roomId)
        {
            Actor.CurrentUser = Employee;
            return Rooms.CreateBookingAsync(new(Guid.NewGuid(), roomId, "项目例会", 2, Clock.UtcNow.AddMinutes(15), Clock.UtcNow.AddHours(1)));
        }
        public async Task<OfficeSupplyRecord> SupplyAsync(bool returnable = false)
        {
            AsAdmin();
            var item = await Supplies.SaveSupplyAsync(0, new(returnable ? "投影设备" : "签字笔", "件", "行政办公室", "", returnable, true, 3, 0));
            await Supplies.ChangeStockAsync(item.Id, new(Guid.NewGuid(), 10, item.VersionNumber, "首次入库"), false);
            return (await Supplies.QuerySuppliesAsync(new())).Items.Single();
        }
        public Task<OfficeSupplyRequestRecord> RequestAsync(int supplyId, int quantity, DateOnly? due = null)
        {
            Actor.CurrentUser = Employee;
            return Supplies.CreateRequestAsync(new(Guid.NewGuid(), supplyId, quantity, "日常办公", due));
        }
        public void Dispose() => Database.Dispose();
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 7, 1, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class MutableUserContext : ICurrentUserContext
    {
        public User? CurrentUser { get; set; }
    }
}
