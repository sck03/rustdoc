using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Worklist;

public sealed class OfficeWorklistSource(BusinessDataAccessScope access, IRuntimePermissionAvailability runtime) : IWorklistSource
{
    public IEnumerable<WorklistQueryGroup> Build(AppDbContext db, User actor)
    {
        if (runtime.OfficeMode == OfficeOperatingMode.Disabled || string.IsNullOrWhiteSpace(actor.CompanyScope)) yield break;
        bool local = runtime.OfficeMode == OfficeOperatingMode.LocalRegister;
        const string rooms = PermissionResourceCatalog.OfficeRooms;
        const string supplies = PermissionResourceCatalog.OfficeSupplies;
        const string people = PermissionResourceCatalog.OfficePeople;
        if (Allowed(rooms, PermissionAction.View, actor))
        {
            var meetings = Scope(db.MeetingBookings.AsNoTracking(), db, actor, rooms, PermissionAction.View);
            if (!local && Allowed(rooms, PermissionAction.Approve, actor))
                yield return new WorklistQueryGroup("meeting-approval", "会议室待审批", Scope(meetings, db, actor, rooms, PermissionAction.Approve)
                    .Where(item => item.Status == MeetingBookingStatus.Pending && item.OwnerUserId != actor.Id)
                    .Select(item => new WorklistRow { RecordId = item.Id, Title = item.Title, Description = item.ApplicantName + " · 待审批", DueAt = item.StartsAt, DueDate = null }), WorklistDeadlineKind.Instant);
            var mine = meetings.Where(item => local || item.OwnerUserId == actor.Id);
            yield return new WorklistQueryGroup("meeting-collection", local ? "会议室待交接" : "我的会议室待使用", mine
                .Where(item => item.Status == MeetingBookingStatus.Approved)
                .Select(item => new WorklistRow { RecordId = item.Id, Title = item.Title, Description = item.ApplicantName + " · 待领取钥匙", DueAt = item.StartsAt, DueDate = null }), WorklistDeadlineKind.Instant);
            yield return new WorklistQueryGroup("meeting-return", local ? "钥匙待归还" : "我的钥匙待归还", mine
                .Where(item => item.Status == MeetingBookingStatus.InUse)
                .Select(item => new WorklistRow { RecordId = item.Id, Title = item.Title, Description = item.ApplicantName + " · 待归还钥匙", DueAt = item.EndsAt, DueDate = null }), WorklistDeadlineKind.Instant);
        }
        if (Allowed(supplies, PermissionAction.View, actor))
        {
            var requests = Scope(db.OfficeSupplyRequests.AsNoTracking(), db, actor, supplies, PermissionAction.View);
            if (!local && Allowed(supplies, PermissionAction.Approve, actor))
                yield return new WorklistQueryGroup("supply-approval", "物品待审批", Scope(requests, db, actor, supplies, PermissionAction.Approve)
                    .Where(item => item.Status == SupplyRequestStatus.Pending && item.OwnerUserId != actor.Id)
                    .Select(item => new WorklistRow { RecordId = item.Id, Title = item.ApplicantName + " · 物品领用", Description = item.Purpose, DueDate = null, DueAt = null }));
            var mine = requests.Where(item => local || item.OwnerUserId == actor.Id);
            yield return new WorklistQueryGroup("supply-collection", local ? "物品待发放" : "我的物品待领用", from item in mine
                                                                                                  join supply in db.OfficeSupplies on item.OfficeSupplyId equals supply.Id
                                                                                                  where item.Status == SupplyRequestStatus.Approved
                                                                                                  select new WorklistRow { RecordId = item.Id, Title = supply.Name, Description = item.ApplicantName + " · " + item.Purpose, DueDate = null, DueAt = null });
            yield return new WorklistQueryGroup("supply-return", local ? "借用物品待归还" : "我的借用物品待归还", from item in mine
                                                                                                  join supply in db.OfficeSupplies on item.OfficeSupplyId equals supply.Id
                                                                                                  where item.Status == SupplyRequestStatus.Issued && supply.IsReturnable && item.ReturnedQuantity < item.Quantity
                                                                                                  select new WorklistRow { RecordId = item.Id, Title = supply.Name, Description = item.ApplicantName + " · " + item.Purpose, DueDate = item.ReturnDueDate, DueAt = null }, WorklistDeadlineKind.Date);
        }
        if (Allowed(people, PermissionAction.View, actor) && Allowed(people, PermissionAction.ViewDetails, actor))
        {
            var employees = Scope(Scope(db.PersonnelEmployees.AsNoTracking(), db, actor, people, PermissionAction.View), db, actor, people, PermissionAction.ViewDetails)
                .Where(item => item.Status != EmploymentStatus.Departed);
            yield return new WorklistQueryGroup("probation-end", "试用期到期", employees
                .Where(item => item.Status == EmploymentStatus.Probation && item.ProbationEndsOn != null)
                .Select(item => new WorklistRow { RecordId = item.Id, Title = item.FullName, Description = item.EmployeeNumber + " · 试用期", DueDate = item.ProbationEndsOn, DueAt = null }), WorklistDeadlineKind.Date);
            yield return new WorklistQueryGroup("contract-end", "劳动合同到期", employees.Where(item => item.ContractEndsOn != null)
                .Select(item => new WorklistRow { RecordId = item.Id, Title = item.FullName, Description = item.EmployeeNumber + " · 劳动合同", DueDate = item.ContractEndsOn, DueAt = null }), WorklistDeadlineKind.Date);
        }
    }

    private bool Allowed(string resource, string action, User actor) =>
        runtime.IsPermissionAvailable(resource, action) && access.HasPermission(resource, action, actor);

    private IQueryable<T> Scope<T>(IQueryable<T> query, AppDbContext db, User actor, string resource, string action) where T : class, IBusinessOwnedEntity =>
        access.ApplyBusinessScope(query.Where(item => item.CompanyScope == actor.CompanyScope &&
            db.OrganizationCompanies.Any(company => company.Code == actor.CompanyScope && company.IsActive)), resource, action, actor);
}
