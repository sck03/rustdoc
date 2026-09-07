using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapOfficeSupplyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        const string resource = PermissionResourceCatalog.OfficeSupplies;
        endpoints.MapGet("/api/office/supplies", async (IOfficeSupplyService service, string? keyword, bool? includeInactive,
            bool? lowStockOnly, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QuerySuppliesAsync(new OfficeResourceQuery(keyword, includeInactive ?? false,
                pageNumber ?? 1, pageSize ?? 24, lowStockOnly ?? false), cancellationToken)))
            .OfficeEndpoint("ListOfficeSupplies", resource, PermissionAction.View);
        endpoints.MapPost("/api/office/supplies", async (IOfficeSupplyService service, OfficeSupplySaveRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SaveSupplyAsync(0, request, cancellationToken)))
            .OfficeEndpoint("CreateOfficeSupply", resource, PermissionAction.Manage);
        endpoints.MapPut("/api/office/supplies/{id:int:min(1)}", async (IOfficeSupplyService service, int id, OfficeSupplySaveRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SaveSupplyAsync(id, request, cancellationToken)))
            .OfficeEndpoint("UpdateOfficeSupply", resource, PermissionAction.Manage);
        endpoints.MapPost("/api/office/supplies/{id:int}/restock", async (IOfficeSupplyService service, int id, OfficeStockRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ChangeStockAsync(id, request, false, cancellationToken)))
            .OfficeEndpoint("RestockOfficeSupply", resource, PermissionAction.Restock);
        endpoints.MapPost("/api/office/supplies/{id:int}/stocktake", async (IOfficeSupplyService service, int id, OfficeStockRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ChangeStockAsync(id, request, true, cancellationToken)))
            .OfficeEndpoint("StocktakeOfficeSupply", resource, PermissionAction.Manage);
        endpoints.MapGet("/api/office/supplies/{id:int}/movements", async (IOfficeSupplyService service, int id,
            int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.StockHistoryAsync(id, pageNumber ?? 1, pageSize ?? 20, cancellationToken)))
            .OfficeEndpoint("GetOfficeStockHistory", resource, PermissionAction.Restock);

        endpoints.MapGet("/api/office/supply-requests", async (IOfficeSupplyService service, string? status, bool? mineOnly,
            int? resourceId, DateTimeOffset? from, DateTimeOffset? to, int? pageNumber, int? pageSize, int? requestId, int? applicantUserId, int? employeeId, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryRequestsAsync(new OfficeRequestQuery(status, mineOnly ?? true, resourceId,
                from, to, pageNumber ?? 1, pageSize ?? 20, requestId, applicantUserId, employeeId), cancellationToken)))
            .OfficeEndpoint("ListOfficeSupplyRequests", resource, PermissionAction.View);
        endpoints.MapPost("/api/office/supply-requests", async (IOfficeSupplyService service, SupplyRequestCreateRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.CreateRequestAsync(request, cancellationToken)))
            .OfficeEndpoint("CreateOfficeSupplyRequest", resource, PermissionAction.Create);
        endpoints.MapGet("/api/office/supply-requests/{id:int}/history", async (IOfficeSupplyService service, int id,
            int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.HistoryAsync(id, pageNumber ?? 1, pageSize ?? 50, cancellationToken)))
            .OfficeEndpoint("GetOfficeSupplyRequestHistory", resource, PermissionAction.View);
        endpoints.MapPost("/api/office/supply-requests/{id:int}/approve", async (IOfficeSupplyService service, int id, OfficeReturnRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Approve, request, cancellationToken)))
            .OfficeEndpoint("ApproveOfficeSupplyRequest", resource, PermissionAction.Approve);
        endpoints.MapPost("/api/office/supply-requests/{id:int}/reject", async (IOfficeSupplyService service, int id, OfficeReturnRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Reject, request, cancellationToken)))
            .OfficeEndpoint("RejectOfficeSupplyRequest", resource, PermissionAction.Approve);
        endpoints.MapPost("/api/office/supply-requests/{id:int}/cancel", async (IOfficeSupplyService service, int id, OfficeReturnRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Cancel, request, cancellationToken)))
            .OfficeEndpoint("CancelOfficeSupplyRequest", resource, PermissionAction.Cancel);
        endpoints.MapPost("/api/office/supply-requests/{id:int}/issue", async (IOfficeSupplyService service, int id, OfficeReturnRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Issue, request, cancellationToken)))
            .OfficeEndpoint("IssueOfficeSupply", resource, PermissionAction.Issue);
        endpoints.MapPost("/api/office/supply-requests/{id:int}/return", async (IOfficeSupplyService service, int id, OfficeReturnRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Return, request, cancellationToken)))
            .OfficeEndpoint("ReturnOfficeSupply", resource, PermissionAction.Return);
    }
}
