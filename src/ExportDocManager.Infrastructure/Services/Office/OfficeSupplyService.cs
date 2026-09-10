using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class OfficeSupplyService(OfficeServiceContext office) : IOfficeSupplyService
{
    private const string Resource = PermissionResourceCatalog.OfficeSupplies;

    public Task<OfficeSupplyRecord> SaveSupplyAsync(int id, OfficeSupplySaveRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Manage, true, async (db, actor, token) =>
        {
            Range(id, 0, int.MaxValue, "物品编号");
            Range(request.MinimumStock, 0, 1000000, "最低库存");
            string unit = Text(request.Unit, "计量单位", 20, true);
            var supply = id == 0 ? new OfficeSupply { CompanyScope = actor.CompanyScope! } : await LockSupplyAsync(db, id, actor, token);
            if (id == 0)
            {
                if (request.ExpectedVersion != 0) throw new ServiceValidationException("新增物品不能包含已有版本。");
                db.OfficeSupplies.Add(supply);
            }
            else
            {
                Version(request.ExpectedVersion, supply.VersionNumber);
                var requests = db.OfficeSupplyRequests.Where(item => item.OfficeSupplyId == id);
                if ((supply.IsReturnable != request.IsReturnable || supply.Unit != unit) && await requests.AnyAsync(token))
                    throw new ResourceConflictException("已有申请的物品不能更改计量单位或归还类型，请新建物品档案。");
                if (!request.IsActive && await requests.AnyAsync(item => item.Status == SupplyRequestStatus.Pending ||
                    item.Status == SupplyRequestStatus.Approved || supply.IsReturnable && item.Status == SupplyRequestStatus.Issued, token))
                    throw new ResourceConflictException("物品仍有待处理申请或借出记录，请处理后再停用。");
            }
            supply.Name = Text(request.Name, "物品名称", 120, true);
            supply.Unit = unit;
            supply.Location = Text(request.Location, "存放位置", 200);
            supply.Description = Text(request.Description, "物品说明", 500);
            supply.IsReturnable = request.IsReturnable;
            supply.IsActive = request.IsActive;
            supply.MinimumStock = request.MinimumStock;
            await db.SaveChangesAsync(token);
            return ToRecord(supply);
        }, cancellationToken);

    public Task<OfficeSupplyRequestRecord> CreateRequestAsync(SupplyRequestCreateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Create, true, async (db, actor, token) =>
        {
            var applicant = await office.ApplicantAsync(db, actor, request.EmployeeId, token);
            RequestKey(request.RequestKey);
            Range(request.Quantity, 1, 1000000, "领用数量");
            string purpose = Text(request.Purpose, "用途", 500, true);
            var supply = await LockSupplyAsync(db, request.OfficeSupplyId, actor, token);
            var previous = await db.OfficeSupplyRequests.SingleOrDefaultAsync(item => item.CompanyScope == actor.CompanyScope &&
                item.OwnerUserId == actor.Id && item.RequestKey == request.RequestKey, token);
            if (previous != null)
            {
                if (previous.OfficeSupplyId != supply.Id || previous.Quantity != request.Quantity || previous.Purpose != purpose ||
                    previous.ReturnDueDate != request.ReturnDueDate || previous.EmployeeId != request.EmployeeId)
                    throw new ResourceConflictException("同一请求标识不能用于不同的领用内容。");
                return await RequestRecordAsync(db, actor, previous.Id, token);
            }
            ValidateSupplyRequest(supply, request.Quantity, request.ReturnDueDate);
            if (office.IsLocalRegister) ReserveStock(supply, request.Quantity);
            var entity = new OfficeSupplyRequest
            {
                OfficeSupplyId = supply.Id,
                EmployeeId = request.EmployeeId,
                RequestKey = request.RequestKey,
                OwnerUserId = actor.Id,
                CompanyScope = actor.CompanyScope!,
                DepartmentId = applicant.Department,
                ApplicantName = applicant.Name,
                Status = office.IsLocalRegister ? SupplyRequestStatus.Approved : SupplyRequestStatus.Pending,
                Purpose = purpose,
                Quantity = request.Quantity,
                ReturnDueDate = request.ReturnDueDate,
                CreatedAt = office.Clock.UtcNow,
                UpdatedAt = office.Clock.UtcNow
            };
            db.OfficeSupplyRequests.Add(entity);
            await db.SaveChangesAsync(token);
            office.AddEvent(db, actor, office.IsLocalRegister ? "Register" : "Submit", "", requestId: entity.Id);
            await db.SaveChangesAsync(token);
            return await RequestRecordAsync(db, actor, entity.Id, token);
        }, cancellationToken);

    public Task<OfficeSupplyRequestRecord> TransitionAsync(int id, OfficeWorkflowAction action, OfficeReturnRequest request,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, ActionPermission(action), true, async (db, actor, token) =>
        {
            var snapshot = await db.OfficeSupplyRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("领用申请不存在。");
            office.DemandRecord(snapshot, actor, Resource, ActionPermission(action));
            var supply = await LockSupplyAsync(db, snapshot.OfficeSupplyId, actor, token);
            var entity = await db.OfficeSupplyRequests.SingleAsync(item => item.Id == id, token);
            Version(request.ExpectedVersion, entity.VersionNumber);
            string note = Text(request.Note, "处理说明", 500, action is OfficeWorkflowAction.Reject or OfficeWorkflowAction.Cancel);
            if (action != OfficeWorkflowAction.Return && request.Quantity != 0)
                throw new ServiceValidationException("只有归还登记可以指定本次数量。");
            switch (action)
            {
                case OfficeWorkflowAction.Approve:
                case OfficeWorkflowAction.Reject:
                    DemandOtherApplicant(entity, actor);
                    RequireStatus(entity, SupplyRequestStatus.Pending);
                    if (action == OfficeWorkflowAction.Approve)
                    {
                        DemandCurrentRequest(supply, entity);
                        ReserveStock(supply, entity.Quantity);
                    }
                    entity.Status = action == OfficeWorkflowAction.Approve ? SupplyRequestStatus.Approved : SupplyRequestStatus.Rejected;
                    break;
                case OfficeWorkflowAction.Cancel:
                    if (entity.Status is not (SupplyRequestStatus.Pending or SupplyRequestStatus.Approved))
                        throw new ResourceConflictException("仅待审批或待领用申请可以取消。");
                    if (entity.Status == SupplyRequestStatus.Approved) supply.ReservedQuantity -= entity.Quantity;
                    entity.Status = SupplyRequestStatus.Cancelled;
                    break;
                case OfficeWorkflowAction.Issue:
                    RequireStatus(entity, SupplyRequestStatus.Approved);
                    DemandCurrentRequest(supply, entity);
                    supply.StockQuantity -= entity.Quantity;
                    supply.ReservedQuantity -= entity.Quantity;
                    entity.Status = SupplyRequestStatus.Issued;
                    AddMovement(db, supply, actor, "Issue", -entity.Quantity, note, Guid.NewGuid(), id);
                    break;
                case OfficeWorkflowAction.Return:
                    RequireStatus(entity, SupplyRequestStatus.Issued);
                    if (!supply.IsReturnable) throw new ResourceConflictException("消耗品不能登记借用归还。");
                    Range(request.Quantity, 1, entity.Quantity - entity.ReturnedQuantity, "本次归还数量");
                    Range(supply.StockQuantity + request.Quantity, 0, 1000000, "归还后库存");
                    entity.ReturnedQuantity += request.Quantity;
                    supply.StockQuantity += request.Quantity;
                    if (entity.ReturnedQuantity == entity.Quantity) entity.Status = SupplyRequestStatus.Returned;
                    AddMovement(db, supply, actor, "Return", request.Quantity, note, Guid.NewGuid(), id);
                    break;
                default: throw new ServiceValidationException("未知领用操作。");
            }
            office.AddEvent(db, actor, action.ToString(), note, requestId: id,
                quantity: action == OfficeWorkflowAction.Return ? request.Quantity : action == OfficeWorkflowAction.Issue ? entity.Quantity : 0);
            await db.SaveChangesAsync(token);
            return await RequestRecordAsync(db, actor, id, token);
        }, cancellationToken);

    public Task<OfficeStockMovementRecord> ChangeStockAsync(int id, OfficeStockRequest request, bool stocktake,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, stocktake ? PermissionAction.Manage : PermissionAction.Restock, true, async (db, actor, token) =>
        {
            RequestKey(request.OperationId);
            Range(request.Quantity, stocktake ? 0 : 1, 1000000, stocktake ? "盘点数量" : "补充数量");
            string note = Text(request.Note, "库存变动说明", 500, true);
            string kind = stocktake ? "Stocktake" : "Restock";
            var supply = await LockSupplyAsync(db, id, actor, token);
            var previous = await db.OfficeStockMovements.SingleOrDefaultAsync(item => item.CompanyScope == actor.CompanyScope && item.OperationId == request.OperationId, token);
            if (previous != null)
            {
                if (previous.OfficeSupplyId != id || previous.ActorUserId != actor.Id || previous.Kind != kind || previous.Note != note ||
                    (stocktake ? previous.StockAfter : previous.QuantityDelta) != request.Quantity)
                    throw new ResourceConflictException("同一库存操作标识不能用于不同内容。");
                return ToRecord(previous);
            }
            Version(request.ExpectedVersion, supply.VersionNumber);
            int after = stocktake ? request.Quantity : supply.StockQuantity + request.Quantity;
            Range(after, 0, 1000000, "变动后库存");
            if (after < supply.ReservedQuantity) throw new ResourceConflictException("盘点库存不能少于已审批预留量，请先取消相关申请。");
            int delta = after - supply.StockQuantity;
            if (delta == 0) throw new ServiceValidationException("盘点数量与当前库存一致，无需调整。");
            supply.StockQuantity = after;
            var movement = AddMovement(db, supply, actor, kind, delta, note, request.OperationId);
            await db.SaveChangesAsync(token);
            return ToRecord(movement);
        }, cancellationToken);

    private OfficeStockMovement AddMovement(AppDbContext db, OfficeSupply supply, User actor,
        string kind, int delta, string note, Guid operationId, int? requestId = null)
    {
        var movement = new OfficeStockMovement
        {
            CompanyScope = supply.CompanyScope,
            OfficeSupplyId = supply.Id,
            OfficeSupplyRequestId = requestId,
            OperationId = operationId,
            Kind = kind,
            QuantityDelta = delta,
            StockAfter = supply.StockQuantity,
            ActorUserId = actor.Id,
            ActorName = ActorName(actor),
            Note = note,
            CreatedAt = office.Clock.UtcNow
        };
        db.OfficeStockMovements.Add(movement);
        return movement;
    }

    private static void ReserveStock(OfficeSupply supply, int quantity)
    {
        if (supply.StockQuantity - supply.ReservedQuantity < quantity)
            throw new ResourceConflictException("可用库存不足，请补充库存后再登记或审批。");
        supply.ReservedQuantity += quantity;
    }

    private void DemandCurrentRequest(OfficeSupply supply, OfficeSupplyRequest request)
    {
        if (!supply.IsActive || request.ReturnDueDate < office.Clock.Today)
            throw new ResourceConflictException("物品已停用或预计归还日期已过，请关闭申请后重新提交。");
    }

    private static void RequireStatus(OfficeSupplyRequest request, SupplyRequestStatus required)
    {
        if (request.Status != required) throw new ResourceConflictException("当前领用状态不允许此操作，请刷新后核对。");
    }
}
