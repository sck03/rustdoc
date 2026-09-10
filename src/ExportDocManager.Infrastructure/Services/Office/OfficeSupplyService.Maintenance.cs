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
    public Task DeleteSupplyAsync(int id, DeleteRecordRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Manage, true, async (db, actor, token) =>
        {
            var supply = await LockSupplyAsync(db, id, actor, token);
            Version(request.ExpectedVersion, supply.VersionNumber);
            string reason = Text(request.Reason, "删除原因", 500, true);
            if (supply.StockQuantity != 0 || supply.ReservedQuantity != 0 ||
                await db.OfficeSupplyRequests.AnyAsync(item => item.OfficeSupplyId == id, token) ||
                await db.OfficeStockMovements.AnyAsync(item => item.OfficeSupplyId == id, token))
                throw new ResourceConflictException("物品已有库存或领用流水，不能删除；可在处理完交接后停用。");
            RecordDeletionAudit.Add(db, actor, nameof(OfficeSupply), id, reason, office.Clock.UtcNow);
            db.OfficeSupplies.Remove(supply);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    public Task<OfficeSupplyRequestRecord> UpdateRequestAsync(int id, SupplyRequestUpdateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Edit, true, async (db, actor, token) =>
        {
            var snapshot = await db.OfficeSupplyRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("领用申请不存在。");
            office.DemandRecord(snapshot, actor, Resource, PermissionAction.Edit);
            var supply = await LockSupplyAsync(db, snapshot.OfficeSupplyId, actor, token);
            var entity = await db.OfficeSupplyRequests.SingleAsync(item => item.Id == id, token);
            Version(request.ExpectedVersion, entity.VersionNumber);
            if (entity.Status is not (SupplyRequestStatus.Pending or SupplyRequestStatus.Approved))
                throw new ResourceConflictException("仅尚未发放的申请可以修改；已交接记录保留历史。");
            ValidateSupplyRequest(supply, request.Quantity, request.ReturnDueDate);
            string purpose = Text(request.Purpose, "用途", 500, true);
            if (entity.Quantity == request.Quantity && entity.Purpose == purpose && entity.ReturnDueDate == request.ReturnDueDate)
                return await RequestRecordAsync(db, actor, id, token);
            if (entity.Status == SupplyRequestStatus.Approved) supply.ReservedQuantity -= entity.Quantity;
            if (office.IsLocalRegister) ReserveStock(supply, request.Quantity);
            entity.Quantity = request.Quantity;
            entity.Purpose = purpose;
            entity.ReturnDueDate = request.ReturnDueDate;
            entity.Status = office.IsLocalRegister ? SupplyRequestStatus.Approved : SupplyRequestStatus.Pending;
            office.AddEvent(db, actor, "Edit", office.IsLocalRegister ? "修正领用登记" : "修改领用后重新审批", requestId: id);
            await db.SaveChangesAsync(token);
            return await RequestRecordAsync(db, actor, id, token);
        }, cancellationToken);

    private void ValidateSupplyRequest(OfficeSupply supply, int quantity, DateOnly? due)
    {
        Range(quantity, 1, 1000000, "领用数量");
        if (!supply.IsActive) throw new ResourceConflictException("物品已停用。");
        if (supply.IsReturnable && (!due.HasValue || due < office.Clock.Today || due > office.Clock.Today.AddDays(365)))
            throw new ServiceValidationException("借用物品须填写今天至未来一年内的预计归还日期。");
        if (!supply.IsReturnable && due.HasValue) throw new ServiceValidationException("消耗品不需要归还日期。");
    }
}
