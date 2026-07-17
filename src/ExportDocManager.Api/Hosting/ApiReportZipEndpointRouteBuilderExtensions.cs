using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceReportPdfZipEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/pdf-zip/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiInvoiceReportZipRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用 ZIP 下载任务。");
                }

                var validation = ValidateInvoiceReportZipRequest(
                    request,
                    out var invoiceIds,
                    out var reportType,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                string templatePath = request.TemplatePath?.Trim() ?? string.Empty;
                bool withSeal = request.WithSeal;
                return AcceptedBackgroundJob(EnqueueInvoiceReportPdfZipJob(
                    jobRunner,
                    user.Username,
                    invoiceIds,
                    reportType,
                    templatePath,
                    withSeal,
                    destinationPath));
            })
            .WithName("StartInvoiceReportPdfZipSaveToPathJob");

            endpoints.MapPost("/api/reports/invoices/pdf-zip/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                ApiInvoiceReportZipRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                request ??= new ApiInvoiceReportZipRequest();
                request.DestinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "InvoicePdfZip",
                    $"InvoiceReports-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                var validation = ValidateInvoiceReportZipRequest(
                    request,
                    out var invoiceIds,
                    out var reportType,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueInvoiceReportPdfZipJob(
                    jobRunner,
                    user.Username,
                    invoiceIds,
                    reportType,
                    request.TemplatePath?.Trim() ?? string.Empty,
                    request.WithSeal,
                    destinationPath));
            })
            .WithName("StartInvoiceReportPdfZipDownloadJob");
        }

        internal static IResult ValidateInvoiceReportZipRequest(
            ApiInvoiceReportZipRequest request,
            out IReadOnlyList<int> invoiceIds,
            out ReportDocumentType reportType,
            out string destinationPath)
        {
            invoiceIds = Array.Empty<int>();
            reportType = ReportDocumentType.ExportDocument;
            destinationPath = string.Empty;

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("批量报表 ZIP 请求体不能为空。"));
            }

            var ids = (request.InvoiceIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("请至少选择一个发票ID。"));
            }

            if (ids.Count > 200)
            {
                return Results.BadRequest(new ApiErrorResponse("单次批量导出最多支持 200 张发票。"));
            }

            if (!TryParseReportDocumentType(request.ReportType, out reportType)
                || reportType != ReportDocumentType.ExportDocument)
            {
                return Results.BadRequest(new ApiErrorResponse("批量发票 ZIP 目前仅支持 ExportDocument 报表类型。"));
            }

            string output = request.DestinationPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(output))
            {
                return Results.BadRequest(new ApiErrorResponse("ZIP 输出路径不能为空。"));
            }

            if (!string.Equals(Path.GetExtension(output), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("ZIP 输出路径必须以 .zip 结尾。"));
            }

            try
            {
                invoiceIds = ids;
                destinationPath = Path.GetFullPath(output);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"ZIP 输出路径无效：{ex.Message}"));
            }
        }

        internal static BackgroundJobSnapshot EnqueueInvoiceReportPdfZipJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            IReadOnlyList<int> invoiceIds,
            ReportDocumentType reportType,
            string templatePath,
            bool withSeal,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "ReportPdfZip",
                "批量报表 ZIP 导出",
                requestedBy,
                async (provider, jobContext) =>
                {
                    var pathProvider = provider.GetRequiredService<IAppPathProvider>();
                    string tempRoot = Path.Combine(
                        pathProvider.CacheRoot,
                        "ReportBatchZip",
                        jobContext.JobId);

                    try
                    {
                        Directory.CreateDirectory(tempRoot);
                        var reportPdfRenderService = provider.GetRequiredService<IReportPdfRenderService>();
                        var entries = new List<(string SourcePath, string EntryName)>();

                        for (var index = 0; index < invoiceIds.Count; index++)
                        {
                            jobContext.CancellationToken.ThrowIfCancellationRequested();
                            int invoiceId = invoiceIds[index];
                            int progress = 10 + (int)Math.Round(index * 70d / invoiceIds.Count);
                            jobContext.Report(
                                progress,
                                "正在生成报表 PDF",
                                $"发票 {invoiceId} ({index + 1}/{invoiceIds.Count})",
                                destinationPath);

                            string pdfFileName = BuildInvoiceReportZipPdfFileName(index, invoiceId);
                            string pdfPath = Path.Combine(tempRoot, pdfFileName);
                            var pdfResult = await reportPdfRenderService.RenderInvoicePdfAsync(
                                    new ReportPdfRenderRequest
                                    {
                                        SourceId = invoiceId,
                                        ReportType = reportType,
                                        TemplatePath = templatePath,
                                        WithSeal = withSeal,
                                        DestinationPath = pdfPath,
                                        DocumentTitle = $"Invoice-{invoiceId}"
                                    },
                                    jobContext.CancellationToken)
                                .ConfigureAwait(false);

                            entries.Add((pdfResult.DestinationPath, pdfFileName));
                        }

                        jobContext.Report(
                            85,
                            "正在生成 ZIP",
                            Path.GetFileName(destinationPath),
                            destinationPath);

                        var zipProgress = new Progress<OperationProgressUpdate>(update =>
                        {
                            jobContext.Report(
                                update.ProgressPercent,
                                update.StatusText,
                                update.DetailText,
                                destinationPath);
                        });

                        await ZipArchiveHelper.CreateFromFilesAsync(
                                entries,
                                destinationPath,
                                jobContext.CancellationToken,
                                zipProgress,
                                "正在生成 ZIP",
                                85,
                                98,
                                "当前没有需要打包的报表 PDF。")
                            .ConfigureAwait(false);

                        jobContext.Report(
                            99,
                            "正在保存 ZIP",
                            Path.GetFileName(destinationPath),
                            destinationPath);

                        return destinationPath;
                    }
                    finally
                    {
                        AtomicFileHelper.TryDeleteDirectory(tempRoot);
                    }
                },
                retryOperation: "StartInvoiceReportPdfZipJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiInvoiceReportZipRequest
                {
                    InvoiceIds = invoiceIds.ToList(),
                    ReportType = reportType.ToString(),
                    TemplatePath = templatePath,
                    WithSeal = withSeal,
                    DestinationPath = destinationPath
                }));
        }

        private static string BuildInvoiceReportZipPdfFileName(int index, int invoiceId)
        {
            return $"{index + 1:000}-invoice-{invoiceId}.pdf";
        }
    }
}
