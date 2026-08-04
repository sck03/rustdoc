using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExcelImportPreviewEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/excel/import-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISettingsService settingsService,
                IExcelImportService excelImportService,
                ApiExcelImportPreviewRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("预览本机 Excel 仅支持桌面版；浏览器版请上传文件。");
                }

                var validation = ValidateExcelSourcePath(
                    request?.FilePath,
                    "Excel 导入源文件",
                    out string sourcePath);
                if (validation != null)
                {
                    return validation;
                }

                try
                {
                    await settingsService.LoadAsync();
                    var result = await excelImportService.ImportFromExcelAsync(sourcePath, context.RequestAborted);
                    return Results.Ok(ApiExcelDtoFactory.FromImportResult(sourcePath, result));
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewExcelImport");

            endpoints.MapPost("/api/tools/excel/import-preview-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ISettingsService settingsService,
                IExcelImportService excelImportService,
                string fileName,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string portableFileName = (fileName ?? string.Empty).Trim().Replace('\\', '/');
                string safeFileName = Path.GetFileName(portableFileName).Trim();
                if (string.IsNullOrWhiteSpace(safeFileName) ||
                    safeFileName.Length > 240 ||
                    safeFileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
                {
                    return Results.BadRequest(new ApiErrorResponse("请上传文件名有效且不超过 240 个字符的 Excel 文件。"));
                }

                if (!IsSupportedExcelSourceExtension(safeFileName))
                {
                    return Results.BadRequest(new ApiErrorResponse("请上传 .xlsx、.xlsm、.xltx、.xltm 或 .xls 文件。"));
                }

                string uploadRoot = Path.Combine(
                    pathProvider.CacheRoot,
                    "BrowserUploads",
                    "ExcelImport",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uploadRoot);
                string sourcePath = Path.Combine(uploadRoot, safeFileName);

                try
                {
                    await using (var output = File.Create(sourcePath))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.ExcelImportBytes,
                            cancellationToken);
                    }

                    if (new FileInfo(sourcePath).Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("上传的 Excel 文件为空。"));
                    }

                    await settingsService.LoadAsync();
                    var result = await excelImportService.ImportFromExcelAsync(sourcePath, cancellationToken);
                    return Results.Ok(ApiExcelDtoFactory.FromImportResult(
                        safeFileName,
                        result,
                        "浏览器上传的 Excel 仅暂存在运行数据根 Cache/BrowserUploads/ExcelImport，请求结束后立即删除；解析结果不会写入数据库，响应只返回发票草稿和安全原文件名，不返回或保存服务器临时绝对路径。"));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(uploadRoot);
                }
            })
            .WithName("PreviewUploadedExcelImport");
        }
    }
}
