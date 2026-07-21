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
        private readonly int _globalQueueLimit;
        private readonly int _perUserQueueLimit;
        private readonly ConcurrentDictionary<string, UserConcurrencyState> _userConcurrency =
            new(StringComparer.OrdinalIgnoreCase);
        private int _reservedJobs;

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
            _globalQueueLimit = normalizedOptions.GlobalQueueLimit;
            _perUserQueueLimit = normalizedOptions.PerUserQueueLimit;
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
            string normalizedRequestedBy = requestedBy?.Trim() ?? string.Empty;
            if (!TryReserveQueueSlot(normalizedRequestedBy, out var userState, out string rejectionMessage))
            {
                return CreateQueueRejectedJob(
                    jobId,
                    normalizedKind,
                    title.Trim(),
                    normalizedRequestedBy,
                    currentUser,
                    now,
                    rejectionMessage,
                    normalizedRetryOperation,
                    normalizedRetryRequestJson);
            }

            var snapshot = _jobs.Upsert(new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = normalizedKind,
                Title = title.Trim(),
                Status = BackgroundJobStatusCatalog.Queued,
                ProgressPercent = 0,
                StatusText = "排队中",
                DetailText = string.Empty,
                RequestedBy = normalizedRequestedBy,
                RequestedByUserId = currentUser != null &&
                    string.Equals(currentUser.Username, normalizedRequestedBy, StringComparison.OrdinalIgnoreCase)
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
            _ = Task.Run(() => RunAsync(snapshot, cancellationSource, executeAsync, userState));

            return snapshot;
        }

        private bool TryReserveQueueSlot(
            string requestedBy,
            out UserConcurrencyState userState,
            out string rejectionMessage)
        {
            string key = string.IsNullOrWhiteSpace(requestedBy) ? "__system__" : requestedBy.Trim();
            int globalCount = Interlocked.Increment(ref _reservedJobs);
            if (globalCount > _globalQueueLimit)
            {
                Interlocked.Decrement(ref _reservedJobs);
                userState = null;
                rejectionMessage = $"系统任务队列已达到 {_globalQueueLimit} 条上限，请等待现有任务完成后重试。";
                return false;
            }

            while (true)
            {
                userState = _userConcurrency.GetOrAdd(
                    key,
                    static (_, limit) => new UserConcurrencyState(limit),
                    _perUserConcurrencyLimit);
                lock (userState.SyncRoot)
                {
                    if (!_userConcurrency.TryGetValue(key, out var current) || !ReferenceEquals(current, userState))
                    {
                        continue;
                    }

                    if (userState.ReservedJobs >= _perUserQueueLimit)
                    {
                        Interlocked.Decrement(ref _reservedJobs);
                        rejectionMessage = $"当前用户任务队列已达到 {_perUserQueueLimit} 条上限，请等待现有任务完成后重试。";
                        return false;
                    }

                    userState.ReservedJobs++;
                    rejectionMessage = string.Empty;
                    return true;
                }
            }
        }

        private void ReleaseQueueSlot(string requestedBy, UserConcurrencyState userState)
        {
            Interlocked.Decrement(ref _reservedJobs);
            if (userState == null)
            {
                return;
            }

            string key = string.IsNullOrWhiteSpace(requestedBy) ? "__system__" : requestedBy.Trim();
            lock (userState.SyncRoot)
            {
                userState.ReservedJobs = Math.Max(0, userState.ReservedJobs - 1);
                if (userState.ReservedJobs == 0)
                {
                    ((ICollection<KeyValuePair<string, UserConcurrencyState>>)_userConcurrency)
                        .Remove(new KeyValuePair<string, UserConcurrencyState>(key, userState));
                    userState.Gate.Dispose();
                }
            }
        }

        private static bool UsesBrowserCapacity(string kind)
        {
            string normalized = kind?.Trim() ?? string.Empty;
            return normalized.Contains("Pdf", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("ReportDocument", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class UserConcurrencyState
        {
            public UserConcurrencyState(int concurrencyLimit)
            {
                Gate = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
            }

            public object SyncRoot { get; } = new();

            public SemaphoreSlim Gate { get; }

            public int ReservedJobs { get; set; }
        }
    }

}
