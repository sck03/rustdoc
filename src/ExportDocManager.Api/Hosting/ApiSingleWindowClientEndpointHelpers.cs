using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
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
                return Results.BadRequest(new ApiErrorResponse("单一窗口客户端目录请求体不能为空。"));
            }

            string importRootPath = request.ImportRootPath?.Trim() ?? string.Empty;
            string receiptRootPath = request.ReceiptRootPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(importRootPath) && string.IsNullOrWhiteSpace(receiptRootPath))
            {
                return Results.BadRequest(new ApiErrorResponse("导入目录或回执目录必须至少提供一个。"));
            }

            if (string.IsNullOrWhiteSpace(importRootPath))
            {
                importRootPath = receiptRootPath;
            }

            if (string.IsNullOrWhiteSpace(receiptRootPath))
            {
                receiptRootPath = importRootPath;
            }

            SingleWindowBusinessType? businessType = null;
            if (!string.IsNullOrWhiteSpace(request.BusinessType))
            {
                if (!TryParseSingleWindowBusinessType(request.BusinessType, out var parsedBusinessType))
                {
                    return BadSingleWindowBusinessType();
                }

                businessType = parsedBusinessType;
            }

            try
            {
                int id = await profileService.SaveDefaultAsync(
                    importRootPath,
                    receiptRootPath,
                    businessType,
                    cancellationToken);
                var profile = await profileService.GetDefaultAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromSavedClientProfile(
                    id,
                    profile,
                    "单一窗口客户端目录档案已保存。"));
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> DispatchSingleWindowBatchToClientAsync(
            ISingleWindowClientBridge clientBridge,
            ISingleWindowClientProfileService profileService,
            ISingleWindowOperationCenterService operationCenterService,
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
                string importRootPath = await ResolveClientImportRootPathAsync(
                    profileService,
                    operationCenterService,
                    request.BatchId,
                    request.ImportRootPath,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(importRootPath))
                {
                    return Results.BadRequest(new ApiErrorResponse("导入目录不能为空，请先传入 importRootPath 或保存当前业务目录档案。"));
                }

                var result = await clientBridge.DispatchBatchToImportRootAsync(
                    request.BatchId,
                    importRootPath,
                    request.ProfileName ?? string.Empty,
                    cancellationToken);
                return Results.Ok(result);
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
        }

        private static async Task<IResult> CollectSingleWindowReceiptFilesAsync(
            ISingleWindowClientBridge clientBridge,
            ISingleWindowClientProfileService profileService,
            ISingleWindowOperationCenterService operationCenterService,
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
                string receiptRootPath = await ResolveClientReceiptRootPathAsync(
                    profileService,
                    operationCenterService,
                    request.BatchId,
                    request.ReceiptRootPath,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(receiptRootPath))
                {
                    return Results.BadRequest(new ApiErrorResponse("回执目录不能为空，请先传入 receiptRootPath 或保存当前业务目录档案。"));
                }

                var result = await clientBridge.CollectReceiptFilesAsync(
                    request.BatchId,
                    receiptRootPath,
                    cancellationToken);
                return Results.Ok(result);
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
        }

        private static async Task<string> ResolveClientImportRootPathAsync(
            ISingleWindowClientProfileService profileService,
            ISingleWindowOperationCenterService operationCenterService,
            int batchId,
            string requestPath,
            CancellationToken cancellationToken)
        {
            return await ResolveClientRootPathAsync(
                profileService,
                operationCenterService,
                batchId,
                requestPath,
                isImportRoot: true,
                cancellationToken);
        }

        private static async Task<string> ResolveClientReceiptRootPathAsync(
            ISingleWindowClientProfileService profileService,
            ISingleWindowOperationCenterService operationCenterService,
            int batchId,
            string requestPath,
            CancellationToken cancellationToken)
        {
            return await ResolveClientRootPathAsync(
                profileService,
                operationCenterService,
                batchId,
                requestPath,
                isImportRoot: false,
                cancellationToken);
        }

        private static async Task<string> ResolveClientRootPathAsync(
            ISingleWindowClientProfileService profileService,
            ISingleWindowOperationCenterService operationCenterService,
            int batchId,
            string requestPath,
            bool isImportRoot,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(requestPath))
            {
                return requestPath.Trim();
            }

            var profile = await profileService.GetDefaultAsync(cancellationToken);
            if (profile == null || profile.Id <= 0)
            {
                return string.Empty;
            }

            SingleWindowBusinessType? businessType = null;
            try
            {
                var detail = await operationCenterService.GetDetailAsync(batchId, cancellationToken);
                if (TryParseSingleWindowBusinessType(detail.BusinessType, out var parsedBusinessType))
                {
                    businessType = parsedBusinessType;
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }

            string businessRoot = isImportRoot
                ? SingleWindowClientProfilePathResolver.ResolveConfiguredImportRoot(profile, businessType).Path
                : SingleWindowClientProfilePathResolver.ResolveConfiguredReceiptRoot(profile, businessType).Path;
            if (!string.IsNullOrWhiteSpace(businessRoot))
            {
                return businessRoot;
            }

            string globalRoot = isImportRoot
                ? SingleWindowClientProfilePathResolver.ResolveConfiguredImportRoot(profile, null).Path
                : SingleWindowClientProfilePathResolver.ResolveConfiguredReceiptRoot(profile, null).Path;
            return globalRoot ?? string.Empty;
        }
    }
}
