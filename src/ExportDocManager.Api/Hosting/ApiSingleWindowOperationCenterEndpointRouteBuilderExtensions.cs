using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowOperationCenterEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/operation-center", async Task<Results<
                Ok<SingleWindowOperationCenterPageResult>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowOperationCenterService operationCenterService,
                string? businessType,
                string? status,
                string? keyword,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                var result = await operationCenterService.QueryPageAsync(
                    new SingleWindowOperationCenterPageQuery
                    {
                        BusinessType = businessType ?? string.Empty,
                        Status = status ?? string.Empty,
                        Keyword = keyword ?? string.Empty,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50
                    },
                    cancellationToken);

                return TypedResults.Ok(result);
            })
            .WithName("ListSingleWindowOperationCenter");

            endpoints.MapGet("/api/single-window/operation-center/{batchId:int}", async Task<Results<
                Ok<SingleWindowOperationCenterDetail>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowOperationCenterService operationCenterService,
                int batchId,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (batchId <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("单一窗口批次ID必须大于0。"));
                }

                try
                {
                    var result = await operationCenterService.GetDetailAsync(batchId, cancellationToken);
                    return TypedResults.Ok(result);
                }
                catch (ResourceNotFoundException)
                {
                    return TypedResults.NotFound();
                }
            })
            .WithName("GetSingleWindowOperationCenterDetail");
        }
    }
}
