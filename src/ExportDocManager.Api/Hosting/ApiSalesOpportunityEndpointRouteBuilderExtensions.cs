using ExportDocManager.Services.Errors;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSalesOpportunityEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/crm/opportunities", async Task<Results<Ok<ApiPagedResponse<ApiSalesOpportunityDto>>, UnauthorizedHttpResult, ForbidHttpResult>> (
                ISalesOpportunityService service,
                string? keyword, string? stage, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await service.QueryAsync(keyword, stage,
                    pageNumber is > 0 ? pageNumber.Value : 1,
                    pageSize is > 0 ? pageSize.Value : 20, ct);
                return TypedResults.Ok(new ApiPagedResponse<ApiSalesOpportunityDto>(page.Items.Select(ToApiDto).ToArray(),
                    page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySalesOpportunities")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.View);

            endpoints.MapPost("/api/crm/opportunities", async Task<Results<Created<ApiSalesOpportunityDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>> (
                ISalesOpportunityService service, ApiSalesOpportunitySaveRequest request, CancellationToken ct) =>
            {
                if (request is null || request.Id > 0)
                    return TypedResults.BadRequest(new ApiErrorResponse("新增商机不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveAsync(ToSaveRequest(request, 0), ct);
                    return TypedResults.Created($"/api/crm/opportunities/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceNotFoundException) { return TypedResults.NotFound(); }
            }).WithName("CreateSalesOpportunity")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.Create);

            endpoints.MapGet("/api/crm/opportunities/{id:int}/history", async Task<Results<Ok<IReadOnlyList<ApiSalesOpportunityHistoryDto>>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>> (
                ISalesOpportunityService service, int id, CancellationToken ct) =>
            {
                var rows = await service.ListHistoryAsync(id, ct);
                return rows.Count == 0
                    ? TypedResults.NotFound()
                    : TypedResults.Ok<IReadOnlyList<ApiSalesOpportunityHistoryDto>>(rows.Select(ToApiDto).ToArray());
            }).WithName("ListSalesOpportunityHistory")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.View);

            endpoints.MapPut("/api/crm/opportunities/{id:int}", async Task<Results<Ok<ApiSalesOpportunityDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>> (
                ISalesOpportunityService service,
                int id, ApiSalesOpportunitySaveRequest request, CancellationToken ct) =>
            {
                if (request is null || id <= 0)
                    return TypedResults.BadRequest(new ApiErrorResponse("商机ID无效。"));
                try { return TypedResults.Ok(ToApiDto(await service.SaveAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceNotFoundException) { return TypedResults.NotFound(); }
            }).WithName("UpdateSalesOpportunity")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.Edit);

            endpoints.MapPost("/api/crm/opportunities/{id:int}/transition", async Task<Results<Ok<ApiSalesOpportunityDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>> (
                ISalesOpportunityService service,
                int id,
                ApiSalesOpportunityTransitionRequest request,
                CancellationToken ct) =>
            {
                try
                {
                    var saved = await service.TransitionAsync(
                        id,
                        new SalesOpportunityTransitionRequest(
                            request?.NextStage ?? string.Empty,
                            request?.ChangeNote ?? string.Empty,
                            request?.ExpectedVersion ?? 0),
                        ct);
                    return TypedResults.Ok(ToApiDto(saved));
                }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceNotFoundException) { return TypedResults.NotFound(); }
            }).WithName("TransitionSalesOpportunity")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.Transition)
            .WithApiSecurityAudit("sales-opportunity-transition");

            endpoints.MapPost("/api/crm/opportunities/{id:int}/archive", async Task<Results<Ok<ApiCommandResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>> (
                ISalesOpportunityService service,
                int id,
                ApiSalesOpportunityLifecycleRequest request,
                CancellationToken ct) =>
            {
                try
                {
                    return await service.ArchiveAsync(id, ct, request?.ExpectedVersion ?? 0)
                        ? TypedResults.Ok(new ApiCommandResponse(true, "商机已归档，历史版本仍可查询。"))
                        : TypedResults.NotFound();
                }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("ArchiveSalesOpportunity")
            .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.Archive)
            .WithApiSecurityAudit("sales-opportunity-archive");
        }

        private static ApiSalesOpportunityDto ToApiDto(SalesOpportunityRecord item) => new(item.Id, item.CrmCustomerId,
            item.CustomerName, item.ProductId, item.ProductCode, item.ProductName, item.Title, item.Stage,
            item.QuotationNo, item.EstimatedAmount, item.Currency, item.ProbabilityPercent,
            item.ExpectedCloseDate, item.NextAction, item.Notes, item.VersionNumber,
            SalesOpportunityStageCatalog.GetAllowedTransitions(item.Stage));
        private static SalesOpportunitySaveRequest ToSaveRequest(ApiSalesOpportunitySaveRequest item, int id) =>
            new(id, item.CrmCustomerId, item.ProductId, item.Title, item.QuotationNo,
                item.EstimatedAmount, item.Currency, item.ProbabilityPercent, item.ExpectedCloseDate, item.NextAction,
                item.Notes, item.ChangeNote, item.ExpectedVersion);
        private static ApiSalesOpportunityHistoryDto ToApiDto(SalesOpportunityHistoryRecord item) =>
            new(item.Id, item.SalesOpportunityId, item.VersionNumber, item.ChangeType, item.Stage,
                item.QuotationNo, item.EstimatedAmount, item.Currency, item.ProbabilityPercent,
                item.ExpectedCloseDate, item.ChangeNote, item.ChangedBy, item.CreatedAt);
    }
}
