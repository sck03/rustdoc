using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapReportTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/reports/templates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                string reportType,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                var templates = await reportHtmlService.GetAvailableTemplatesAsync(parsedReportType, cancellationToken);
                return Results.Ok(templates.Select(template => new ApiReportTemplateDto(
                    template.ReportType.ToString(),
                    template.DisplayName,
                    template.TemplatePath,
                    template.WithSealDefault)));
            })
            .WithName("ListReportTemplates");

            endpoints.MapPost("/api/reports/templates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateCreateRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许新建报表模板。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.CreateTemplateAsync(
                        parsedReportType,
                        request.TemplatePath,
                        request.DisplayName,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(result));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateReportTemplate");

            endpoints.MapPost("/api/reports/templates/storage-check", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplateStorageDiagnosticsService diagnosticsService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许检查模板目录可写性。");
                }

                var result = await diagnosticsService.CheckAsync(cancellationToken);
                return Results.Ok(new ApiReportTemplateStorageStatusResponse(
                    result.TemplateRoot,
                    result.Exists,
                    result.Writable,
                    result.Message,
                    result.StoragePolicy));
            })
            .WithName("CheckReportTemplateStorage");

            endpoints.MapGet("/api/reports/templates/fields", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportTemplateFieldCatalogService fieldCatalogService,
                string reportType) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                var catalog = fieldCatalogService.GetFieldCatalog(parsedReportType);
                return Results.Ok(ToApiReportTemplateFieldCatalogDto(catalog));
            })
            .WithName("GetReportTemplateFieldCatalog");

            endpoints.MapGet("/api/reports/templates/content", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportTemplateService reportTemplateService,
                string reportType,
                string templatePath,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.GetTemplateContentAsync(
                        parsedReportType,
                        templatePath,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(result));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("GetReportTemplateContent");

            endpoints.MapPut("/api/reports/templates/content", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许保存报表模板。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.SaveTemplateContentAsync(
                        parsedReportType,
                        request.TemplatePath,
                        request.Content ?? string.Empty,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(result));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("SaveReportTemplateContent");

            endpoints.MapPost("/api/reports/templates/rename", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateRenameRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许重命名报表模板。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板请求体不能为空。"));
                }

                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.RenameTemplateAsync(
                        parsedReportType,
                        request.TemplatePath,
                        request.NewTemplatePath,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(result));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("RenameReportTemplate");

            endpoints.MapDelete("/api/reports/templates/content", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                string reportType,
                string templatePath,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许删除报表模板。");
                }

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.DeleteTemplateAsync(
                        parsedReportType,
                        templatePath,
                        cancellationToken);
                    return Results.Ok(new ApiCommandResponse(true, result.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteReportTemplate");

            endpoints.MapPost("/api/reports/templates/package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplatePackageService packageService,
                ApiReportTemplatePackageExportRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许导出模板包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请下载模板包。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("模板包请求体不能为空。"));
                }

                try
                {
                    var result = await packageService.ExportAsync(request.PackagePath, cancellationToken: cancellationToken);
                    return Results.Ok(new ApiReportTemplatePackageExportResponse(
                        result.PackagePath,
                        result.TemplateCount,
                        result.StoragePolicy));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("SaveReportTemplatePackageToPath");

            endpoints.MapPost("/api/reports/templates/package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplatePackageService packageService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许下载模板包。");
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "TemplatePackages",
                    "edtpl-download");
                string packagePath = Path.Combine(tempRoot, BuildReportTemplatePackageFileName());

                try
                {
                    var result = await packageService.ExportAsync(packagePath, cancellationToken: cancellationToken);
                    byte[] bytes = await File.ReadAllBytesAsync(result.PackagePath, cancellationToken);
                    return Results.File(
                        bytes,
                        "application/octet-stream",
                        Path.GetFileName(result.PackagePath));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            })
            .WithName("DownloadReportTemplatePackage");

            endpoints.MapPost("/api/reports/templates/package/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplatePackageService packageService,
                ApiReportTemplatePackageImportRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许导入模板包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径导入仅允许可信 Tauri 桌面端；浏览器请上传模板包。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("模板包请求体不能为空。"));
                }

                if (!TryParseReportTemplateImportStrategy(request.Strategy, out var strategy))
                {
                    return Results.BadRequest(new ApiErrorResponse("模板包导入策略无效。"));
                }

                try
                {
                    var result = await packageService.ImportAsync(
                        request.PackagePath,
                        strategy,
                        cancellationToken: cancellationToken);
                    return Results.Ok(new ApiReportTemplatePackageImportResponse(
                        result.TemplateCount,
                        result.PackageVersion,
                        result.StoragePolicy));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
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
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("ImportReportTemplatePackage");

            endpoints.MapPost("/api/reports/templates/package/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IReportTemplatePackageService packageService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许上传模板包。");
                }

                string rawStrategy = context.Request.Query["strategy"].ToString();
                if (!TryParseReportTemplateImportStrategy(
                    string.IsNullOrWhiteSpace(rawStrategy) ? "Merge" : rawStrategy,
                    out var strategy))
                {
                    return Results.BadRequest(new ApiErrorResponse("模板包导入策略无效。"));
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "TemplatePackages",
                    "edtpl-upload");

                try
                {
                    string fileName = NormalizeUploadedReportTemplatePackageFileName(
                        context.Request.Query["fileName"].ToString());
                    string packagePath = Path.Combine(tempRoot, fileName);
                    await using (var output = File.Create(packagePath))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.PackageImportBytes,
                            cancellationToken);
                    }

                    if (new FileInfo(packagePath).Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("模板包文件不能为空。"));
                    }

                    var result = await packageService.ImportAsync(
                        packagePath,
                        strategy,
                        cancellationToken: cancellationToken);
                    return Results.Ok(new ApiReportTemplatePackageImportResponse(
                        result.TemplateCount,
                        result.PackageVersion,
                        result.StoragePolicy));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
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
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            })
            .WithName("UploadReportTemplatePackage");

            endpoints.MapPost("/api/reports/templates/preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplatePreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                request ??= new ApiReportTemplatePreviewRequest();
                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return Results.BadRequest(new ApiErrorResponse("模板内容不能为空。"));
                }

                try
                {
                    var result = await reportTemplateService.PreviewTemplateContentAsync(
                        parsedReportType,
                        request.Content,
                        request.WithSeal,
                        cancellationToken);

                    return Results.Ok(new ApiReportTemplatePreviewResponse(
                        result.ReportType.ToString(),
                        result.WithSeal,
                        result.Html));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewReportTemplateContent");
        }

        private static ApiReportTemplateContentDto ToApiReportTemplateContentDto(ReportTemplateContentResult result)
        {
            return new ApiReportTemplateContentDto(
                result.ReportType.ToString(),
                result.DisplayName,
                result.TemplatePath,
                result.WithSealDefault,
                result.Content,
                result.StoragePolicy);
        }

        private static ApiReportTemplateFieldCatalogResponse ToApiReportTemplateFieldCatalogDto(
            ReportTemplateFieldCatalog catalog)
        {
            return new ApiReportTemplateFieldCatalogResponse(
                catalog.ReportType.ToString(),
                catalog.CategoryOrder,
                catalog.Fields
                    .Select(field => new ApiReportTemplateFieldDto(
                        field.ReportType.ToString(),
                        field.Category,
                        field.Label,
                        field.Value))
                    .ToArray());
        }

        private static bool TryParseReportTemplateImportStrategy(
            string rawStrategy,
            out ReportTemplateImportStrategy strategy)
        {
            return Enum.TryParse(rawStrategy, ignoreCase: true, out strategy);
        }

        private static string BuildReportTemplatePackageFileName()
        {
            return $"templates_{DateTime.Now:yyyyMMddHHmmss}.edtpl";
        }

        private static string NormalizeUploadedReportTemplatePackageFileName(string fileName)
        {
            string normalized = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "uploaded_template_package.edtpl";
            }

            string extension = Path.GetExtension(normalized);
            if (string.IsNullOrWhiteSpace(extension))
            {
                normalized += ".edtpl";
                extension = ".edtpl";
            }

            if (!string.Equals(extension, ".edtpl", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("模板包文件只支持 .edtpl 或 .zip。");
            }

            return normalized;
        }
    }
}
