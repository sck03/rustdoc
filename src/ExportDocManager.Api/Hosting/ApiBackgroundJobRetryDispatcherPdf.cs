using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRetryDispatcher
    {
        private IResult RetryPdfMergeJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiPdfMergeRequest>(sourceJob, out var pdfMergeRequest, out var pdfMergeError))
            {
                return pdfMergeError;
            }

            var pdfValidation = ApiEndpointRouteBuilderExtensions.ValidatePdfMergeRequest(
                pdfMergeRequest,
                out var sourceFiles,
                out string pdfDestinationPath);
            if (pdfValidation != null)
            {
                return pdfValidation;
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueuePdfMergeJob(
                    jobRunner,
                    requestedBy,
                    sourceFiles,
                    pdfDestinationPath));
        }
    }
}
