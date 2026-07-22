using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPaymentReportPdfEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/payments/{paymentId:int}/pdf/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                int paymentId,
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

                var validation = ValidatePaymentReportPdfRequest(
                    paymentId,
                    request,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                string templatePath = request.TemplatePath?.Trim() ?? string.Empty;
                bool withSeal = request.WithSeal;
                return AcceptedBackgroundJob(EnqueuePaymentReportPdfJob(
                    jobRunner,
                    user.Username,
                    paymentId,
                    templatePath,
                    withSeal,
                    destinationPath));
            })
            .WithName("StartPaymentVoucherPdfSaveToPathJob");

            endpoints.MapPost("/api/reports/payments/{paymentId:int}/pdf/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                int paymentId,
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
                    "PaymentPdf",
                    $"Payment-{paymentId}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
                var validation = ValidatePaymentReportPdfRequest(
                    paymentId,
                    request,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueuePaymentReportPdfJob(
                    jobRunner,
                    user.Username,
                    paymentId,
                    request.TemplatePath?.Trim() ?? string.Empty,
                    request.WithSeal,
                    destinationPath));
            })
            .WithName("StartPaymentVoucherPdfDownloadJob");
        }

        internal static IResult ValidatePaymentReportPdfRequest(
            int paymentId,
            ApiReportPdfRequest request,
            out string destinationPath)
        {
            destinationPath = string.Empty;

            if (paymentId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("付款/报销单 ID 必须大于0。"));
            }

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("付款/报销单 PDF 请求体不能为空。"));
            }

            if (!TryParseReportDocumentType(request.ReportType, out var reportType)
                || reportType != ReportDocumentType.PaymentVoucher)
            {
                return Results.BadRequest(new ApiErrorResponse("付款/报销单 PDF 生成仅支持 PaymentVoucher 报表类型。"));
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

        internal static BackgroundJobSnapshot EnqueuePaymentReportPdfJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            int paymentId,
            string templatePath,
            bool withSeal,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "PaymentReportPdf",
                "付款/报销单 PDF 生成",
                requestedBy,
                async (provider, jobContext) =>
                {
                    jobContext.Report(
                        15,
                        "正在渲染付款/报销单",
                        $"付款/报销单 {paymentId}",
                        destinationPath);

                    var reportPdfRenderService = provider.GetRequiredService<IReportPdfRenderService>();
                    var pdfResult = await reportPdfRenderService.RenderPaymentVoucherPdfAsync(
                            new ReportPdfRenderRequest
                            {
                                SourceId = paymentId,
                                ReportType = ReportDocumentType.PaymentVoucher,
                                TemplatePath = templatePath,
                                WithSeal = withSeal,
                                DestinationPath = destinationPath,
                                DocumentTitle = $"PaymentVoucher-{paymentId}"
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
                retryOperation: "StartPaymentVoucherPdfJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiPaymentReportPdfJobRetryRequest
                {
                    PaymentId = paymentId,
                    Body = new ApiReportPdfRequest
                    {
                        ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                        TemplatePath = templatePath,
                        WithSeal = withSeal,
                        DestinationPath = destinationPath
                    }
                }),
                initialOutputPath: destinationPath);
        }

        internal sealed class ApiPaymentReportPdfJobRetryRequest
        {
            public int PaymentId { get; set; }

            public ApiReportPdfRequest Body { get; set; } = new();
        }
    }
}
