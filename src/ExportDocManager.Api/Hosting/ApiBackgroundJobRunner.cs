using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        private readonly ApiBackgroundJobService _jobs;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApiBackgroundJobRunner> _logger;

        public ApiBackgroundJobRunner(
            ApiBackgroundJobService jobs,
            IServiceScopeFactory scopeFactory,
            ILogger<ApiBackgroundJobRunner> logger)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public BackgroundJobSnapshot Enqueue(
            string kind,
            string title,
            string requestedBy,
            Func<IServiceProvider, ApiBackgroundJobExecutionContext, Task<string>> executeAsync,
            string retryOperation = "",
            string retryRequestJson = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentNullException.ThrowIfNull(executeAsync);

            string normalizedKind = kind.Trim();
            string normalizedRetryOperation = retryOperation?.Trim() ?? string.Empty;
            string normalizedRetryRequestJson = retryRequestJson?.Trim() ?? string.Empty;
            string jobId = $"{normalizedKind.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var now = DateTimeOffset.UtcNow;
            var snapshot = _jobs.Upsert(new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = normalizedKind,
                Title = title.Trim(),
                Status = BackgroundJobStatusCatalog.Queued,
                ProgressPercent = 0,
                StatusText = "排队中",
                DetailText = string.Empty,
                RequestedBy = requestedBy ?? string.Empty,
                CreatedAt = now,
                CanCancel = true,
                CanRetry = false,
                RetryOperation = normalizedRetryOperation,
                RetryRequestJson = normalizedRetryRequestJson
            });

            var cancellationSource = new CancellationTokenSource();
            _jobs.RegisterCancellationSource(jobId, cancellationSource);
            _ = Task.Run(() => RunAsync(snapshot, cancellationSource, executeAsync));

            return snapshot;
        }
    }

}
