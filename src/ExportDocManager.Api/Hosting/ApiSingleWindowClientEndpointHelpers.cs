using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static async Task<IResult> SaveSingleWindowClientProfileAsync(
            ISingleWindowClientProfileService profileService,
            ApiSingleWindowClientProfileSaveRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("持卡机操作档案请求体不能为空。"));
            }

            try
            {
                await profileService.SaveAsync(
                    new SingleWindowClientProfileUpdate
                    {
                        ProfileKey = request.ProfileKey ?? string.Empty,
                        ProfileName = request.ProfileName ?? string.Empty,
                        CompanyScope = request.CompanyScope ?? string.Empty,
                        CardIdentifier = request.CardIdentifier ?? string.Empty,
                        CustomsCooClientRootPath = request.CustomsCooClientRootPath ?? string.Empty,
                        AgentConsignmentClientRootPath = request.AgentConsignmentClientRootPath ?? string.Empty,
                        CanSubmitCustomsCoo = request.CanSubmitCustomsCoo,
                        CanSubmitAgentConsignment = request.CanSubmitAgentConsignment
                    },
                    cancellationToken);
                var profiles = await profileService.ListAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromClientProfiles(
                    profiles,
                    "操作档案已保存并设为当前档案。"));
            }
            catch (Exception ex)
            {
                return WriteServiceException(ex);
            }
        }

        private static async Task<IResult> ActivateSingleWindowClientProfileAsync(
            ISingleWindowClientProfileService profileService,
            string profileKey,
            CancellationToken cancellationToken)
        {
            try
            {
                await profileService.ActivateAsync(profileKey, cancellationToken);
                var profiles = await profileService.ListAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromClientProfiles(
                    profiles,
                    "已切换当前公司抬头与操作卡。"));
            }
            catch (Exception ex)
            {
                return WriteServiceException(ex);
            }
        }

        private static async Task<IResult> DispatchSingleWindowBatchToClientAsync(
            ISingleWindowClientBridge clientBridge,
            ApiSingleWindowClientDispatchRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("发送到导入目录请求体不能为空。"));
            }

            if (request.BatchId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("单一窗口批次ID必须大于0。"));
            }

            try
            {
                var result = await clientBridge.DispatchBatchToImportRootAsync(
                    request.BatchId,
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (ResourceNotFoundException ex)
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
                return WriteServiceException(ex);
            }
        }

        private static async Task<IResult> CollectSingleWindowReceiptFilesAsync(
            ISingleWindowClientBridge clientBridge,
            ApiSingleWindowReceiptCollectionRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("回执文件收集请求体不能为空。"));
            }

            if (request.BatchId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("单一窗口批次ID必须大于0。"));
            }

            try
            {
                var result = await clientBridge.CollectReceiptFilesAsync(
                    request.BatchId,
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (ResourceNotFoundException ex)
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
                return WriteServiceException(ex);
            }
        }
    }
}
