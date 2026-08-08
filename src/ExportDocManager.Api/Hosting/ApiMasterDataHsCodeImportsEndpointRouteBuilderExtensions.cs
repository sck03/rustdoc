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
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
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
            }).WithName("PreviewHsCodesImportFromPath");

            endpoints.MapPost("/api/master-data/hs-codes/import-preview-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IHsCodeService hsCodeService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(pathProvider, "HsCodeImports", "hs-preview");
                try
                {
                    string fileName = NormalizeUploadedHsCodeImportFileName(context.Request.Query["fileName"].ToString());
                    string importPath = Path.Combine(tempRoot, fileName);
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
                        ParseHsCodeImportMode(context.Request.Query["mode"].ToString()),
                        context.Request.Query["sourceName"].ToString(),
                        int.TryParse(context.Request.Query["effectiveYear"], out int year) ? year : null,
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
            }).WithName("PreviewHsCodesImportUpload");

            endpoints.MapPost("/api/master-data/hs-codes/import-commit", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                IAppPathProvider pathProvider,
                ApiHsCodeImportCommitRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
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
            }).WithName("CommitHsCodesImport");

            endpoints.MapPost("/api/master-data/hs-codes/import-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeImportPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
            .WithName("ImportHsCodesFromPath");

            endpoints.MapPost("/api/master-data/hs-codes/import-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "HsCodeImports",
                    "hs-upload");

                try
                {
                    string fileName = NormalizeUploadedHsCodeImportFileName(
                        context.Request.Query["fileName"].ToString());
                    string importPath = Path.Combine(tempRoot, fileName);
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
                    return Results.Ok(await BuildHsCodeImportResponseAsync(repository, fileName, cancellationToken));
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
            .WithName("UploadHsCodesImportFile");
        }
    }
}
