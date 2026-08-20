using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExporterMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/exporters", async Task<Results<
                Ok<IReadOnlyList<ApiExporterDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterReadRepository repository,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var rows = await repository.QueryAsync(
                    new ExporterReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return TypedResults.Ok(ApiMasterDataDtoFactory.FromExporters(rows));
            })
            .WithName("ListExporters");

            endpoints.MapGet("/api/master-data/exporters/page", async Task<Results<
                Ok<ApiPagedResponse<ApiExporterDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterReadRepository repository,
                int pageNumber,
                int pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var page = await repository.QueryPageAsync(new ExporterReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromExporters));
            })
            .WithName("ListExportersPage");

            endpoints.MapGet("/api/master-data/exporters/{id:int}", async Task<Results<
                Ok<ApiExporterDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                var exporter = await exporterService.GetExporterByIdAsync(id, cancellationToken);
                return exporter == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromExporter(exporter));
            })
            .WithName("GetExporter");

            endpoints.MapPost("/api/master-data/exporters/{id:int}/seals/{sealType}/upload", async Task<Results<
                Ok<ApiExporterDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound<ApiErrorResponse>,
                JsonHttpResult<ApiErrorResponse>,
                StatusCodeHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterSealService sealService,
                int id,
                string sealType,
                string? fileName,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (!TryParseExporterSealKind(sealType, out var sealKind))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("印章类型必须是 document 或 customs。"));
                }

                string safeFileName = NormalizeExporterSealFileName(fileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(safeFileName))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请选择有效的印章图片文件。"));
                }

                string extension = Path.GetExtension(safeFileName);
                if (!IsSupportedExporterSealExtension(extension))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("印章图片仅支持 PNG、JPEG、GIF 或 WebP。"));
                }

                try
                {
                    await using var content = new MemoryStream();
                    await ApiUploadLimits.CopyRequestBodyAsync(
                        context.Request,
                        content,
                        ApiUploadLimits.ExporterSealImageBytes,
                        cancellationToken);
                    if (content.Length == 0)
                    {
                        return TypedResults.BadRequest(new ApiErrorResponse("上传的印章图片为空。"));
                    }

                    var exporter = await sealService.SaveSealAsync(
                        id,
                        sealKind,
                        safeFileName,
                        content.ToArray(),
                        cancellationToken);
                    return TypedResults.Ok(ApiMasterDataDtoFactory.FromExporter(exporter));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return TypedResults.Json(
                        new ApiErrorResponse(ex.Message),
                        statusCode: StatusCodes.Status413PayloadTooLarge);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (KeyNotFoundException ex)
                {
                    return TypedResults.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(ex.Message));
                }
            })
            .Accepts<IFormFile>(
                "application/octet-stream",
                "image/png",
                "image/jpeg",
                "image/gif",
                "image/webp")
            .WithName("UploadExporterSeal")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge);

            endpoints.MapPost("/api/master-data/exporters", async Task<Results<
                Created<ApiExporterDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                ApiExporterDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增出口商不能包含已有ID。"));
                }

                Exporter exporter;
                try
                {
                    exporter = ApiMasterDataDtoFactory.ToExporterForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("出口商");
                }

                exporter.Id = 0;
                exporter.RowVersion = null;

                int savedId = await exporterService.SaveExporterAsync(exporter, cancellationToken);
                var saved = await exporterService.GetExporterByIdAsync(savedId, cancellationToken) ?? exporter;
                return TypedResults.Created(
                    $"/api/master-data/exporters/{savedId}",
                    ApiMasterDataDtoFactory.FromExporter(saved));
            })
            .WithName("CreateExporter")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/exporters/{id:int}", async Task<Results<
                Ok<ApiExporterDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id,
                ApiExporterDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体出口商ID与路径ID不一致。"));
                }

                if (await exporterService.GetExporterByIdAsync(id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                Exporter exporter;
                try
                {
                    exporter = ApiMasterDataDtoFactory.ToExporterForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("出口商");
                }

                exporter.Id = id;

                int savedId = await exporterService.SaveExporterAsync(exporter, cancellationToken);
                var saved = await exporterService.GetExporterByIdAsync(savedId, cancellationToken) ?? exporter;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromExporter(saved));
            })
            .WithName("UpdateExporter")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/exporters/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (await exporterService.GetExporterByIdAsync(id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                await exporterService.DeleteExporterAsync(id, cancellationToken);
                return TypedResults.Ok(new ApiCommandResponse(true, "出口商已删除。"));
            })
            .WithName("DeleteExporter")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }

        private static bool TryParseExporterSealKind(string value, out ExporterSealKind sealKind)
        {
            if (string.Equals(value, "document", StringComparison.OrdinalIgnoreCase))
            {
                sealKind = ExporterSealKind.Document;
                return true;
            }

            if (string.Equals(value, "customs", StringComparison.OrdinalIgnoreCase))
            {
                sealKind = ExporterSealKind.Customs;
                return true;
            }

            sealKind = default;
            return false;
        }

        private static string NormalizeExporterSealFileName(string fileName)
        {
            string portableName = (fileName ?? string.Empty).Trim().Replace('\\', '/');
            string safeFileName = Path.GetFileName(portableName).Trim();
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                !CrossPlatformFileNamePolicy.IsSafeFileName(safeFileName))
            {
                return string.Empty;
            }

            return safeFileName;
        }

        private static bool IsSupportedExporterSealExtension(string extension)
        {
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }
    }
}
