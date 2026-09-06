using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

using ExportDocManager.Services.Time;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPaymentReportPdfEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/payments/{paymentId:int}/pdf/save-to-path", (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                int paymentId,
                ApiPaymentReportPdfRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机保存操作仅支持桌面版；浏览器版请使用 PDF 下载任务。");
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
                return AcceptedBackgroundJob(EnqueuePaymentReportPdfJob(
                    jobRunner,
                    user.Username,
                    paymentId,
                    templatePath,
                    destinationPath));
            })
            .WithName("StartPaymentVoucherPdfSaveToPathJob")
            .WithApiCapability(PermissionResourceCatalog.PaymentOutput, PermissionAction.ExportPdf)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/reports/payments/{paymentId:int}/pdf/download", (
                HttpContext context,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                IBusinessClock clock,
                int paymentId,
                ApiPaymentReportPdfRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                request ??= new ApiPaymentReportPdfRequest();
                request.DestinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "PaymentPdf",
                    $"Payment-{paymentId}-{clock.Now:yyyyMMdd-HHmmss}.pdf");
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
                    destinationPath));
            })
            .WithName("StartPaymentVoucherPdfDownloadJob")
            .WithApiCapability(PermissionResourceCatalog.PaymentOutput, PermissionAction.ExportPdf)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        }

        internal static IResult? ValidatePaymentReportPdfRequest(
            int paymentId,
            ApiPaymentReportPdfRequest? request,
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
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "PaymentReportPdf",
                "付款/报销单 PDF 生成",
                requestedBy,
                async (provider, jobContext) =>
                {
                    await DemandReportOutputAccessAsync(provider, ReportDocumentType.PaymentVoucher,
                        [paymentId], PermissionAction.ExportPdf, jobContext.CancellationToken);
                    jobContext.Report(
                        15,
                        "正在渲染付款/报销单",
                        $"付款/报销单 {paymentId}",
                        destinationPath);

                    var reportPdfRenderService = provider.GetRequiredService<IReportPdfRenderService>();
                    var pdfResult = await reportPdfRenderService.RenderPaymentVoucherPdfAsync(
                            new PaymentReportPdfRenderRequest
                            {
                                SourceId = paymentId,
                                TemplatePath = templatePath,
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
                    Body = new ApiPaymentReportPdfRequest
                    {
                        TemplatePath = templatePath,
                        DestinationPath = destinationPath
                    }
                }),
                initialOutputPath: destinationPath);
        }

        internal sealed class ApiPaymentReportPdfJobRetryRequest
        {
            public int PaymentId { get; set; }

            public ApiPaymentReportPdfRequest Body { get; set; } = new();
        }
    }
}
