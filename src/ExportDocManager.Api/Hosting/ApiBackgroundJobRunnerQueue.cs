using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        private BackgroundJobSnapshot CreateQueueRejectedJob(
            string jobId,
            string kind,
            string title,
            string requestedBy,
            User currentUser,
            DateTimeOffset now,
            string rejectionMessage,
            string retryOperation,
            string retryRequestJson)
        {
            return _jobs.Upsert(new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = kind,
                Title = title,
                Status = BackgroundJobStatusCatalog.Failed,
                ProgressPercent = 0,
                StatusText = ApiBackgroundJobQueueStatusCatalog.Rejected,
                DetailText = rejectionMessage,
                RequestedBy = requestedBy,
                RequestedByUserId = currentUser != null &&
                    string.Equals(currentUser.Username, requestedBy, StringComparison.OrdinalIgnoreCase)
                        ? currentUser.Id
                        : 0,
                CreatedAt = now,
                CompletedAt = now,
                ErrorMessage = rejectionMessage,
                CanCancel = false,
                CanRetry = !string.IsNullOrWhiteSpace(retryOperation) && !string.IsNullOrWhiteSpace(retryRequestJson),
                RetryOperation = retryOperation,
                RetryRequestJson = retryRequestJson
            });
        }
    }
}
