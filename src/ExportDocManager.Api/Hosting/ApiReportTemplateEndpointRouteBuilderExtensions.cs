using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapReportTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/reports/templates", async (
                HttpContext context,
                IReportHtmlService reportHtmlService,
                string? reportType,
                CancellationToken cancellationToken) =>
            {

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                var templates = await reportHtmlService.GetAvailableTemplatesAsync(parsedReportType, cancellationToken);
                return Results.Ok(templates.Select(template => new ApiReportTemplateDto(
                    template.ReportType.ToString(),
                    template.DisplayName,
                    ToApiReportTemplatePath(context, template.TemplatePath),
                    template.WithSealDefault)));
            })
            .WithName("ListReportTemplates")
            .WithApiPermission(
                PermissionModuleCatalog.DocumentReports,
                selector: ApiPermissionSelector.ReportType)
            .Produces<IReadOnlyList<ApiReportTemplateDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/reports/templates", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateCreateRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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
                    return Results.Ok(ToApiReportTemplateContentDto(context, result));
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
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("CreateReportTemplate")
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/storage-check", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateStorageDiagnosticsService diagnosticsService,
                ApiDesktopAccessOptions desktopAccessOptions,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许检查模板目录可写性。");
                }

                var result = await diagnosticsService.CheckAsync(cancellationToken);
                return Results.Ok(new ApiReportTemplateStorageStatusResponse(
                    ApiResponsePathPolicy.Reveal(
                        result.TemplateRoot,
                        ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)),
                    result.Exists,
                    result.Writable,
                    result.Message,
                    result.StoragePolicy));
            })
            .WithName("CheckReportTemplateStorage")
            .Produces<ApiReportTemplateStorageStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/reports/templates/fields", (
                HttpContext context,
                IReportTemplateFieldCatalogService fieldCatalogService,
                string? reportType) =>
            {

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                var catalog = fieldCatalogService.GetFieldCatalog(parsedReportType);
                return Results.Ok(ToApiReportTemplateFieldCatalogDto(catalog));
            })
            .WithName("GetReportTemplateFieldCatalog")
            .Produces<ApiReportTemplateFieldCatalogResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/reports/templates/content", async (
                HttpContext context,
                IReportTemplateService reportTemplateService,
                string? reportType,
                string? templatePath,
                CancellationToken cancellationToken) =>
            {

                if (!TryParseReportDocumentType(reportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportTemplateService.GetTemplateContentAsync(
                        parsedReportType,
                        templatePath ?? string.Empty,
                        cancellationToken);
                    return Results.Ok(ToApiReportTemplateContentDto(context, result));
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
                    return WriteServiceException(ex);
                }
            })
            .WithName("GetReportTemplateContent")
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/reports/templates/content", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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
                    return Results.Ok(ToApiReportTemplateContentDto(context, result));
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
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SaveReportTemplateContent")
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/rename", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateRenameRequest request,
                CancellationToken cancellationToken) => ExecuteReportTemplateManagementAsync(
                    context,
                    authorizationService,
                    request?.ReportType,
                    request != null,
                    "当前权限模板不允许重命名报表模板。",
                    async parsedReportType => ToApiReportTemplateContentDto(
                        context,
                        await reportTemplateService.RenameTemplateAsync(
                            parsedReportType,
                            request!.TemplatePath,
                            request!.NewTemplatePath,
                            cancellationToken))))
            .WithName("RenameReportTemplate")
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/reports/templates/display-name", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateMetadataRequest request,
                CancellationToken cancellationToken) => ExecuteReportTemplateManagementAsync(
                    context,
                    authorizationService,
                    request?.ReportType,
                    request != null,
                    "当前权限模板不允许修改报表模板显示名称。",
                    async parsedReportType =>
                    {
                        var result = await reportTemplateService.UpdateTemplateDisplayNameAsync(
                            parsedReportType,
                            request!.TemplatePath,
                            request!.DisplayName,
                            cancellationToken);
                        return ToApiReportTemplateContentDto(context, result);
                    }))
            .WithName("UpdateReportTemplateDisplayName")
            .Produces<ApiReportTemplateContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/reports/templates/default", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                ApiReportTemplateMetadataRequest request,
                CancellationToken cancellationToken) => ExecuteReportTemplateManagementAsync(
                    context,
                    authorizationService,
                    request?.ReportType,
                    request != null,
                    "当前权限模板不允许设置默认报表模板。",
                    async parsedReportType =>
                    {
                        var result = await reportTemplateService.SetDefaultTemplateAsync(
                            parsedReportType,
                            request!.TemplatePath,
                            cancellationToken);
                        return new ApiCommandResponse(true, result.Message);
                    }))
            .WithName("SetDefaultReportTemplate")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/reports/templates/content", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplateService reportTemplateService,
                string? reportType,
                string? templatePath,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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
                        templatePath ?? string.Empty,
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
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("DeleteReportTemplate")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/package/save-to-path", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplatePackageService packageService,
                ApiReportTemplatePackageExportRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许导出模板包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请下载模板包。");
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
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SaveReportTemplatePackageToPath")
            .Produces<ApiReportTemplatePackageExportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/package/download", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplatePackageService packageService,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许下载模板包。");
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "TemplatePackages",
                    "edtpl-download");
                string packagePath = Path.Combine(tempRoot, BuildReportTemplatePackageFileName(clock.Now));
                bool cleanupRegistered = false;

                try
                {
                    var result = await packageService.ExportAsync(packagePath, cancellationToken: cancellationToken);
                    var response = StreamTemporaryFile(
                        context,
                        result.PackagePath,
                        "application/octet-stream",
                        Path.GetFileName(result.PackagePath),
                        tempRoot);
                    cleanupRegistered = true;
                    return response;
                }
                catch (ArgumentException ex)
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
                    if (!cleanupRegistered)
                    {
                        AtomicFileHelper.TryDeleteDirectory(tempRoot);
                    }
                }
            })
            .WithName("DownloadReportTemplatePackage")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/package/import", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IReportTemplatePackageService packageService,
                ApiReportTemplatePackageImportRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许导入模板包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机文件导入仅支持桌面版；浏览器版请上传模板包。");
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
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("ImportReportTemplatePackage")
            .Produces<ApiReportTemplatePackageImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/package/upload", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IReportTemplatePackageService packageService,
                IAppPathProvider pathProvider,
                string? strategy,
                string? fileName,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("当前权限模板不允许上传模板包。");
                }

                string rawStrategy = strategy ?? string.Empty;
                if (!TryParseReportTemplateImportStrategy(
                    string.IsNullOrWhiteSpace(rawStrategy) ? "Merge" : rawStrategy,
                    out var importStrategy))
                {
                    return Results.BadRequest(new ApiErrorResponse("模板包导入策略无效。"));
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "TemplatePackages",
                    "edtpl-upload");

                try
                {
                    string safeFileName = NormalizeUploadedReportTemplatePackageFileName(fileName ?? string.Empty);
                    string packagePath = Path.Combine(tempRoot, safeFileName);
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
                        importStrategy,
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
            .WithName("UploadReportTemplatePackage")
            .Produces<ApiReportTemplatePackageImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/templates/preview", async (
                HttpContext context,
                IReportTemplateService reportTemplateService,
                ApiReportTemplatePreviewRequest request,
                CancellationToken cancellationToken) =>
            {

                request ??= new ApiReportTemplatePreviewRequest();
                if (!TryParseReportDocumentType(request.ReportType, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                if (string.IsNullOrWhiteSpace(request.Content))
                {
                    return Results.BadRequest(new ApiErrorResponse("模板内容不能为空。"));
                }

                if (parsedReportType == ReportDocumentType.PaymentVoucher && request.WithSeal.HasValue)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款报销模板不接受任何印章配置，请移除 withSeal 字段。"));
                }

                try
                {
                    var result = await reportTemplateService.PreviewTemplateContentAsync(
                        parsedReportType,
                        request.Content,
                        parsedReportType == ReportDocumentType.ExportDocument && (request.WithSeal ?? true),
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
                    return WriteServiceException(ex);
                }
            })
            .WithName("PreviewReportTemplateContent")
            .Produces<ApiReportTemplatePreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);
        }

        private static async Task<IResult> ExecuteReportTemplateManagementAsync(
            HttpContext context,
            ApiAuthorizationService authorizationService,
            string? rawReportType,
            bool requestAvailable,
            string forbiddenMessage,
            Func<ReportDocumentType, Task<object>> operation)
        {
            var user = ApiEndpointAuth.GetRequiredUser(context);
            if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Manage))
            {
                return WriteForbidden(forbiddenMessage);
            }

            if (!requestAvailable)
            {
                return Results.BadRequest(new ApiErrorResponse("报表模板请求体不能为空。"));
            }

            if (!TryParseReportDocumentType(rawReportType, out var reportType))
            {
                return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
            }

            try
            {
                return Results.Ok(await operation(reportType));
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
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return WriteServiceException(ex);
            }
        }

        private static ApiReportTemplateContentDto ToApiReportTemplateContentDto(
            HttpContext context,
            ReportTemplateContentResult result)
        {
            return new ApiReportTemplateContentDto(
                result.ReportType.ToString(),
                result.DisplayName,
                ToApiReportTemplatePath(context, result.TemplatePath),
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

        private static string BuildReportTemplatePackageFileName(DateTimeOffset now)
        {
            return $"templates_{now:yyyyMMddHHmmss}.edtpl";
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
