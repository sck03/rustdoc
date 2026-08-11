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
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string? query, int? maxResults, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SearchHsCodeKnowledge");

            endpoints.MapGet("/api/invoices/hs-knowledge/search", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string? query, int? maxResults, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SearchInvoiceHsCodeKnowledge");

            endpoints.MapGet("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                int page = Math.Max(pageNumber ?? 1, 1);
                int size = Math.Clamp(pageSize ?? 50, 1, 200);
                var items = await service.ListExamplesAsync(keyword, page, size, cancellationToken);
                int total = await service.CountExamplesAsync(keyword, cancellationToken);
                return Results.Ok(new { items, totalCount = total, pageNumber = page, pageSize = size });
            }).WithName("ListHsCodeKnowledgeExamples");

            endpoints.MapPost("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeExampleInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return Results.Ok(await service.SaveExampleAsync(request, cancellationToken)); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SaveHsCodeKnowledgeExample");

            endpoints.MapDelete("/api/master-data/hs-knowledge/examples/{id:int}", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                int id, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return await service.DeleteExampleAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
            }).WithName("DeleteHsCodeKnowledgeExample");

            endpoints.MapPost("/api/master-data/hs-knowledge/examples/delete-batch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IHsCodeKnowledgeService service,
                HsCodeKnowledgeExampleDeleteBatchInput request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentHsKnowledge, PermissionAccessLevel.Manage))
                    return WriteForbidden("只有管理权限可以批量删除申报实例。");
                int deleted = await service.DeleteExamplesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已删除 {deleted} 条申报实例。"));
            }).WithName("DeleteHsCodeKnowledgeExamplesBatch");

            endpoints.MapPost("/api/master-data/hs-knowledge/feedback", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次选择，本地推荐会逐步优化。")); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordHsCodeKnowledgeFeedback");

            endpoints.MapPost("/api/invoices/hs-knowledge/feedback", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次发票归类选择。")); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordInvoiceHsCodeKnowledgeFeedback");

            endpoints.MapGet("/api/master-data/hs-knowledge/history-candidates", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try
                {
                    return Results.Ok(await service.DiscoverHistoryCandidatesAsync(
                        keyword, pageNumber ?? 1, pageSize ?? 30, cancellationToken));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("DiscoverHsCodeHistoryCandidates");

            endpoints.MapGet("/api/master-data/hs-knowledge/remote-candidates", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string? status, string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return Results.Ok(await service.ListRemoteCandidatesAsync(
                    status, keyword, pageNumber ?? 1, pageSize ?? 30, cancellationToken));
            }).WithName("ListHsCodeRemoteCandidates");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateReviewInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return await service.ReviewRemoteCandidateAsync(request, cancellationToken) ? Results.Ok(new ApiCommandResponse(true, request.Confirmed ? "已确认并加入正式申报实例库。" : "已忽略该联网候选。")) : Results.NotFound(); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidate");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review-batch", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateBatchReviewInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try
                {
                    int reviewed = await service.ReviewRemoteCandidatesAsync(request?.Items ?? [], cancellationToken);
                    return Results.Ok(new ApiCommandResponse(true, $"已处理 {reviewed} 条联网候选。"));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidatesBatch");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/reset", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateResetInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                int reset = await service.ResetRemoteCandidatesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已将 {reset} 条审核记录恢复为待审核。"));
            }).WithName("ResetHsCodeRemoteCandidates");

            MapHsCodeKnowledgePackageEndpoints(endpoints);
        }
    }
}
