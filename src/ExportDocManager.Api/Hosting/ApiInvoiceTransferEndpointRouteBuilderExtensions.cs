using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string InvoiceTransferStoragePolicy =
            "发票单据包只读取/写入用户显式选择的 .edpkg 路径；导出临时 JSON 写运行数据根 Cache/InvoiceTransfer 后立即清理；导入只写当前发票/商品/客户/出口商业务表，不读取付款/报销表，不创建默认导出目录或系统 C 盘用户目录落点。同号发票按 InvoiceNo + Type 判断，实际数据和报关数据互不覆盖。";

        private static void MapInvoiceTransferEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/invoices/{id:int}/transfer-package/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IInvoiceTransferService transferService,
                int id,
                ApiInvoiceTransferPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请直接下载单据包。");
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (request == null || string.IsNullOrWhiteSpace(request.PackagePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("单据包保存路径不能为空。"));
                }

                try
                {
                    string packagePath = await transferService.ExportAsync(
                        id,
                        request.PackagePath,
                        cancellationToken);

                    return Results.Ok(new ApiInvoiceTransferExportResponse(
                        true,
                        id,
                        packagePath,
                        InvoiceTransferStoragePolicy,
                        "发票单据包已导出。"));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("SaveInvoiceTransferPackageToPath");

            endpoints.MapPost("/api/invoices/{id:int}/transfer-package/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                IInvoiceTransferService transferService,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                string packagePath = CreateBrowserDownloadPath(
                    pathProvider,
                    "InvoiceTransfer",
                    $"Invoice-{id}-{DateTime.Now:yyyyMMdd-HHmmss}.edpkg");
                try
                {
                    string exportedPath = await transferService.ExportAsync(id, packagePath, cancellationToken);
                    byte[] content = await File.ReadAllBytesAsync(exportedPath, cancellationToken);
                    return Results.File(content, "application/octet-stream", Path.GetFileName(exportedPath));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            finally
            {
                string directory = Path.GetDirectoryName(packagePath) ?? string.Empty;
                AtomicFileHelper.TryDeleteDirectory(directory);
                }
            })
            .WithName("DownloadInvoiceTransferPackage");

            endpoints.MapPost("/api/invoices/transfer-package/preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IInvoiceTransferService transferService,
                ApiInvoiceTransferPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径预览仅允许可信 Tauri 桌面端；浏览器请上传单据包。");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.PackagePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("单据包路径不能为空。"));
                }

                try
                {
                    var read = await transferService.ReadPackageAsync(request.PackagePath, cancellationToken);
                    var preview = await transferService.PreviewAsync(read.Package, cancellationToken);
                    return Results.Ok(new ApiInvoiceTransferPreviewResponse(
                        read.ChecksumValid,
                        read.ChecksumMessage ?? string.Empty,
                        ToInvoiceTransferPreviewDto(preview),
                        InvoiceTransferStoragePolicy));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewInvoiceTransferPackage");

            endpoints.MapPost("/api/invoices/transfer-package/upload/preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                IInvoiceTransferService transferService,
                string fileName,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await ProcessUploadedInvoiceTransferPackageAsync(
                    context,
                    pathProvider,
                    fileName,
                    async packagePath =>
                    {
                        var read = await transferService.ReadPackageAsync(packagePath, cancellationToken);
                        var preview = await transferService.PreviewAsync(read.Package, cancellationToken);
                        return Results.Ok(new ApiInvoiceTransferPreviewResponse(
                            read.ChecksumValid,
                            read.ChecksumMessage ?? string.Empty,
                            ToInvoiceTransferPreviewDto(preview),
                            InvoiceTransferStoragePolicy));
                    },
                    cancellationToken);
            })
            .WithName("PreviewUploadedInvoiceTransferPackage");

            endpoints.MapPost("/api/invoices/transfer-package/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IInvoiceTransferService transferService,
                ApiInvoiceTransferImportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径导入仅允许可信 Tauri 桌面端；浏览器请上传单据包。");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.PackagePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("单据包路径不能为空。"));
                }

                if (!TryParseInvoiceTransferConflictAction(request.ConflictAction, out var action))
                {
                    return Results.BadRequest(new ApiErrorResponse("冲突处理方式必须是 Skip、Overwrite、NewInvoiceNo 或 AppendItems。"));
                }

                try
                {
                    var read = await transferService.ReadPackageAsync(request.PackagePath, cancellationToken);
                    var preview = await transferService.PreviewAsync(read.Package, cancellationToken);
                    if (!read.ChecksumValid && !request.AllowInvalidChecksum)
                    {
                        return WriteConflict($"单据包校验失败：{read.ChecksumMessage}");
                    }

                    var result = await transferService.ImportAsync(
                        read.Package,
                        action,
                        request.NewInvoiceNo,
                        cancellationToken);
                    var resultDto = ToInvoiceTransferImportResultDto(result);

                    return Results.Ok(new ApiInvoiceTransferImportResponse(
                        result.Success,
                        resultDto,
                        ToInvoiceTransferPreviewDto(preview),
                        InvoiceTransferStoragePolicy,
                        result.Message ?? "导入完成。"));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("ImportInvoiceTransferPackage");

            endpoints.MapPost("/api/invoices/transfer-package/upload/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                IInvoiceTransferService transferService,
                string fileName,
                string conflictAction,
                string newInvoiceNo,
                bool allowInvalidChecksum,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryParseInvoiceTransferConflictAction(conflictAction, out var action))
                {
                    return Results.BadRequest(new ApiErrorResponse("冲突处理方式必须是 Skip、Overwrite、NewInvoiceNo 或 AppendItems。"));
                }

                return await ProcessUploadedInvoiceTransferPackageAsync(
                    context,
                    pathProvider,
                    fileName,
                    async packagePath =>
                    {
                        var read = await transferService.ReadPackageAsync(packagePath, cancellationToken);
                        var preview = await transferService.PreviewAsync(read.Package, cancellationToken);
                        if (!read.ChecksumValid && !allowInvalidChecksum)
                        {
                            return WriteConflict($"单据包校验失败：{read.ChecksumMessage}");
                        }

                        var result = await transferService.ImportAsync(
                            read.Package,
                            action,
                            newInvoiceNo,
                            cancellationToken);
                        return Results.Ok(new ApiInvoiceTransferImportResponse(
                            result.Success,
                            ToInvoiceTransferImportResultDto(result),
                            ToInvoiceTransferPreviewDto(preview),
                            InvoiceTransferStoragePolicy,
                            result.Message ?? "导入完成。"));
                    },
                    cancellationToken);
            })
            .WithName("ImportUploadedInvoiceTransferPackage");
        }

        private static async Task<IResult> ProcessUploadedInvoiceTransferPackageAsync(
            HttpContext context,
            IAppPathProvider pathProvider,
            string fileName,
            Func<string, Task<IResult>> processAsync,
            CancellationToken cancellationToken)
        {
            string safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".edpkg", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("请上传 .edpkg 发票单据包。"));
            }

            string uploadRoot = Path.Combine(pathProvider.CacheRoot, "BrowserUploads", "InvoiceTransfer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uploadRoot);
            string packagePath = Path.Combine(uploadRoot, safeFileName);
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
                    return Results.BadRequest(new ApiErrorResponse("上传的发票单据包为空。"));
                }

                return await processAsync(packagePath);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PayloadLimitExceededException ex)
            {
                return WritePayloadTooLarge(ex);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(uploadRoot);
            }
        }

        private static ApiInvoiceTransferPreviewDto ToInvoiceTransferPreviewDto(InvoiceTransferPreview preview)
        {
            preview ??= new InvoiceTransferPreview();
            return new ApiInvoiceTransferPreviewDto(
                preview.InvoiceNo ?? string.Empty,
                preview.Type ?? string.Empty,
                preview.ItemCount,
                preview.CustomerExists,
                preview.ExporterExists,
                preview.InvoiceExists,
                preview.InvoiceMatches,
                preview.ExistingInvoiceId);
        }

        private static ApiInvoiceTransferImportResultDto ToInvoiceTransferImportResultDto(InvoiceImportResult result)
        {
            result ??= new InvoiceImportResult();
            return new ApiInvoiceTransferImportResultDto(
                result.Success,
                result.Message ?? string.Empty,
                result.InvoiceId,
                result.FinalInvoiceNo ?? string.Empty,
                result.ActionTaken.ToString());
        }

        private static bool TryParseInvoiceTransferConflictAction(
            string action,
            out InvoiceImportConflictAction conflictAction)
        {
            if (Enum.TryParse(action?.Trim(), ignoreCase: true, out conflictAction) &&
                Enum.IsDefined(typeof(InvoiceImportConflictAction), conflictAction))
            {
                return true;
            }

            conflictAction = default;
            return false;
        }
    }
}
