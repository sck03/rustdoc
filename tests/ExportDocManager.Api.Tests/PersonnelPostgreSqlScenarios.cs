using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests;

internal static class PersonnelPostgreSqlScenarios
{
    internal static async Task RunAsync(IDbContextFactory<AppDbContext> factory, DatabaseConnectionSettings settings)
    {
        const string company = "PEOPLE-PG", firstDepartment = "PEOPLE-PG-1", secondDepartment = "PEOPLE-PG-2";
        var admin = new User { Username = "people-pg-admin", FullName = "人事测试管理员", Role = UserRoleCatalog.Admin, CompanyScope = company, DepartmentId = firstDepartment, PasswordHash = "test-only" };
        var account = new User { Username = "people-pg-employee", FullName = "并发测试员工", CompanyScope = company, DepartmentId = firstDepartment, PasswordHash = "test-only" };
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationCompanies.Add(new() { Code = company, Name = "人员并发测试公司" });
            db.OrganizationDepartments.AddRange(new OrganizationDepartment { Code = firstDepartment, CompanyCode = company, Name = "业务部" },
                new OrganizationDepartment { Code = secondDepartment, CompanyCode = company, Name = "运营部" });
            db.Users.AddRange(admin, account);
            await db.SaveChangesAsync();
        }
        var clock = new BusinessClock(TimeProvider.System, "Asia/Shanghai");
        OfficeServiceContext Context(User actor) => new(factory, new BusinessDataAccessScope(settings, new PersonnelUser(actor)), clock, OfficeOperatingMode.Team);
        var people = new PersonnelService(Context(admin));
        var create = new PersonnelCreateRequest(Guid.NewGuid(), "EMP-PG-001", firstDepartment, "业务专员", EmploymentType.FullTime,
            clock.Today.AddDays(-1), false, null, null, new("并发测试员工"));
        var registrations = await RaceAsync(8, _ => people.CreateAsync(create));
        Assert.All(registrations, result => Assert.IsType<PersonnelRecord>(result));
        var person = (PersonnelRecord)registrations[0];
        Assert.All(registrations, result => Assert.Equal(person.Employee.Id, ((PersonnelRecord)result).Employee.Id));
        Assert.Single((await people.HistoryAsync(person.Employee.Id, 1, 24)).Items);
        person = (await people.LinkAccountAsync(person.Employee.Id, new(person.VersionNumber, account.Id, account.VersionNumber))).Record;
        var updates = await RaceAsync(2, _ => people.TransitionAsync(person.Employee.Id, PersonnelAction.Transfer,
            new(person.VersionNumber, clock.Today, "并发调岗", secondDepartment, "运营专员")));
        Assert.Single(updates.OfType<PersonnelChangeResult>());
        Assert.Single(updates.OfType<ServiceConcurrencyException>());
        person = updates.OfType<PersonnelChangeResult>().Single().Record;
        Assert.Equal(secondDepartment, person.Account!.DepartmentId);
        await using (var db = factory.CreateDbContext()) account = await db.Users.AsNoTracking().SingleAsync(item => item.Id == account.Id);
        var employeeRooms = new MeetingRoomService(Context(account));
        var rooms = new MeetingRoomService(Context(admin));
        var room = await rooms.SaveRoomAsync(0, new("人事交接会议室", "测试办公室", "", 10, 4, 30, true, true, 0));
        var results = await RaceAsync<object>(2, async index => index == 0
            ? await people.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, clock.Today, "并发离职"))
            : await employeeRooms.CreateBookingAsync(new(Guid.NewGuid(), room.Id, "并发申请", 2, clock.UtcNow.AddMinutes(15), clock.UtcNow.AddHours(1))));
        await using (var db = factory.CreateDbContext())
        {
            var saved = await db.PersonnelEmployees.SingleAsync(item => item.Id == person.Employee.Id);
            var bookings = await db.MeetingBookings.Where(item => item.OwnerUserId == account.Id).ToListAsync();
            if (saved.Status == EmploymentStatus.Departed)
            {
                Assert.Empty(bookings);
                Assert.Single(results.OfType<PersonnelChangeResult>());
                Assert.Single(results.OfType<PermissionDeniedException>());
                Assert.False((await db.Users.SingleAsync(item => item.Id == account.Id)).IsActive);
            }
            else
            {
                Assert.Single(bookings);
                Assert.Single(results.OfType<MeetingBookingRecord>());
                Assert.Single(results.OfType<ResourceConflictException>());
            }
        }
        Console.WriteLine("Personnel PostgreSQL: 8 simultaneous idempotent hires, competing transfers and departure/request race passed.");
    }

    private static async Task<object[]> RaceAsync<T>(int count, Func<int, Task<T>> action) where T : class
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async index =>
        {
            await gate.Task;
            try { return (object)await action(index); }
            catch (ResourceConflictException exception) { return exception; }
            catch (PermissionDeniedException exception) { return exception; }
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
    private sealed class PersonnelUser(User user) : ICurrentUserContext { public User CurrentUser { get; } = user; }
}
