using System.Text.Json;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRetryDispatcher
    {
        private static readonly JsonSerializerOptions RetryJsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ApiBackgroundJobRunner jobRunner;

        public ApiBackgroundJobRetryDispatcher(ApiBackgroundJobRunner jobRunner)
        {
            this.jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        }

        public async Task<IResult> RetryAsync(
            BackgroundJobSnapshot sourceJob,
            string requestedBy,
            IInvoiceService invoiceService,
            CancellationToken cancellationToken)
        {
            if (!CanRetry(sourceJob))
            {
                return Results.Json(
                    new ApiErrorResponse("任务不存在重试描述，无法重试。"),
                    statusCode: StatusCodes.Status409Conflict);
            }

            string operation = NormalizeOperation(sourceJob.RetryOperation);
            switch (operation)
            {
                case "StartPdfMergeJob":
                    return RetryPdfMergeJob(sourceJob, requestedBy);

                case "StartExcelTemplateExportJob":
                    return RetryExcelTemplateExportJob(sourceJob, requestedBy);

                case "StartBlankBookingSheetExportJob":
                    return RetryBlankBookingSheetExportJob(sourceJob, requestedBy);

                case "StartBookingSheetConvertJob":
                    return RetryBookingSheetConvertJob(sourceJob, requestedBy);

                case "StartInvoiceBookingSheetExportJob":
                    return await RetryInvoiceBookingSheetExportJobAsync(sourceJob, requestedBy, invoiceService);

                case "StartInvoiceReportPdfJob":
                    return RetryInvoiceReportPdfJob(sourceJob, requestedBy);

                case "StartPaymentVoucherPdfJob":
                    return RetryPaymentVoucherPdfJob(sourceJob, requestedBy);

                case "StartInvoiceReportPdfZipJob":
                    return RetryInvoiceReportPdfZipJob(sourceJob, requestedBy);

                case "StartInvoiceDocumentPackageJob":
                    return RetryInvoiceDocumentPackageJob(sourceJob, requestedBy);

                case "StartInvoiceDocumentEmailJob":
                    return RetryInvoiceDocumentEmailJob(sourceJob, requestedBy);

                default:
                    return Results.Json(
                        new ApiErrorResponse("任务重试操作不受支持，请重新提交任务。"),
                        statusCode: StatusCodes.Status409Conflict);
            }
        }

        private static string NormalizeOperation(string operation)
        {
            string normalized = operation?.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(normalized)
                ? string.Empty
                : char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        private static bool CanRetry(BackgroundJobSnapshot job)
        {
            return job != null
                && job.CanRetry
                && !string.IsNullOrWhiteSpace(job.RetryOperation)
                && !string.IsNullOrWhiteSpace(job.RetryRequestJson);
        }

        private static bool TryDeserializeRetryRequest<TRequest>(
            BackgroundJobSnapshot job,
            out TRequest request,
            out IResult error)
            where TRequest : class
        {
            request = null;
            error = null;

            try
            {
                request = JsonSerializer.Deserialize<TRequest>(
                    job.RetryRequestJson,
                    RetryJsonOptions);
            }
            catch (JsonException)
            {
            }

            if (request != null)
            {
                return true;
            }

            error = Results.Json(
                new ApiErrorResponse("任务重试描述无效，请重新提交任务。"),
                statusCode: StatusCodes.Status409Conflict);
            return false;
        }
    }
}
