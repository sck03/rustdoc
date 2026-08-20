using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static async Task<IResult> ExportSingleWindowSubmitPackageAsync(
            ISingleWindowHandoffPackageService handoffPackageService,
            ISettingsService settingsService,
            IAppPathProvider pathProvider,
            IBusinessClock clock,
            SingleWindowBusinessType businessType,
            int invoiceId,
            ApiSingleWindowSubmitPackageRequest request,
            CancellationToken cancellationToken)
        {
            string packagePath = string.IsNullOrWhiteSpace(request?.PackagePath)
                ? BuildDefaultSingleWindowSubmitPackagePath(pathProvider, clock, businessType, invoiceId)
                : request.PackagePath.Trim();

            try
            {
                await settingsService.LoadAsync();
                var result = await handoffPackageService.ExportSubmitPackageAsync(
                    businessType,
                    invoiceId,
                    request?.StationAssignmentCode ?? string.Empty,
                    packagePath,
                    cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromHandoffPackageResult(
                    result,
                    "单一窗口提交包已导出。"));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
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
        }

        private static async Task<IResult> DownloadSingleWindowSubmitPackageAsync(
            HttpContext context,
            ISingleWindowHandoffPackageService handoffPackageService,
            ISettingsService settingsService,
            IAppPathProvider pathProvider,
            IBusinessClock clock,
            SingleWindowBusinessType businessType,
            int invoiceId,
            ApiSingleWindowSubmitPackageRequest request,
            CancellationToken cancellationToken)
        {
            if (invoiceId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
            }

            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "COO" : "ACD";
            string packagePath = CreateBrowserDownloadPath(
                pathProvider,
                "SingleWindowSubmitPackage",
                $"{prefix}-{invoiceId}-{clock.Now:yyyyMMdd-HHmmss}.swpkg");
            string packageDirectory = Path.GetDirectoryName(packagePath) ?? string.Empty;
            bool cleanupRegistered = false;
            try
            {
                await settingsService.LoadAsync();
                var result = await handoffPackageService.ExportSubmitPackageAsync(
                    businessType,
                    invoiceId,
                    request?.StationAssignmentCode ?? string.Empty,
                    packagePath,
                    cancellationToken);
                var response = StreamTemporaryFile(
                    context,
                    result.PackagePath,
                    "application/octet-stream",
                    Path.GetFileName(result.PackagePath),
                    packageDirectory);
                cleanupRegistered = true;
                return response;
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (ResourceNotFoundException ex)
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            finally
            {
                if (!cleanupRegistered)
                {
                    AtomicFileHelper.TryDeleteDirectory(packageDirectory);
                }
            }
        }

        private static async Task<IResult> ExportSingleWindowReceiptPackageAsync(
            ISingleWindowHandoffPackageService handoffPackageService,
            IAppPathProvider pathProvider,
            IBusinessClock clock,
            ApiSingleWindowReceiptPackageExportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("回执包导出请求体不能为空。"));
            }

            if (!TryParseSingleWindowBusinessType(request.BusinessType, out var businessType))
            {
                return BadSingleWindowBusinessType();
            }

            var receiptFiles = (request.ReceiptFiles ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(PhysicalPathComparison.Comparer)
                .ToArray();
            if (receiptFiles.Length == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("回执文件列表不能为空。"));
            }

            string packagePath = string.IsNullOrWhiteSpace(request.PackagePath)
                ? BuildDefaultSingleWindowReceiptPackagePath(
                    pathProvider,
                    clock,
                    businessType,
                    request.BatchReference,
                    request.InvoiceNo)
                : request.PackagePath.Trim();

            try
            {
                var result = await handoffPackageService.ExportReceiptPackageAsync(
                    businessType,
                    request.BatchReference ?? string.Empty,
                    request.InvoiceNo ?? string.Empty,
                    receiptFiles,
                    packagePath,
                    cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromReceiptPackageResult(
                    result,
                    "单一窗口回执包已导出。"));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        private static async Task<IResult> DownloadSingleWindowReceiptPackageAsync(
            HttpContext context,
            ISingleWindowHandoffPackageService handoffPackageService,
            IAppPathProvider pathProvider,
            IBusinessClock clock,
            ApiSingleWindowReceiptPackageExportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("回执包下载请求体不能为空。"));
            }

            if (!TryParseSingleWindowBusinessType(request.BusinessType, out var businessType))
            {
                return BadSingleWindowBusinessType();
            }

            var receiptFiles = (request.ReceiptFiles ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(PhysicalPathComparison.Comparer)
                .ToArray();
            if (receiptFiles.Length == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("回执文件列表不能为空。"));
            }

            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "COO" : "ACD";
            string packagePath = CreateBrowserDownloadPath(
                pathProvider,
                "SingleWindowReceiptPackage",
                $"Receipt-{prefix}-{clock.Now:yyyyMMdd-HHmmss}.swpkg");
            string packageDirectory = Path.GetDirectoryName(packagePath) ?? string.Empty;
            bool cleanupRegistered = false;
            try
            {
                var result = await handoffPackageService.ExportReceiptPackageAsync(
                    businessType,
                    request.BatchReference ?? string.Empty,
                    request.InvoiceNo ?? string.Empty,
                    receiptFiles,
                    packagePath,
                    cancellationToken);
                var response = StreamTemporaryFile(
                    context,
                    result.PackagePath,
                    "application/octet-stream",
                    Path.GetFileName(result.PackagePath),
                    packageDirectory);
                cleanupRegistered = true;
                return response;
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            finally
            {
                if (!cleanupRegistered)
                {
                    AtomicFileHelper.TryDeleteDirectory(packageDirectory);
                }
            }
        }

        private static async Task<ApiSingleWindowSubmitPackageRequest> ReadSingleWindowSubmitPackageRequestAsync(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            if ((context.Request.ContentLength ?? 0) == 0)
            {
                return new ApiSingleWindowSubmitPackageRequest(string.Empty, string.Empty);
            }

            try
            {
                return await context.Request.ReadFromJsonAsync<ApiSingleWindowSubmitPackageRequest>(
                    cancellationToken: cancellationToken) ?? new ApiSingleWindowSubmitPackageRequest(string.Empty, string.Empty);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidDataException("提交包请求体必须是有效 JSON。", ex);
            }
        }

        private static async Task<IResult> ImportSingleWindowPackageAsync(
            ISingleWindowHandoffPackageService handoffPackageService,
            IAppPathProvider pathProvider,
            SingleWindowPackageType expectedPackageType,
            ApiSingleWindowImportPackageRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request?.PackagePath))
            {
                return Results.BadRequest(new ApiErrorResponse("交接包路径不能为空。"));
            }

            string packagePath = request.PackagePath.Trim();
            try
            {
                var manifest = ReadSingleWindowPackageManifest(packagePath);
                if (manifest.PackageType != expectedPackageType)
                {
                    string expectedText = expectedPackageType == SingleWindowPackageType.SubmitPackage
                        ? "单一窗口提交包"
                        : "单一窗口回执包";
                    return Results.BadRequest(new ApiErrorResponse($"所选文件不是{expectedText}。"));
                }

                string workingRoot = ResolveSingleWindowImportWorkingRoot(
                    pathProvider,
                    expectedPackageType,
                    request.WorkingDirectory);

                var imported = expectedPackageType == SingleWindowPackageType.SubmitPackage
                    ? await handoffPackageService.ImportSubmitPackageAsync(packagePath, workingRoot, cancellationToken)
                    : await handoffPackageService.ImportReceiptPackageAsync(packagePath, workingRoot, cancellationToken);
                bool keepWorkingDirectory = request.KeepWorkingDirectory;

                ApiSingleWindowImportedPackageResponse response;
                using (imported)
                {
                    if (keepWorkingDirectory)
                    {
                        imported.KeepWorkingDirectory();
                    }

                    response = ApiSingleWindowDtoFactory.FromImportedPackage(
                        packagePath,
                        imported,
                        keepWorkingDirectory,
                        expectedPackageType == SingleWindowPackageType.SubmitPackage
                            ? "单一窗口提交包已导入。"
                            : "单一窗口回执包已导入。");
                }

                return Results.Ok(response);
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
            catch (PayloadLimitExceededException ex)
            {
                return WritePayloadTooLarge(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        private static async Task<IResult> ImportSingleWindowUploadedPackageAsync(
            HttpContext context,
            ISingleWindowHandoffPackageService handoffPackageService,
            IAppPathProvider pathProvider,
            SingleWindowPackageType packageType,
            string? fileName,
            string? workingDirectory,
            bool keepWorkingDirectory,
            CancellationToken cancellationToken)
        {
            string safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".swpkg", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("请上传 .swpkg 单一窗口包。"));
            }

            var upload = BuildSingleWindowBrowserUploadPath(pathProvider, safeFileName);
            string uploadRoot = upload.UploadRoot;
            Directory.CreateDirectory(uploadRoot);
            string packagePath = upload.PackagePath;
            try
            {
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
                    return Results.BadRequest(new ApiErrorResponse("上传的单一窗口包为空。"));
                }

                return await ImportSingleWindowPackageAsync(
                    handoffPackageService,
                    pathProvider,
                    packageType,
                    new ApiSingleWindowImportPackageRequest(
                        packagePath,
                        workingDirectory?.Trim() ?? string.Empty,
                        keepWorkingDirectory),
                    cancellationToken);
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(uploadRoot);
            }
        }

    }
}
