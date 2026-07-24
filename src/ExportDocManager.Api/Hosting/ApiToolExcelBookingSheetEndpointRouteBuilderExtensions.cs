using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExcelBookingSheetEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/excel/booking-sheet/blank/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiExcelOutputRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用 Excel 下载任务。");
                }

                var validation = ValidateExcelDestinationPath(
                    request?.DestinationPath,
                    "空白托单输出路径",
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueBlankBookingSheetExportJob(jobRunner, user.Username, destinationPath));
            })
            .WithName("StartBlankBookingSheetSaveToPathJob");

            endpoints.MapPost("/api/tools/excel/booking-sheet/blank/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                string destinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "BlankBookingSheet",
                    "空白托单模板.xlsx");
                return AcceptedBackgroundJob(EnqueueBlankBookingSheetExportJob(
                    jobRunner,
                    user.Username,
                    destinationPath));
            })
            .WithName("StartBlankBookingSheetDownloadJob");

            endpoints.MapPost("/api/tools/excel/booking-sheet/convert/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiExcelConvertBookingSheetRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径转换仅允许可信 Tauri 桌面端；浏览器请上传 Excel 后下载转换结果。");
                }

                var sourceValidation = ValidateExcelSourcePath(
                    request?.SourcePath,
                    "导入模板源文件",
                    out string sourcePath);
                if (sourceValidation != null)
                {
                    return sourceValidation;
                }

                var destinationValidation = ValidateExcelDestinationPath(
                    request?.DestinationPath,
                    "订舱托单输出路径",
                    out string destinationPath);
                if (destinationValidation != null)
                {
                    return destinationValidation;
                }

                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new ApiErrorResponse("订舱托单必须另存为新文件，不能覆盖源 Excel。"));
                }

                return AcceptedBackgroundJob(EnqueueBookingSheetConvertJob(
                    jobRunner,
                    user.Username,
                    sourcePath,
                    destinationPath));
            })
            .WithName("StartBookingSheetConvertSaveToPathJob");

            endpoints.MapPost("/api/tools/excel/booking-sheet/convert/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                string fileName,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                string safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
                if (!IsSupportedExcelSourceExtension(safeFileName))
                {
                    return Results.BadRequest(new ApiErrorResponse("请上传 .xlsx、.xlsm、.xltx、.xltm 或 .xls 文件。"));
                }

                string uploadRoot = Path.Combine(pathProvider.CacheRoot, "BrowserUploads", "Excel", Guid.NewGuid().ToString("N"));
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
                        AtomicFileHelper.TryDeleteDirectory(uploadRoot);
                        return Results.BadRequest(new ApiErrorResponse("上传的 Excel 文件为空。"));
                    }

                    string destinationPath = CreateBrowserDownloadPath(
                        pathProvider,
                        "BookingSheetConvert",
                        $"{Path.GetFileNameWithoutExtension(safeFileName)}-BookingSheet.xlsx");
                    return AcceptedBackgroundJob(EnqueueBookingSheetConvertJob(
                        jobRunner,
                        user.Username,
                        sourcePath,
                        destinationPath,
                        deleteSourceAfterCompletion: true,
                        enableRetry: false));
                }
                catch (PayloadLimitExceededException ex)
                {
                    AtomicFileHelper.TryDeleteDirectory(uploadRoot);
                    return WritePayloadTooLarge(ex);
                }
                catch
                {
                    AtomicFileHelper.TryDeleteDirectory(uploadRoot);
                    throw;
                }
            })
            .WithName("UploadAndStartBookingSheetConvertDownloadJob");

            endpoints.MapPost("/api/tools/excel/booking-sheet/from-invoice/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IInvoiceService invoiceService,
                ApiBackgroundJobRunner jobRunner,
                ApiInvoiceBookingSheetRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用发票托单下载任务。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票托单导出请求体不能为空。"));
                }

                if (request.InvoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                var destinationValidation = ValidateExcelDestinationPath(
                    request.DestinationPath,
                    "订舱托单输出路径",
                    out string destinationPath);
                if (destinationValidation != null)
                {
                    return destinationValidation;
                }

                var invoice = await invoiceService.GetInvoiceByIdAsync(request.InvoiceId);
                if (invoice == null)
                {
                    return Results.NotFound(new ApiErrorResponse("未找到指定的发票。"));
                }

                return AcceptedBackgroundJob(EnqueueInvoiceBookingSheetExportJob(
                    jobRunner,
                    user.Username,
                    invoice,
                    request.InvoiceId,
                    destinationPath));
            })
            .WithName("StartInvoiceBookingSheetSaveToPathJob");

            endpoints.MapPost("/api/tools/excel/booking-sheet/from-invoice/{invoiceId:int}/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                var invoice = await invoiceService.GetInvoiceByIdAsync(invoiceId);
                if (invoice == null)
                {
                    return Results.NotFound(new ApiErrorResponse("未找到指定的发票。"));
                }

                string fileName = string.IsNullOrWhiteSpace(invoice.InvoiceNo)
                    ? $"Invoice-{invoiceId}-BookingSheet.xlsx"
                    : $"{invoice.InvoiceNo}-BookingSheet.xlsx";
                string destinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "InvoiceBookingSheet",
                    fileName);
                return AcceptedBackgroundJob(EnqueueInvoiceBookingSheetExportJob(
                    jobRunner,
                    user.Username,
                    invoice,
                    invoiceId,
                    destinationPath));
            })
            .WithName("StartInvoiceBookingSheetDownloadJob");
        }

        internal static BackgroundJobSnapshot EnqueueBlankBookingSheetExportJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "BlankBookingSheetExport",
                "导出空白订舱托单",
                requestedBy,
                async (provider, jobContext) =>
                {
                    jobContext.Report(10, "正在准备空白托单", "读取随程序目录 Resources/ExcelTemplates。", destinationPath);
                    await provider.GetRequiredService<ISettingsService>().LoadAsync();
                    jobContext.CancellationToken.ThrowIfCancellationRequested();

                    var templateService = provider.GetRequiredService<IExcelImportTemplateService>();
                    string outputPath = await Task.Run(
                        () => templateService.ExportBlankBookingSheet(destinationPath, overwrite: true),
                        jobContext.CancellationToken);

                    jobContext.Report(95, "正在保存空白托单", Path.GetFileName(outputPath), outputPath);
                    return outputPath;
                },
                retryOperation: "StartBlankBookingSheetExportJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiExcelOutputRequest
                {
                    DestinationPath = destinationPath
                }),
                initialOutputPath: destinationPath);
        }

        internal static BackgroundJobSnapshot EnqueueBookingSheetConvertJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            string sourcePath,
            string destinationPath,
            bool deleteSourceAfterCompletion = false,
            bool enableRetry = true)
        {
            return jobRunner.Enqueue(
                "BookingSheetConvert",
                "导入模板转订舱托单",
                requestedBy,
                async (provider, jobContext) =>
                {
                    try
                    {
                        jobContext.Report(10, "正在读取导入模板", Path.GetFileName(sourcePath), destinationPath);
                        await provider.GetRequiredService<ISettingsService>().LoadAsync();
                        jobContext.CancellationToken.ThrowIfCancellationRequested();

                        var templateService = provider.GetRequiredService<IExcelImportTemplateService>();
                        string outputPath = await Task.Run(
                            () => templateService.ExportBookingSheet(sourcePath, destinationPath, overwrite: true),
                            jobContext.CancellationToken);

                        jobContext.Report(95, "正在保存订舱托单", Path.GetFileName(outputPath), outputPath);
                        return outputPath;
                    }
                    finally
                    {
                        if (deleteSourceAfterCompletion)
                        {
                            string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
                            AtomicFileHelper.TryDeleteDirectory(sourceDirectory);
                        }
                    }
                },
                retryOperation: enableRetry ? "StartBookingSheetConvertJob" : string.Empty,
                retryRequestJson: enableRetry
                    ? SerializeBackgroundJobRetryRequest(new ApiExcelConvertBookingSheetRequest
                    {
                        SourcePath = sourcePath,
                        DestinationPath = destinationPath
                    })
                    : string.Empty,
                initialOutputPath: destinationPath);
        }

        internal static BackgroundJobSnapshot EnqueueInvoiceBookingSheetExportJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            Invoice invoice,
            int invoiceId,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "InvoiceBookingSheetExport",
                "从发票导出订舱托单",
                requestedBy,
                async (provider, jobContext) =>
                {
                    string invoiceNo = string.IsNullOrWhiteSpace(invoice.InvoiceNo)
                        ? $"ID {invoice.Id}"
                        : invoice.InvoiceNo;

                    jobContext.Report(10, "正在准备发票托单", $"对应发票：{invoiceNo}", destinationPath);
                    await provider.GetRequiredService<ISettingsService>().LoadAsync();
                    jobContext.CancellationToken.ThrowIfCancellationRequested();

                    var templateService = provider.GetRequiredService<IExcelImportTemplateService>();
                    string outputPath = await Task.Run(
                        () => templateService.ExportBookingSheetFromInvoice(invoice, destinationPath, overwrite: true),
                        jobContext.CancellationToken);

                    jobContext.Report(95, "正在保存发票托单", Path.GetFileName(outputPath), outputPath);
                    return outputPath;
                },
                retryOperation: "StartInvoiceBookingSheetExportJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiInvoiceBookingSheetRequest
                {
                    InvoiceId = invoiceId,
                    DestinationPath = destinationPath
                }),
                initialOutputPath: destinationPath);
        }
    }
}
