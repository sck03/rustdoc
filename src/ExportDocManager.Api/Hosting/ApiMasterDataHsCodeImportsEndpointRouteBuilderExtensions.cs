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
        private static void MapHsCodeImportsEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/master-data/hs-codes/import-preview-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IHsCodeService hsCodeService,
                IAppPathProvider pathProvider,
                ApiHsCodeImportPreviewPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                    return WriteForbidden("该本机 Excel 预览仅支持桌面版；浏览器版请上传文件。");
                if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入文件路径不能为空。"));
                if (!File.Exists(request.FilePath)) return Results.NotFound(new ApiErrorResponse("HS编码导入文件不存在。"));
                if (!IsAllowedHsCodeImportFileName(request.FilePath))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入仅支持 .xlsx 或 .xlsm 文件。"));
                try
                {
                    var preview = await hsCodeService.PreviewImportAsync(
                        request.FilePath,
                        ParseHsCodeImportMode(request.Mode),
                        request.SourceName,
                        request.EffectiveYear,
                        cancellationToken);
                    return Results.Ok(await StoreHsCodeImportPreviewAsync(pathProvider, preview, cancellationToken));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
            }).WithName("PreviewHsCodesImportFromPath")
            .Produces<ApiHsCodeImportPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/import-preview-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IHsCodeService hsCodeService,
                IAppPathProvider pathProvider,
                string? fileName,
                string? mode,
                string? sourceName,
                int? effectiveYear,
                CancellationToken cancellationToken) =>
            {
                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(pathProvider, "HsCodeImports", "hs-preview");
                try
                {
                    string safeFileName = NormalizeUploadedHsCodeImportFileName(fileName ?? string.Empty);
                    string importPath = Path.Combine(tempRoot, safeFileName);
                    await using (var output = File.Create(importPath))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.ExcelImportBytes,
                            cancellationToken);
                    }
                    if (new FileInfo(importPath).Length == 0) return Results.BadRequest(new ApiErrorResponse("HS编码导入文件不能为空。"));
                    var preview = await hsCodeService.PreviewImportAsync(
                        importPath,
                        ParseHsCodeImportMode(mode ?? string.Empty),
                        sourceName ?? string.Empty,
                        effectiveYear,
                        cancellationToken);
                    return Results.Ok(await StoreHsCodeImportPreviewAsync(pathProvider, preview, cancellationToken));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            }).Accepts<IFormFile>("application/octet-stream").WithName("PreviewHsCodesImportUpload")
            .Produces<ApiHsCodeImportPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/import-commit", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                IAppPathProvider pathProvider,
                ApiHsCodeImportCommitRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request == null || !Guid.TryParseExact(request.Token, "N", out _))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入预检令牌无效。"));
                string previewPath = GetHsCodeImportPreviewPath(pathProvider, request.Token);
                if (!File.Exists(previewPath)) return Results.NotFound(new ApiErrorResponse("导入预检已过期，请重新选择文件。"));
                try
                {
                    await using var input = File.OpenRead(previewPath);
                    var preview = await System.Text.Json.JsonSerializer.DeserializeAsync<HsCodeImportPreview>(input, cancellationToken: cancellationToken)
                        ?? throw new InvalidDataException("HS编码导入预检内容无效。");
                    var result = await hsCodeService.CommitImportAsync(preview, cancellationToken);
                    await knowledgeService.RefreshReplacementRelationsAsync(preview, cancellationToken);
                    return Results.Ok(new ApiHsCodeImportCommitResponse(
                        true, result.AddedCount, result.UpdatedCount, result.UnchangedCount,
                        result.SuspectedObsoleteCount, result.SkippedCount, result.Message));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(previewPath);
                }
            }).WithName("CommitHsCodesImport")
            .Produces<ApiHsCodeImportCommitResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/import-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeImportPathRequest request,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机 Excel 导入仅支持桌面版；浏览器版请上传文件。");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入文件路径不能为空。"));
                }

                string filePath = request.FilePath.Trim();
                if (!File.Exists(filePath))
                {
                    return Results.NotFound(new ApiErrorResponse("HS编码导入文件不存在。"));
                }

                if (!IsAllowedHsCodeImportFileName(filePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入仅支持 .xlsx 或 .xlsm 文件。"));
                }

                try
                {
                    await hsCodeService.ImportAsync(filePath);
                    return Results.Ok(await BuildHsCodeImportResponseAsync(repository, filePath, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("ImportHsCodesFromPath")
            .Produces<ApiHsCodeImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/import-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                IAppPathProvider pathProvider,
                string? fileName,
                CancellationToken cancellationToken) =>
            {

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "HsCodeImports",
                    "hs-upload");

                try
                {
                    string safeFileName = NormalizeUploadedHsCodeImportFileName(fileName ?? string.Empty);
                    string importPath = Path.Combine(tempRoot, safeFileName);
                    await using (var output = File.Create(importPath))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.ExcelImportBytes,
                            cancellationToken);
                    }

                    if (new FileInfo(importPath).Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("HS编码导入文件不能为空。"));
                    }

                    await hsCodeService.ImportAsync(importPath);
                    return Results.Ok(await BuildHsCodeImportResponseAsync(repository, safeFileName, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("UploadHsCodesImportFile")
            .Produces<ApiHsCodeImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
