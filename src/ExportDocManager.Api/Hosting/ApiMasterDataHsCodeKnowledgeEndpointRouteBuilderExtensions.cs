using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapHsCodeKnowledgeEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/hs-knowledge/search", async (
                HttpContext context, IHsCodeKnowledgeService service,
                string? query, int? maxResults, CancellationToken cancellationToken) =>
            {
                try { return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SearchHsCodeKnowledge")
            .Produces<HsCodeKnowledgeSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/invoices/hs-knowledge/search", async (
                HttpContext context, IHsCodeKnowledgeService service,
                string? query, int? maxResults, CancellationToken cancellationToken) =>
            {
                try { return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SearchInvoiceHsCodeKnowledge")
            .WithApiPermission(PermissionModuleCatalog.DocumentInvoices)
            .Produces<HsCodeKnowledgeSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IHsCodeKnowledgeService service,
                string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                int page = Math.Max(pageNumber ?? 1, 1);
                int size = Math.Clamp(pageSize ?? 50, 1, 200);
                var items = await service.ListExamplesAsync(keyword, page, size, cancellationToken);
                int total = await service.CountExamplesAsync(keyword, cancellationToken);
                return Results.Ok(new ApiHsCodeKnowledgeExamplePage(items, total, page, size));
            }).WithName("ListHsCodeKnowledgeExamples")
            .Produces<ApiHsCodeKnowledgeExamplePage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeExampleInput request, CancellationToken cancellationToken) =>
            {
                try { return Results.Ok(await service.SaveExampleAsync(request, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SaveHsCodeKnowledgeExample")
            .Produces<ExportDocManager.Models.Entities.HsCodeDeclarationExample>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapDelete("/api/master-data/hs-knowledge/examples/{id:int}", async (
                HttpContext context, IHsCodeKnowledgeService service,
                int id, CancellationToken cancellationToken) =>
            {
                return await service.DeleteExampleAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
            }).WithName("DeleteHsCodeKnowledgeExample")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/master-data/hs-knowledge/examples/delete-batch", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IHsCodeKnowledgeService service,
                HsCodeKnowledgeExampleDeleteBatchInput request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);
                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentHsKnowledge, PermissionAccessLevel.Manage))
                    return WriteForbidden("只有管理权限可以批量删除申报实例。");
                int deleted = await service.DeleteExamplesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已删除 {deleted} 条申报实例。"));
            }).WithName("DeleteHsCodeKnowledgeExamplesBatch")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/master-data/hs-knowledge/feedback", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次选择，本地推荐会逐步优化。")); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordHsCodeKnowledgeFeedback")
            .WithApiPermission(
                PermissionModuleCatalog.DocumentHsKnowledge,
                writeAccessLevel: PermissionAccessLevel.Operate)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/invoices/hs-knowledge/feedback", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次发票归类选择。")); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordInvoiceHsCodeKnowledgeFeedback")
            .WithApiPermission(PermissionModuleCatalog.DocumentInvoices)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/master-data/hs-knowledge/history-candidates", async (
                HttpContext context, IHsCodeKnowledgeService service,
                string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DiscoverHistoryCandidatesAsync(
                        keyword, pageNumber ?? 1, pageSize ?? 30, cancellationToken));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("DiscoverHsCodeHistoryCandidates")
            .Produces<HsCodeHistoryCandidatePage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/master-data/hs-knowledge/remote-candidates", async (
                HttpContext context, IHsCodeKnowledgeService service,
                string? status, string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                return Results.Ok(await service.ListRemoteCandidatesAsync(
                    status, keyword, pageNumber ?? 1, pageSize ?? 30, cancellationToken));
            }).WithName("ListHsCodeRemoteCandidates")
            .Produces<HsCodeRemoteCandidatePage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateReviewInput request, CancellationToken cancellationToken) =>
            {
                try { return await service.ReviewRemoteCandidateAsync(request, cancellationToken) ? Results.Ok(new ApiCommandResponse(true, request.Confirmed ? "已确认并加入正式申报实例库。" : "已忽略该联网候选。")) : Results.NotFound(); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidate")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review-batch", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateBatchReviewInput request, CancellationToken cancellationToken) =>
            {
                try
                {
                    int reviewed = await service.ReviewRemoteCandidatesAsync(request?.Items ?? [], cancellationToken);
                    return Results.Ok(new ApiCommandResponse(true, $"已处理 {reviewed} 条联网候选。"));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidatesBatch")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/reset", async (
                HttpContext context, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateResetInput request, CancellationToken cancellationToken) =>
            {
                int reset = await service.ResetRemoteCandidatesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已将 {reset} 条审核记录恢复为待审核。"));
            }).WithName("ResetHsCodeRemoteCandidates")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            MapHsCodeKnowledgePackageEndpoints(endpoints);
        }
    }
}
