using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class OfficeSupplyService
{
    public Task<PagedResult<OfficeSupplyRecord>> QuerySuppliesAsync(OfficeResourceQuery query, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, (db, actor, token) =>
        {
            string keyword = Text(query.Keyword, "关键词", 120);
            var supplies = db.OfficeSupplies.AsNoTracking().Where(item => item.CompanyScope == actor.CompanyScope &&
                (query.IncludeInactive || item.IsActive) && (!query.LowStockOnly || item.StockQuantity - item.ReservedQuantity <= item.MinimumStock) &&
                (keyword == "" || item.Name.Contains(keyword) || item.Location.Contains(keyword)))
                .OrderByDescending(item => item.IsActive).ThenBy(item => item.Name).ThenBy(item => item.Id);
            return PageAsync(supplies.Select(item => new OfficeSupplyRecord(item.Id, item.Name, item.Unit, item.Location,
                item.Description, item.IsReturnable, item.IsActive, item.StockQuantity, item.ReservedQuantity, item.MinimumStock, item.VersionNumber)),
                query.PageNumber, query.PageSize, token);
        }, cancellationToken);

    public Task<PagedResult<OfficeSupplyRequestRecord>> QueryRequestsAsync(OfficeRequestQuery query, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, (db, actor, token) =>
        {
            ValidateRequestQuery(query);
            var status = Status<SupplyRequestStatus>(query.Status);
            var from = query.From?.ToUniversalTime();
            var to = query.To?.ToUniversalTime();
            var requests = office.Requests(db.OfficeSupplyRequests.AsNoTracking(), actor, Resource)
                .Where(item => (!query.MineOnly || item.OwnerUserId == actor.Id) && (!status.HasValue || item.Status == status.Value) &&
                    (!query.ResourceId.HasValue || item.OfficeSupplyId == query.ResourceId) &&
                    (!query.RequestId.HasValue || item.Id == query.RequestId) && (!query.ApplicantUserId.HasValue || item.OwnerUserId == query.ApplicantUserId) &&
                    (!query.EmployeeId.HasValue || item.EmployeeId == query.EmployeeId) &&
                    (!from.HasValue || item.CreatedAt >= from.Value) && (!to.HasValue || item.CreatedAt < to.Value))
                .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id);
            return PageAsync(RequestRecords(db, requests), query.PageNumber, query.PageSize, token);
        }, cancellationToken);

    public Task<PagedResult<OfficeStockMovementRecord>> StockHistoryAsync(int id, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Restock, false, async (db, actor, token) =>
        {
            if (!await db.OfficeSupplies.AnyAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token))
                throw new ResourceNotFoundException("物品不存在。");
            return await PageAsync(db.OfficeStockMovements.AsNoTracking().Where(item => item.OfficeSupplyId == id && item.CompanyScope == actor.CompanyScope)
                .OrderByDescending(item => item.Id).Select(item => new OfficeStockMovementRecord(item.Id, item.Kind,
                    item.QuantityDelta, item.StockAfter, item.ActorName, item.Note, item.CreatedAt)), pageNumber, pageSize, token);
        }, cancellationToken);

    public Task<PagedResult<OfficeRequestEventRecord>> HistoryAsync(int id, int pageNumber = 1, int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, async (db, actor, token) =>
        {
            var request = await db.OfficeSupplyRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("领用申请不存在。");
            office.DemandRecord(request, actor, Resource, PermissionAction.View);
            return await ReadEventsAsync(db.OfficeRequestEvents.AsNoTracking().Where(item => item.OfficeSupplyRequestId == id && item.CompanyScope == actor.CompanyScope), pageNumber, pageSize, token);
        }, cancellationToken);

    private static IQueryable<OfficeSupplyRequestRecord> RequestRecords(AppDbContext db, IQueryable<OfficeSupplyRequest> requests) =>
        from request in requests
        join supply in db.OfficeSupplies on request.OfficeSupplyId equals supply.Id
        select new OfficeSupplyRequestRecord(request.Id, supply.Id, supply.Name, supply.Unit, supply.IsReturnable,
            request.OwnerUserId.GetValueOrDefault(), request.ApplicantName, request.DepartmentId, request.Purpose,
            request.Quantity, request.ReturnedQuantity, request.ReturnDueDate, request.Status, request.CreatedAt, request.VersionNumber);

    private static Task<OfficeSupplyRequestRecord> RequestRecordAsync(AppDbContext db, User actor, int id, CancellationToken token) =>
        RequestRecords(db, db.OfficeSupplyRequests.AsNoTracking().Where(item => item.CompanyScope == actor.CompanyScope && item.Id == id)).SingleAsync(token);

    private static OfficeSupplyRecord ToRecord(OfficeSupply item) => new(item.Id, item.Name, item.Unit, item.Location,
        item.Description, item.IsReturnable, item.IsActive, item.StockQuantity, item.ReservedQuantity, item.MinimumStock, item.VersionNumber);

    private static OfficeStockMovementRecord ToRecord(OfficeStockMovement item) =>
        new(item.Id, item.Kind, item.QuantityDelta, item.StockAfter, item.ActorName, item.Note, item.CreatedAt);
}
