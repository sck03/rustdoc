using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExporterMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/exporters", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new ExporterReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromExporters(rows));
            })
            .WithName("ListExporters");

            endpoints.MapGet("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                var exporter = await exporterService.GetExporterByIdAsync(id);
                return exporter == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromExporter(exporter));
            })
            .WithName("GetExporter");

            endpoints.MapPost("/api/master-data/exporters/{id:int}/seals/{sealType}/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterSealService sealService,
                int id,
                string sealType,
                string fileName,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (!TryParseExporterSealKind(sealType, out var sealKind))
                {
                    return Results.BadRequest(new ApiErrorResponse("印章类型必须是 document 或 customs。"));
                }

                string safeFileName = NormalizeExporterSealFileName(fileName);
                if (string.IsNullOrWhiteSpace(safeFileName))
                {
                    return Results.BadRequest(new ApiErrorResponse("请选择有效的印章图片文件。"));
                }

                string extension = Path.GetExtension(safeFileName);
                if (!IsSupportedExporterSealExtension(extension))
                {
                    return Results.BadRequest(new ApiErrorResponse("印章图片仅支持 PNG、JPEG、GIF 或 WebP。"));
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
                        return Results.BadRequest(new ApiErrorResponse("上传的印章图片为空。"));
                    }

                    var exporter = await sealService.SaveSealAsync(
                        id,
                        sealKind,
                        safeFileName,
                        content.ToArray(),
                        cancellationToken);
                    return Results.Ok(ApiMasterDataDtoFactory.FromExporter(exporter));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("UploadExporterSeal");

            endpoints.MapPost("/api/master-data/exporters", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                ApiExporterDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增出口商不能包含已有ID。"));
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

                try
                {
                    int savedId = await exporterService.SaveExporterAsync(exporter);
                    var saved = await exporterService.GetExporterByIdAsync(savedId) ?? exporter;
                    return Results.Created(
                        $"/api/master-data/exporters/{savedId}",
                        ApiMasterDataDtoFactory.FromExporter(saved));
                }
                catch (Exception ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("CreateExporter");

            endpoints.MapPut("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id,
                ApiExporterDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体出口商ID与路径ID不一致。"));
                }

                if (await exporterService.GetExporterByIdAsync(id) == null)
                {
                    return Results.NotFound();
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

                try
                {
                    int savedId = await exporterService.SaveExporterAsync(exporter);
                    var saved = await exporterService.GetExporterByIdAsync(savedId) ?? exporter;
                    return Results.Ok(ApiMasterDataDtoFactory.FromExporter(saved));
                }
                catch (Exception ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("UpdateExporter");

            endpoints.MapDelete("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (await exporterService.GetExporterByIdAsync(id) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await exporterService.DeleteExporterAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "出口商已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("DeleteExporter");
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
                safeFileName.Length > 240 ||
                safeFileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
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
