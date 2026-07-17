using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowExportReviewEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/export-review/{businessType}/{invoiceId:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowExportReviewService exportReviewService,
                ISettingsService settingsService,
                string businessType,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (!TryParseSingleWindowBusinessType(businessType, out var parsedBusinessType))
                {
                    return BadSingleWindowBusinessType();
                }

                return await BuildSingleWindowExportReviewAsync(
                    exportReviewService,
                    settingsService,
                    parsedBusinessType,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("GetSingleWindowExportReview");

            endpoints.MapPost("/api/single-window/coo/{invoiceId:int}/export-review", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowExportReviewService exportReviewService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await BuildSingleWindowExportReviewAsync(
                    exportReviewService,
                    settingsService,
                    SingleWindowBusinessType.CustomsCoo,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("BuildCustomsCooExportReview");

            endpoints.MapPost("/api/single-window/acd/{invoiceId:int}/export-review", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowExportReviewService exportReviewService,
                ISettingsService settingsService,
                int invoiceId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                return await BuildSingleWindowExportReviewAsync(
                    exportReviewService,
                    settingsService,
                    SingleWindowBusinessType.AgentConsignment,
                    invoiceId,
                    cancellationToken);
            })
            .WithName("BuildAgentConsignmentExportReview");

            endpoints.MapPost("/api/single-window/export-review/{businessType}/{invoiceId:int}/repair", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowExportReviewService exportReviewService,
                ISettingsService settingsService,
                string businessType,
                int invoiceId,
                ApiSingleWindowRepairGroupsRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (!TryParseSingleWindowBusinessType(businessType, out var parsedBusinessType))
                {
                    return BadSingleWindowBusinessType();
                }

                var groupKeys = ApiSingleWindowDtoFactory.NormalizeGroupKeys(request?.GroupKeys);
                if (groupKeys.Count == 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("自动修复分组不能为空。"));
                }

                try
                {
                    await settingsService.LoadAsync();
                    int repairedGroupCount = await exportReviewService.RepairGroupsAsync(
                        parsedBusinessType,
                        invoiceId,
                        groupKeys,
                        cancellationToken);
                    var review = await exportReviewService.BuildSubmitReviewAsync(
                        parsedBusinessType,
                        invoiceId,
                        cancellationToken);
                    string message = repairedGroupCount > 0
                        ? $"已自动修复 {repairedGroupCount} 个单一窗口分组。"
                        : "没有可自动修复的单一窗口分组。";

                    return Results.Ok(new ApiSingleWindowRepairGroupsResponse(
                        true,
                        repairedGroupCount,
                        review,
                        message));
                }
                catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(
                        new ApiErrorResponse(ex.Message),
                        statusCode: StatusCodes.Status403Forbidden);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("RepairSingleWindowExportReviewGroups");
        }
    }
}
