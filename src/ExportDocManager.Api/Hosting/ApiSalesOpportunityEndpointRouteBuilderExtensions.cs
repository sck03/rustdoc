using ExportDocManager.Services.Opportunities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSalesOpportunityEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/crm/opportunities", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, ISalesOpportunityService service, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                int.TryParse(c.Request.Query["pageNumber"], out int pageNumber);
                int.TryParse(c.Request.Query["pageSize"], out int pageSize);
                var page = await service.QueryAsync(c.Request.Query["keyword"], c.Request.Query["stage"],
                    pageNumber > 0 ? pageNumber : 1, pageSize > 0 ? pageSize : 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSalesOpportunityDto>(page.Items.Select(ToApiDto).ToArray(),
                    page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySalesOpportunities");
            endpoints.MapPost("/api/crm/opportunities", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, ISalesOpportunityService service, ApiSalesOpportunitySaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增商机不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveAsync(ToSaveRequest(request, 0), ct);
                    return Results.Created($"/api/crm/opportunities/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSalesOpportunity");
            endpoints.MapGet("/api/crm/opportunities/{id:int}/history", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, ISalesOpportunityService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                var rows = await service.ListHistoryAsync(id, ct);
                return rows.Count > 0 ? Results.Ok(rows.Select(ToApiDto)) : Results.NotFound();
            }).WithName("ListSalesOpportunityHistory");
            endpoints.MapPut("/api/crm/opportunities/{id:int}", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, ISalesOpportunityService service, int id, ApiSalesOpportunitySaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (request == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("商机ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSalesOpportunity");
            endpoints.MapDelete("/api/crm/opportunities/{id:int}", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, ISalesOpportunityService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await service.DeleteAsync(id, ct)
                    ? Results.Ok(new ApiCommandResponse(true, "商机已删除。")) : Results.NotFound();
            }).WithName("DeleteSalesOpportunity");
        }

        private static ApiSalesOpportunityDto ToApiDto(SalesOpportunityRecord item) => new(item.Id, item.CrmCustomerId,
            item.CustomerName, item.ProductId, item.ProductCode, item.ProductName, item.Title, item.Stage,
            item.QuotationNo, item.EstimatedAmount, item.Currency, item.ProbabilityPercent,
            item.ExpectedCloseAt, item.NextAction, item.Notes);
        private static SalesOpportunitySaveRequest ToSaveRequest(ApiSalesOpportunitySaveRequest item, int id) =>
            new(id, item.CrmCustomerId, item.ProductId, item.Title, item.Stage, item.QuotationNo,
                item.EstimatedAmount, item.Currency, item.ProbabilityPercent, item.ExpectedCloseAt, item.NextAction, item.Notes, item.ChangeNote);
        private static ApiSalesOpportunityHistoryDto ToApiDto(SalesOpportunityHistoryRecord item) =>
            new(item.Id, item.SalesOpportunityId, item.VersionNumber, item.ChangeType, item.Stage,
                item.QuotationNo, item.EstimatedAmount, item.Currency, item.ProbabilityPercent,
                item.ExpectedCloseAt, item.ChangeNote, item.ChangedBy, item.CreatedAt);
    }
}
