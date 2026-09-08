using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Worklist;

public sealed class FollowUpWorklistSource(BusinessDataAccessScope access, IRuntimePermissionAvailability runtime) : IWorklistSource
{
    public IEnumerable<WorklistQueryGroup> Build(AppDbContext db, User actor)
    {
        const string resource = PermissionResourceCatalog.CrmFollowUps;
        if (!runtime.IsPermissionAvailable(resource, PermissionAction.Complete) ||
            !access.HasPermission(resource, PermissionAction.View, actor) || !access.HasPermission(resource, PermissionAction.Complete, actor) ||
            !access.HasPermission(PermissionResourceCatalog.CrmCustomers, PermissionAction.View, actor)) yield break;
        var followUps = access.ApplyCrmFollowUpScope(access.ApplyCrmFollowUpScope(db.CrmFollowUps.AsNoTracking(), actor), actor, PermissionAction.Complete);
        var customers = access.ApplyCrmCustomerScope(db.CrmCustomers.AsNoTracking(), actor);
        bool local = !access.UsesPostgreSql;
        yield return new WorklistQueryGroup("customer-follow-up", "客户跟进", from item in followUps
                                                                          join customer in customers on item.CrmCustomerId equals customer.Id
                                                                          where !item.IsCompleted && (local || item.OwnerUserId == actor.Id)
                                                                          select new WorklistRow
                                                                          {
                                                                              RecordId = item.Id,
                                                                              ParentId = customer.Id,
                                                                              Title = customer.Name,
                                                                              Description = item.NextAction == "" ? item.Summary : item.NextAction,
                                                                              DueAt = item.NextFollowUpAt,
                                                                              DueDate = null
                                                                          }, WorklistDeadlineKind.Instant);
    }
}
