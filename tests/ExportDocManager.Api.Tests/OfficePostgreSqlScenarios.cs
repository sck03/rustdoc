using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests;

internal static class OfficePostgreSqlScenarios
{
    internal static async Task RunAsync(IDbContextFactory<AppDbContext> factory, DatabaseConnectionSettings settings, User administrator)
    {
        const string company = "OFFICE-PG";
        var employee = new User { Username = "office-pg-employee", CompanyScope = company, FullName = "行政并发测试员工", PasswordHash = "test-fixture-only" };
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationCompanies.Add(new OrganizationCompany { Code = company, Name = "行政并发测试公司" });
            db.Users.Add(employee);
            await db.SaveChangesAsync();
        }
        var admin = new User { Id = administrator.Id, Role = UserRoleCatalog.Admin, Username = administrator.Username, CompanyScope = company };
        var clock = new BusinessClock(TimeProvider.System, "Asia/Shanghai");
        OfficeServiceContext Context(User actor) => new(factory, new BusinessDataAccessScope(settings, new FixedOfficeUser(actor)), clock, OfficeOperatingMode.Team);
        var adminRooms = new MeetingRoomService(Context(admin));
        var employeeRooms = new MeetingRoomService(Context(employee));
        var adminSupplies = new OfficeSupplyService(Context(admin));
        var employeeSupplies = new OfficeSupplyService(Context(employee));
        var room = await adminRooms.SaveRoomAsync(0, new("并发会议室", "测试楼", "", 10, 8, 90, true, true, 0));
        var start = clock.UtcNow.AddMinutes(15);
        var bookings = await RaceAsync(12, _ => employeeRooms.CreateBookingAsync(
            new(Guid.NewGuid(), room.Id, "同一时段竞争", 2, start, start.AddHours(1))));
        var booking = Assert.Single(bookings.OfType<MeetingBookingRecord>());
        Assert.Equal(11, bookings.Count(item => item is ResourceConflictException));
        Assert.Single(await employeeRooms.AvailabilityAsync(room.Id, start.AddHours(-1), start.AddDays(1)));
        Assert.Single((await employeeRooms.QueryBookingsAsync(new())).Items);
        booking = await adminRooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Approve, new(booking.VersionNumber));
        var handovers = await RaceAsync(2, _ => adminRooms.TransitionAsync(booking.Id, OfficeWorkflowAction.Issue, new(booking.VersionNumber)));
        Assert.Single(handovers.OfType<MeetingBookingRecord>());
        Assert.Single(handovers.OfType<ServiceConcurrencyException>());
        Assert.True((await adminRooms.QueryRoomsAsync(new())).Items.Single().InUse);

        var supply = await adminSupplies.SaveSupplyAsync(0, new("并发物品", "件", "测试仓库", "", false, true, 1, 0));
        await adminSupplies.ChangeStockAsync(supply.Id, new(Guid.NewGuid(), 5, supply.VersionNumber, "初始入库"), false);
        var requests = new List<OfficeSupplyRequestRecord>();
        for (int index = 0; index < 12; index++)
            requests.Add(await employeeSupplies.CreateRequestAsync(new(Guid.NewGuid(), supply.Id, 1, "并发领用", null)));
        var approvals = await RaceAsync(requests.Count, index => adminSupplies.TransitionAsync(
            requests[index].Id, OfficeWorkflowAction.Approve, new(requests[index].VersionNumber, 0)));
        Assert.Equal(5, approvals.OfType<OfficeSupplyRequestRecord>().Count());
        Assert.Equal(7, approvals.Count(item => item is ResourceConflictException));
        supply = (await adminSupplies.QuerySuppliesAsync(new())).Items.Single();
        Assert.Equal(5, supply.StockQuantity);
        Assert.Equal(5, supply.ReservedQuantity);
        Assert.Equal(0, supply.AvailableQuantity);

        var approved = approvals.OfType<OfficeSupplyRequestRecord>().First();
        var issues = await RaceAsync(2, _ => adminSupplies.TransitionAsync(approved.Id, OfficeWorkflowAction.Issue, new(approved.VersionNumber, 0)));
        Assert.Single(issues.OfType<OfficeSupplyRequestRecord>());
        Assert.Single(issues.OfType<ServiceConcurrencyException>());
        supply = (await adminSupplies.QuerySuppliesAsync(new())).Items.Single();
        Assert.Equal(4, supply.StockQuantity);
        var restock = new OfficeStockRequest(Guid.NewGuid(), 3, supply.VersionNumber, "重复提交的入库操作");
        var receipts = await RaceAsync(2, _ => adminSupplies.ChangeStockAsync(supply.Id, restock, false));
        Assert.Equal(2, receipts.OfType<OfficeStockMovementRecord>().Count());
        Assert.Single(receipts.OfType<OfficeStockMovementRecord>().Select(item => item.Id).Distinct());

        var cancellable = approvals.OfType<OfficeSupplyRequestRecord>().Skip(1).First();
        var cancelledOrIssued = await RaceAsync(2, index => adminSupplies.TransitionAsync(cancellable.Id,
            index == 0 ? OfficeWorkflowAction.Cancel : OfficeWorkflowAction.Issue,
            new(cancellable.VersionNumber, 0, index == 0 ? "取消竞争测试" : "发放竞争测试")));
        Assert.Single(cancelledOrIssued.OfType<OfficeSupplyRequestRecord>());
        Assert.Single(cancelledOrIssued.OfType<ServiceConcurrencyException>());
        await using (var db = factory.CreateDbContext())
        {
            var stock = await db.OfficeSupplies.SingleAsync(item => item.Id == supply.Id);
            int outstanding = await db.OfficeSupplyRequests.Where(item => item.OfficeSupplyId == supply.Id && item.Status == SupplyRequestStatus.Approved)
                .SumAsync(item => item.Quantity);
            int ledger = await db.OfficeStockMovements.Where(item => item.OfficeSupplyId == supply.Id).SumAsync(item => item.QuantityDelta);
            Assert.Equal(outstanding, stock.ReservedQuantity);
            Assert.Equal(ledger, stock.StockQuantity);
            Assert.InRange(stock.ReservedQuantity, 0, stock.StockQuantity);
        }
    }

    private static async Task<object[]> RaceAsync<T>(int count, Func<int, Task<T>> operation) where T : class
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async index =>
        {
            await gate.Task;
            try { return (object)await operation(index); }
            catch (ResourceConflictException exception) { return exception; }
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private sealed class FixedOfficeUser(User user) : ICurrentUserContext
    {
        public User CurrentUser { get; } = user;
    }
}
