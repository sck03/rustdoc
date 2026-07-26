using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiBackgroundJobExecutionContext
    {
        private readonly ApiBackgroundJobService _jobs;
        private readonly BackgroundJobSnapshot _initial;

        public ApiBackgroundJobExecutionContext(
            ApiBackgroundJobService jobs,
            BackgroundJobSnapshot initial,
            CancellationToken cancellationToken)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            CancellationToken = cancellationToken;
        }

        public string JobId => _initial.JobId;

        public CancellationToken CancellationToken { get; }

        public void Report(
            int? progressPercent,
            string statusText,
            string detailText = "",
            string outputPath = "")
        {
            _jobs.Update(JobId, current =>
            {
                if (BackgroundJobStatusCatalog.IsTerminal(current.Status) ||
                    string.Equals(current.Status, BackgroundJobStatusCatalog.Canceling, StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }

                return new BackgroundJobSnapshot
                {
                    JobId = current.JobId,
                    Kind = current.Kind,
                    Title = current.Title,
                    Status = BackgroundJobStatusCatalog.Running,
                    ProgressPercent = progressPercent ?? current.ProgressPercent,
                    StatusText = statusText ?? current.StatusText,
                    DetailText = detailText ?? current.DetailText,
                    RequestedBy = current.RequestedBy,
                    RequestedByUserId = current.RequestedByUserId,
                    CreatedAt = current.CreatedAt,
                    StartedAt = current.StartedAt,
                    CompletedAt = current.CompletedAt,
                    OutputPath = string.IsNullOrWhiteSpace(outputPath) ? current.OutputPath : outputPath,
                    ErrorMessage = current.ErrorMessage,
                    CanCancel = current.CanCancel,
                    CanRetry = false,
                    RetryOperation = current.RetryOperation,
                    RetryRequestJson = current.RetryRequestJson
                };
            });
        }
    }
}
