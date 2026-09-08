using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Worklist;

public sealed class InvoiceWorklistSource(BusinessDataAccessScope access, IRuntimePermissionAvailability runtime) : IWorklistSource
{
    public IEnumerable<WorklistQueryGroup> Build(AppDbContext db, User actor)
    {
        const string resource = PermissionModuleCatalog.DocumentInvoices;
        if (!runtime.IsPermissionAvailable(resource, PermissionAction.Operate) ||
            !access.HasPermission(resource, PermissionAction.View, actor) || !access.HasPermission(resource, PermissionAction.Operate, actor)) yield break;
        var invoices = access.ApplyInvoiceScopeForPermission(access.ApplyInvoiceScope(db.Invoices.AsNoTracking(), actor), resource, PermissionAction.Operate, actor);
        bool local = !access.UsesPostgreSql;
        yield return new WorklistQueryGroup("invoice-review", "单据待核对", invoices
            .Where(item => item.Status == InvoiceStatusCatalog.Draft && (local || item.OwnerUserId == actor.Id))
            .Select(item => new WorklistRow { RecordId = item.Id, Title = item.InvoiceNo, Description = item.Type + " · " + item.CustomerNameEN, DueDate = null, DueAt = null }));
    }
}
