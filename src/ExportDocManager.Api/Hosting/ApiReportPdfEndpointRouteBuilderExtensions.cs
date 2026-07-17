using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceReportPdfEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/pdf/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId,
                ApiReportPdfRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用 PDF 下载任务。");
                }

                var validation = ValidateReportPdfRequest(
                    invoiceId,
                    request,
                    out var reportType,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                string templatePath = request.TemplatePath?.Trim() ?? string.Empty;
                bool withSeal = request.WithSeal;
                return AcceptedBackgroundJob(EnqueueInvoiceReportPdfJob(
                    jobRunner,
                    user.Username,
                    invoiceId,
                    reportType,
                    templatePath,
                    withSeal,
                    destinationPath));
            })
            .WithName("StartInvoiceReportPdfSaveToPathJob");

            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/pdf/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId,
                ApiReportPdfRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                request ??= new ApiReportPdfRequest();
                request.DestinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "InvoicePdf",
                    $"Invoice-{invoiceId}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
                var validation = ValidateReportPdfRequest(
                    invoiceId,
                    request,
                    out var reportType,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueInvoiceReportPdfJob(
                    jobRunner,
                    user.Username,
                    invoiceId,
                    reportType,
                    request.TemplatePath?.Trim() ?? string.Empty,
                    request.WithSeal,
                    destinationPath));
            })
            .WithName("StartInvoiceReportPdfDownloadJob");
        }

        internal static IResult ValidateReportPdfRequest(
            int invoiceId,
            ApiReportPdfRequest request,
            out ReportDocumentType reportType,
            out string destinationPath)
        {
            reportType = ReportDocumentType.ExportDocument;
            destinationPath = string.Empty;

            if (invoiceId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
            }

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("报表 PDF 请求体不能为空。"));
            }

            if (!TryParseReportDocumentType(request.ReportType, out reportType)
                || reportType != ReportDocumentType.ExportDocument)
            {
                return Results.BadRequest(new ApiErrorResponse("发票 PDF 生成目前仅支持 ExportDocument 报表类型。"));
            }

            string output = request.DestinationPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(output))
            {
                return Results.BadRequest(new ApiErrorResponse("PDF 输出路径不能为空。"));
            }

            if (!string.Equals(Path.GetExtension(output), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("PDF 输出路径必须以 .pdf 结尾。"));
            }

            try
            {
                destinationPath = Path.GetFullPath(output);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"PDF 输出路径无效：{ex.Message}"));
            }
        }

        internal static BackgroundJobSnapshot EnqueueInvoiceReportPdfJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            int invoiceId,
            ReportDocumentType reportType,
            string templatePath,
            bool withSeal,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "ReportPdf",
                "报表 PDF 生成",
                requestedBy,
                async (provider, jobContext) =>
                {
                    jobContext.Report(
                        15,
                        "正在渲染报表 HTML",
                        $"发票 {invoiceId}",
                        destinationPath);

                    var reportPdfRenderService = provider.GetRequiredService<IReportPdfRenderService>();
                    var pdfResult = await reportPdfRenderService.RenderInvoicePdfAsync(
                            new ReportPdfRenderRequest
                            {
                                SourceId = invoiceId,
                                ReportType = reportType,
                                TemplatePath = templatePath,
                                WithSeal = withSeal,
                                DestinationPath = destinationPath,
                                DocumentTitle = $"Invoice-{invoiceId}"
                            },
                            jobContext.CancellationToken)
                        .ConfigureAwait(false);

                    jobContext.Report(
                        95,
                        "正在保存 PDF",
                        Path.GetFileName(pdfResult.DestinationPath),
                        pdfResult.DestinationPath);

                    return pdfResult.DestinationPath;
                },
                retryOperation: "StartInvoiceReportPdfJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiReportPdfJobRetryRequest
                {
                    InvoiceId = invoiceId,
                    Body = new ApiReportPdfRequest
                    {
                        ReportType = reportType.ToString(),
                        TemplatePath = templatePath,
                        WithSeal = withSeal,
                        DestinationPath = destinationPath
                    }
                }));
        }

        private sealed class ApiReportPdfJobRetryRequest
        {
            public int InvoiceId { get; set; }

            public ApiReportPdfRequest Body { get; set; } = new();
        }
    }
}
