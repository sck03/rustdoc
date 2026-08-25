using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRetryDispatcher
    {
        private IResult RetryInvoiceReportPdfJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiReportPdfJobRetryRequest>(sourceJob, out var reportRequest, out var reportError))
            {
                return reportError ?? Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            if (reportRequest is null)
            {
                return Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            var reportValidation = ApiEndpointRouteBuilderExtensions.ValidateReportPdfRequest(
                reportRequest.InvoiceId,
                reportRequest.Body,
                out var reportType,
                out string reportDestinationPath);
            if (reportValidation != null)
            {
                return reportValidation;
            }

            string reportTemplatePath = reportRequest.Body.TemplatePath?.Trim() ?? string.Empty;
            bool reportWithSeal = reportRequest.Body.WithSeal;
            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueInvoiceReportPdfJob(
                    jobRunner,
                    requestedBy,
                    reportRequest.InvoiceId,
                    reportType,
                    reportTemplatePath,
                    reportWithSeal,
                    reportDestinationPath));
        }

        private IResult RetryInvoiceReportPdfZipJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiInvoiceReportZipRequest>(sourceJob, out var zipRequest, out var zipError))
            {
                return zipError ?? Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            var zipValidation = ApiEndpointRouteBuilderExtensions.ValidateInvoiceReportZipRequest(
                zipRequest,
                out var invoiceIds,
                out var zipReportType,
                out string zipDestinationPath);
            if (zipValidation != null)
            {
                return zipValidation;
            }

            string zipTemplatePath = zipRequest.TemplatePath?.Trim() ?? string.Empty;
            bool zipWithSeal = zipRequest.WithSeal;
            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueInvoiceReportPdfZipJob(
                    jobRunner,
                    requestedBy,
                    invoiceIds,
                    zipReportType,
                    zipTemplatePath,
                    zipWithSeal,
                    zipDestinationPath));
        }

        private IResult RetryInvoiceDocumentPackageJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiEndpointRouteBuilderExtensions.ApiInvoiceDocumentPackageJobRetryRequest>(
                    sourceJob,
                    out var packageRequest,
                    out var packageError))
            {
                return packageError ?? Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            var packageValidation = ApiEndpointRouteBuilderExtensions.ValidateInvoiceDocumentPackageRequest(
                packageRequest?.InvoiceId ?? 0,
                packageRequest?.Body,
                out var packageItems,
                out bool includeMergedPdf,
                out bool createZip,
                out string packageDestinationPath);
            if (packageValidation != null)
            {
                return packageValidation;
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueInvoiceDocumentPackageJob(
                    jobRunner,
                    requestedBy,
                    packageRequest!.InvoiceId,
                    packageItems,
                    includeMergedPdf,
                    createZip,
                    packageDestinationPath));
        }

        private IResult RetryInvoiceDocumentEmailJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiEndpointRouteBuilderExtensions.ApiInvoiceDocumentEmailJobRetryRequest>(
                    sourceJob,
                    out var emailRequest,
                    out var emailError))
            {
                return emailError ?? Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            var emailValidation = ApiEndpointRouteBuilderExtensions.ValidateInvoiceDocumentEmailRequest(
                emailRequest?.InvoiceId ?? 0,
                emailRequest?.Body,
                out var emailItems,
                out bool includeMergedPdf,
                out string toAddress,
                out string subject,
                out string body);
            if (emailValidation != null)
            {
                return emailValidation;
            }

            if (string.IsNullOrWhiteSpace(emailRequest!.DeliveryId))
            {
                return Results.BadRequest(new ApiErrorResponse("邮件任务缺少投递键，不能安全重试。"));
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueInvoiceDocumentEmailJob(
                    jobRunner,
                    requestedBy,
                    emailRequest.DeliveryId,
                    emailRequest.InvoiceId,
                    emailItems,
                    includeMergedPdf,
                    toAddress,
                    subject,
                    body));
        }

        private IResult RetryPaymentVoucherPdfJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiEndpointRouteBuilderExtensions.ApiPaymentReportPdfJobRetryRequest>(
                    sourceJob,
                    out var paymentRequest,
                    out var paymentError))
            {
                return paymentError ?? Results.BadRequest(new ApiErrorResponse("任务重试描述无效。"));
            }

            var paymentValidation = ApiEndpointRouteBuilderExtensions.ValidatePaymentReportPdfRequest(
                paymentRequest?.PaymentId ?? 0,
                paymentRequest?.Body,
                out string paymentDestinationPath);
            if (paymentValidation != null)
            {
                return paymentValidation;
            }

            string paymentTemplatePath = paymentRequest!.Body.TemplatePath?.Trim() ?? string.Empty;
            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueuePaymentReportPdfJob(
                    jobRunner,
                    requestedBy,
                    paymentRequest.PaymentId,
                    paymentTemplatePath,
                    paymentDestinationPath));
        }

        private sealed class ApiReportPdfJobRetryRequest
        {
            public int InvoiceId { get; set; }

            public ApiReportPdfRequest Body { get; set; } = new();
        }
    }
}
