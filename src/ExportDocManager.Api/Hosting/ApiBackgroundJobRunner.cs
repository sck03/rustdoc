using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using System.Collections.Concurrent;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        private readonly ApiBackgroundJobService _jobs;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApiBackgroundJobRunner> _logger;
        private readonly ICurrentUserContext _currentUserContext;
        private readonly SemaphoreSlim _globalConcurrency;
        private readonly SemaphoreSlim _browserConcurrency;
        private readonly int _perUserConcurrencyLimit;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _userConcurrency =
            new(StringComparer.OrdinalIgnoreCase);

        public ApiBackgroundJobRunner(
            ApiBackgroundJobService jobs,
            IServiceScopeFactory scopeFactory,
            ILogger<ApiBackgroundJobRunner> logger)
            : this(jobs, scopeFactory, logger, null, new ApiBackgroundJobConcurrencyOptions())
        {
        }

        public ApiBackgroundJobRunner(
            ApiBackgroundJobService jobs,
            IServiceScopeFactory scopeFactory,
            ILogger<ApiBackgroundJobRunner> logger,
            ApiBackgroundJobConcurrencyOptions concurrencyOptions)
            : this(jobs, scopeFactory, logger, null, concurrencyOptions)
        {
        }

        public ApiBackgroundJobRunner(
            ApiBackgroundJobService jobs,
            IServiceScopeFactory scopeFactory,
            ILogger<ApiBackgroundJobRunner> logger,
            ICurrentUserContext currentUserContext,
            ApiBackgroundJobConcurrencyOptions concurrencyOptions)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _currentUserContext = currentUserContext;
            var normalizedOptions = (concurrencyOptions ?? throw new ArgumentNullException(nameof(concurrencyOptions)))
                .Normalize();
            _globalConcurrency = new SemaphoreSlim(normalizedOptions.GlobalLimit, normalizedOptions.GlobalLimit);
            _browserConcurrency = new SemaphoreSlim(normalizedOptions.BrowserLimit, normalizedOptions.BrowserLimit);
            _perUserConcurrencyLimit = normalizedOptions.PerUserLimit;
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
            var currentUser = _currentUserContext?.CurrentUser;
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
                RequestedByUserId = currentUser != null &&
                    string.Equals(currentUser.Username, requestedBy, StringComparison.OrdinalIgnoreCase)
                        ? currentUser.Id
                        : 0,
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

        private SemaphoreSlim GetUserConcurrency(string requestedBy)
        {
            string key = string.IsNullOrWhiteSpace(requestedBy) ? "__system__" : requestedBy.Trim();
            return _userConcurrency.GetOrAdd(
                key,
                _ => new SemaphoreSlim(_perUserConcurrencyLimit, _perUserConcurrencyLimit));
        }

        private static bool UsesBrowserCapacity(string kind)
        {
            string normalized = kind?.Trim() ?? string.Empty;
            return normalized.Contains("Pdf", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("ReportDocument", StringComparison.OrdinalIgnoreCase);
        }
    }

}
