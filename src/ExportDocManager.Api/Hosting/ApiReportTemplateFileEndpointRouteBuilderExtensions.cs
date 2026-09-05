using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapReportTemplateFileEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/templates/file/save-to-path", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplateFileService fileService,
                ApiReportTemplateFileExportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请下载模板文件。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("模板文件导出请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await fileService.ExportAsync(
                        parsedReportType,
                        request.TemplatePath,
                        request.FilePath,
                        cancellationToken);
                    return Results.Ok(new ApiReportTemplateFileExportResponse(
                        result.FilePath,
                        result.Bytes,
                        result.StoragePolicy));
                }
                catch (FileNotFoundException ex) { return Results.NotFound(new ApiErrorResponse(ex.Message)); }
                catch (UnauthorizedAccessException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (IOException ex) { return WriteServiceException(ex); }
                catch (InvalidOperationException ex) { return WriteServiceException(ex); }
            })
            .WithName("SaveReportTemplateFileToPath")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Export)
            .Produces<ApiReportTemplateFileExportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/file/import", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplateFileService fileService,
                ApiReportTemplateFileImportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机文件导入仅支持桌面版；浏览器版请上传模板文件。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("模板文件导入请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await fileService.ImportAsync(
                        parsedReportType,
                        request.TemplatePath,
                        request.FilePath,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(context, result));
                }
                catch (FileNotFoundException ex) { return Results.NotFound(new ApiErrorResponse(ex.Message)); }
                catch (UnauthorizedAccessException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (IOException ex) { return WriteServiceException(ex); }
                catch (InvalidOperationException ex) { return WriteServiceException(ex); }
            })
            .WithName("ImportReportTemplateFile")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Import)
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/file/download", async (
                HttpContext context,
                IReportTemplateFileService fileService,
                IAppPathProvider pathProvider,
                ApiReportTemplateFileDownloadRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("模板文件下载请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(pathProvider, "TemplateFiles", "html-download");
                string targetPath = Path.Combine(tempRoot, "template.html");
                bool cleanupRegistered = false;
                try
                {
                    await fileService.ExportAsync(parsedReportType, request.TemplatePath, targetPath, cancellationToken);
                    var response = StreamTemporaryFile(context, targetPath, "text/html; charset=utf-8", "template.html", tempRoot);
                    cleanupRegistered = true;
                    return response;
                }
                catch (FileNotFoundException ex) { return Results.NotFound(new ApiErrorResponse(ex.Message)); }
                catch (UnauthorizedAccessException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (IOException ex) { return WriteServiceException(ex); }
                catch (InvalidOperationException ex) { return WriteServiceException(ex); }
                finally
                {
                    if (!cleanupRegistered) AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            })
            .WithName("DownloadReportTemplateFile")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Export)
            .Produces<byte[]>(StatusCodes.Status200OK, "text/html")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/file/upload", async (
                HttpContext context,
                IReportTemplateService reportTemplateService,
                string? reportType,
                string? templatePath,
                string? fileName,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    NormalizeUploadedReportTemplateFileName(fileName ?? string.Empty);
                    await using var input = new MemoryStream();
                    long bytes = await ApiUploadLimits.CopyRequestBodyAsync(
                        context.Request,
                        input,
                        10L * 1024L * 1024L,
                        cancellationToken);
                    if (bytes == 0) return Results.BadRequest(new ApiErrorResponse("模板文件不能为空。"));

                    input.Position = 0;
                    using var reader = new StreamReader(input, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string content = await reader.ReadToEndAsync(cancellationToken);
                    var result = await reportTemplateService.SaveTemplateContentAsync(
                        parsedReportType,
                        templatePath ?? string.Empty,
                        content,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(context, result));
                }
                catch (PayloadLimitExceededException ex) { return WritePayloadTooLarge(ex); }
                catch (FileNotFoundException ex) { return Results.NotFound(new ApiErrorResponse(ex.Message)); }
                catch (UnauthorizedAccessException ex) { return WriteServiceException(ex); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (IOException ex) { return WriteServiceException(ex); }
                catch (InvalidOperationException ex) { return WriteServiceException(ex); }
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("UploadReportTemplateFile")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Import)
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge);
        }
    }
}
