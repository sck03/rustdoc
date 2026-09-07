using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class PersonnelService
{
    public Task<PagedResult<PersonnelDirectoryRecord>> QueryAsync(PersonnelQuery query, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, async (db, actor, token) =>
        {
            var status = Status<EmploymentStatus>(query.Status);
            var people = office.Requests(db.PersonnelEmployees.AsNoTracking(), actor, Resource);
            if (status == EmploymentStatus.Departed || query.AttentionOnly)
            {
                if (!office.HasPermission(Resource, PermissionAction.ViewDetails, actor))
                    throw new PermissionDeniedException("查看离职档案或到期提醒需要人事档案权限。");
                people = office.Requests(people, actor, Resource, PermissionAction.ViewDetails);
            }
            people = status.HasValue ? people.Where(item => item.Status == status) : people.Where(item => item.Status != EmploymentStatus.Departed);
            string keyword = Text(query.Keyword, "搜索词", 100).ToUpperInvariant();
            if (keyword.Length > 0) people = people.Where(item => item.EmployeeNumberNormalized.Contains(keyword) ||
                item.FullName.ToUpper().Contains(keyword) || item.JobTitle.ToUpper().Contains(keyword) || item.WorkEmail.ToUpper().Contains(keyword));
            string department = Text(query.DepartmentId, "部门", 50);
            if (department.Length > 0) people = people.Where(item => item.DepartmentId == department);
            if (query.AttentionOnly)
            {
                var soon = office.Clock.Today.AddDays(30);
                people = people.Where(item => item.Status != EmploymentStatus.Departed &&
                    (item.Status == EmploymentStatus.Probation && item.ProbationEndsOn <= soon || item.ContractEndsOn <= soon));
            }
            var page = await PageAsync(people.Include(item => item.Department).OrderBy(item => item.EmployeeNumberNormalized).ThenBy(item => item.Id),
                query.PageNumber, query.PageSize, token);
            return new PagedResult<PersonnelDirectoryRecord>(page.Items.Select(item => Directory(item, actor)).ToList(), page.TotalCount, page.PageNumber, page.PageSize);
        }, cancellationToken);

    public Task<PersonnelOptions> OptionsAsync(CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, async (db, actor, token) => new PersonnelOptions(
            await db.OrganizationDepartments.AsNoTracking().Where(item => item.CompanyCode == actor.CompanyScope)
                .OrderByDescending(item => item.IsActive).ThenBy(item => item.Name)
                .Select(item => new PersonnelDepartmentRecord(item.Code, item.Name, item.IsActive)).ToListAsync(token),
            office.CanRecord(new PersonnelEmployee { CompanyScope = actor.CompanyScope!, DepartmentId = actor.DepartmentId ?? "" }, actor, Resource, PermissionAction.Create) &&
                office.CanRecord(new PersonnelEmployee { CompanyScope = actor.CompanyScope!, DepartmentId = actor.DepartmentId ?? "" }, actor, Resource, PermissionAction.ViewDetails)), cancellationToken);

    public Task<PersonnelRecord> GetAsync(int id, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.ViewDetails, false, async (db, actor, token) =>
            await RecordAsync(db, await EmployeeAsync(db, id, actor, PermissionAction.ViewDetails, false, token), actor, token), cancellationToken);

    public Task<PagedResult<PersonnelEventRecord>> HistoryAsync(int id, int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.ViewDetails, false, async (db, actor, token) =>
        {
            await EmployeeAsync(db, id, actor, PermissionAction.ViewDetails, false, token);
            return await PageAsync(db.PersonnelEvents.AsNoTracking().Where(item => item.EmployeeId == id && item.CompanyScope == actor.CompanyScope)
                .OrderByDescending(item => item.Id).Select(item => new PersonnelEventRecord(item.Id, item.Action, item.EffectiveDate,
                    item.ActorName, item.Summary, item.Note, item.CreatedAt)), pageNumber, pageSize, token);
        }, cancellationToken);

    public Task<PersonnelClearance> ClearanceAsync(int id, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.ViewDetails, false, async (db, actor, token) =>
        {
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.ViewDetails, false, token);
            return await ReadClearanceAsync(db, employee, token);
        }, cancellationToken);

    private PersonnelDirectoryRecord Directory(PersonnelEmployee employee, User actor) => new(employee.Id, employee.EmployeeNumber,
        employee.FullName, employee.DepartmentId, employee.Department?.Name ?? employee.DepartmentId, employee.JobTitle,
        employee.WorkEmail, employee.WorkPhone, employee.WorkLocation, employee.Status,
        office.CanRecord(employee, actor, Resource, PermissionAction.ViewDetails));

    private async Task<PersonnelRecord> RecordAsync(AppDbContext db, PersonnelEmployee employee, User actor, CancellationToken token)
    {
        await db.Entry(employee).Reference(item => item.Department).LoadAsync(token);
        await db.Entry(employee).Reference(item => item.Account).LoadAsync(token);
        var account = employee.Account;
        return new PersonnelRecord(Directory(employee, actor), Profile(employee), employee.EmploymentType, employee.HireDate,
            employee.LastEffectiveDate, employee.ProbationEndsOn, employee.ContractEndsOn, employee.ConfirmedOn, employee.DepartedOn,
            account == null ? null : new PersonnelAccountRecord(account.Id, account.Username, account.FullName ?? "", account.DepartmentId ?? "", account.IsActive, account.VersionNumber),
            employee.VersionNumber,
            employee.Status != EmploymentStatus.Departed && office.CanRecord(employee, actor, Resource, PermissionAction.Edit),
            employee.OwnerUserId != actor.Id && office.CanRecord(employee, actor, Resource, PermissionAction.Transition),
            !office.IsLocalRegister && employee.Status != EmploymentStatus.Departed && !employee.OwnerUserId.HasValue &&
                BusinessDataAccessScope.CanViewAllBusinessData(actor) && office.CanRecord(employee, actor, Resource, PermissionAction.Assign));
    }

    private static async Task<PersonnelClearance> ReadClearanceAsync(AppDbContext db, PersonnelEmployee employee, CancellationToken token)
    {
        var meetings = OpenMeetings(db, employee.OwnerUserId, employee.Id).Where(item => item.CompanyScope == employee.CompanyScope);
        var supplies = OpenSupplies(db, employee.OwnerUserId, employee.Id).Where(item => item.CompanyScope == employee.CompanyScope);
        var meetingItems = await (from booking in meetings
                                  join room in db.MeetingRooms on booking.MeetingRoomId equals room.Id
                                  orderby booking.Id
                                  select new PersonnelClearanceItem("rooms", booking.Id, room.Name, booking.Status.ToString(), booking.Status == MeetingBookingStatus.InUse ? 1 : 0))
            .Take(20).ToListAsync(token);
        var supplyItems = await (from request in supplies
                                 join supply in db.OfficeSupplies on request.OfficeSupplyId equals supply.Id
                                 orderby request.Id
                                 select new PersonnelClearanceItem("supplies", request.Id, supply.Name, request.Status.ToString(),
                                     request.Status == SupplyRequestStatus.Issued ? request.Quantity - request.ReturnedQuantity : 0)).Take(20).ToListAsync(token);
        return new(await meetings.CountAsync(token), await supplies.CountAsync(token), [.. meetingItems, .. supplyItems]);
    }

    internal static IQueryable<MeetingBooking> OpenMeetings(AppDbContext db, int? userId, int? employeeId = null) => db.MeetingBookings.Where(item =>
        (employeeId.HasValue && item.EmployeeId == employeeId || userId.HasValue && item.EmployeeId == null && item.OwnerUserId == userId) &&
        (item.Status == MeetingBookingStatus.Pending || item.Status == MeetingBookingStatus.Approved || item.Status == MeetingBookingStatus.InUse));

    internal static IQueryable<OfficeSupplyRequest> OpenSupplies(AppDbContext db, int? userId, int? employeeId = null) => db.OfficeSupplyRequests.Where(item =>
        (employeeId.HasValue && item.EmployeeId == employeeId || userId.HasValue && item.EmployeeId == null && item.OwnerUserId == userId) &&
        (item.Status == SupplyRequestStatus.Pending || item.Status == SupplyRequestStatus.Approved ||
            item.Status == SupplyRequestStatus.Issued && item.ReturnedQuantity < item.Quantity && db.OfficeSupplies.Any(supply => supply.Id == item.OfficeSupplyId && supply.IsReturnable)));

    internal static async Task DemandClearanceAsync(AppDbContext db, int? userId, CancellationToken token, int? employeeId = null)
    {
        if (await OpenMeetings(db, userId, employeeId).AnyAsync(token) || await OpenSupplies(db, userId, employeeId).AnyAsync(token))
            throw new ResourceConflictException("仍有未结清的预约或领用申请，请先取消未交接申请、归还钥匙和借用物品，再办理组织变更或离职。");
    }
}
