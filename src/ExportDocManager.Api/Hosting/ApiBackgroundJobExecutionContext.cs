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
            _jobs.Update(JobId, current => new BackgroundJobSnapshot
            {
                JobId = current.JobId,
                Kind = current.Kind,
                Title = current.Title,
                Status = string.Equals(current.Status, BackgroundJobStatusCatalog.Canceling, StringComparison.OrdinalIgnoreCase)
                    ? BackgroundJobStatusCatalog.Canceling
                    : BackgroundJobStatusCatalog.Running,
                ProgressPercent = progressPercent ?? current.ProgressPercent,
                StatusText = statusText ?? current.StatusText,
                DetailText = detailText ?? current.DetailText,
                RequestedBy = current.RequestedBy,
                CreatedAt = current.CreatedAt,
                StartedAt = current.StartedAt,
                OutputPath = string.IsNullOrWhiteSpace(outputPath) ? current.OutputPath : outputPath,
                ErrorMessage = current.ErrorMessage,
                CanCancel = current.CanCancel,
                CanRetry = false,
                RetryOperation = current.RetryOperation,
                RetryRequestJson = current.RetryRequestJson
            });
        }
    }
}
