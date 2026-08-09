using System.Collections.Concurrent;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobService : IBackgroundJobService
    {
        private readonly ConcurrentDictionary<string, BackgroundJobSnapshot> _jobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _lastPersistedUtcTicks = new(StringComparer.OrdinalIgnoreCase);
        private readonly IAppPathProvider _pathProvider;
        private readonly ApiBackgroundJobRetentionOptions _retentionOptions;
        private readonly Lock _mutationLock = new();
        private readonly Lock _historyCleanupLock = new();

        internal int PersistThrottleEntryCount => _lastPersistedUtcTicks.Count;

        public ApiBackgroundJobService()
        {
            _retentionOptions = new ApiBackgroundJobRetentionOptions().Normalize();
        }

        public ApiBackgroundJobService(IAppPathProvider pathProvider)
        {
            _retentionOptions = new ApiBackgroundJobRetentionOptions().Normalize();
            if (pathProvider == null)
            {
                return;
            }

            _pathProvider = pathProvider;
            _storePath = Path.Combine(pathProvider.CacheRoot, "BackgroundJobs", "jobs.json");
            LoadPersistedJobs();
            PruneOrphanControlledBrowserOutputs();
        }

        public ApiBackgroundJobService(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            IDbContextFactory<AppDbContext> contextFactory)
            : this(pathProvider, databaseSettings, contextFactory, new ApiBackgroundJobRetentionOptions())
        {
        }

        public ApiBackgroundJobService(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            IDbContextFactory<AppDbContext> contextFactory,
            ApiBackgroundJobRetentionOptions retentionOptions)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _retentionOptions = (retentionOptions ?? throw new ArgumentNullException(nameof(retentionOptions))).Normalize();
            _useDatabaseStore = DatabaseModeHelper.UsesPostgreSql(
                databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings)));
            _storePath = _useDatabaseStore
                ? string.Empty
                : Path.Combine(pathProvider.CacheRoot, "BackgroundJobs", "jobs.json");
            LoadPersistedJobs();
            PruneOrphanControlledBrowserOutputs();
        }

        public Task<bool> RequestCancelAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Task.FromResult(false);
            }

            string key = jobId.Trim();
            bool accepted = false;
            lock (_mutationLock)
            {
                while (_jobs.TryGetValue(key, out var job))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!job.CanCancel || BackgroundJobStatusCatalog.IsTerminal(job.Status))
                    {
                        return Task.FromResult(false);
                    }

                // Move the job to Canceling before signaling the worker.  The
                // worker may complete immediately after observing cancellation;
                // changing the state first prevents a stale Running snapshot
                // from making an otherwise accepted request look unsuccessful.
                var next = new BackgroundJobSnapshot
                {
                    JobId = job.JobId,
                    Kind = job.Kind,
                    Title = job.Title,
                    Status = BackgroundJobStatusCatalog.Canceling,
                    ProgressPercent = job.ProgressPercent,
                    StatusText = "正在取消",
                    DetailText = job.DetailText,
                    RequestedBy = job.RequestedBy,
                    RequestedByUserId = job.RequestedByUserId,
                    CreatedAt = job.CreatedAt,
                    StartedAt = job.StartedAt,
                    CompletedAt = job.CompletedAt,
                    UpdatedAt = NextUpdatedAt(job.UpdatedAt, default),
                    OutputPath = job.OutputPath,
                    ErrorMessage = job.ErrorMessage,
                    CanCancel = false,
                    CanRetry = job.CanRetry,
                    RetryOperation = job.RetryOperation,
                    RetryRequestJson = job.RetryRequestJson
                };

                    if (!_jobs.TryUpdate(key, next, job))
                    {
                        continue;
                    }

                    try
                    {
                        PersistJob(next);
                    }
                    catch
                    {
                        _jobs.TryUpdate(key, job, next);
                        throw;
                    }

                    accepted = true;
                    break;
                }
            }

            if (accepted)
            {
                TryCancel(key);
            }

            return Task.FromResult(accepted);
        }

        public Task<bool> DeleteAsync(
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Task.FromResult(false);
            }

            string key = jobId.Trim();
            cancellationToken.ThrowIfCancellationRequested();
            BackgroundJobSnapshot removedJob;
            lock (_mutationLock)
            {
                if (!_jobs.TryGetValue(key, out var job) || !BackgroundJobStatusCatalog.IsTerminal(job.Status))
                {
                    return Task.FromResult(false);
                }

                if (!_jobs.TryRemove(key, out removedJob))
                {
                    return Task.FromResult(false);
                }

                bool hadPersistedTick = _lastPersistedUtcTicks.TryGetValue(key, out long persistedTick);
                try
                {
                    DeletePersistedJobs(new[] { key });
                    _lastPersistedUtcTicks.TryRemove(key, out _);
                }
                catch
                {
                    _jobs[key] = removedJob;
                    if (hadPersistedTick)
                    {
                        _lastPersistedUtcTicks[key] = persistedTick;
                    }
                    throw;
                }
            }

            TryDeleteControlledBrowserOutput(removedJob.OutputPath);
            return Task.FromResult(true);
        }

        public Task<int> ClearTerminalAsync(
            string requestedBy = "",
            CancellationToken cancellationToken = default)
        {
            requestedBy = requestedBy?.Trim() ?? string.Empty;
            var removedJobs = new List<BackgroundJobSnapshot>();
            var persistedTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            lock (_mutationLock)
            {
                var candidates = new List<KeyValuePair<string, BackgroundJobSnapshot>>();
                foreach (var pair in _jobs.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!BackgroundJobStatusCatalog.IsTerminal(pair.Value.Status))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(requestedBy) &&
                        !string.Equals(pair.Value.RequestedBy, requestedBy, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    candidates.Add(pair);
                }

                foreach (var pair in candidates)
                {
                    if (_jobs.TryRemove(pair.Key, out var removedJob))
                    {
                        removedJobs.Add(removedJob);
                        if (_lastPersistedUtcTicks.TryGetValue(pair.Key, out long persistedTick))
                        {
                            persistedTicks[pair.Key] = persistedTick;
                        }
                    }
                }

                if (removedJobs.Count > 0)
                {
                    try
                    {
                        DeletePersistedJobs(removedJobs.Select(job => job.JobId).ToArray());
                        foreach (var removedJob in removedJobs)
                        {
                            _lastPersistedUtcTicks.TryRemove(removedJob.JobId, out _);
                        }
                    }
                    catch
                    {
                        foreach (var removedJob in removedJobs)
                        {
                            _jobs[removedJob.JobId] = removedJob;
                            if (persistedTicks.TryGetValue(removedJob.JobId, out long persistedTick))
                            {
                                _lastPersistedUtcTicks[removedJob.JobId] = persistedTick;
                            }
                        }
                        throw;
                    }
                }
            }

            foreach (var removedJob in removedJobs)
            {
                TryDeleteControlledBrowserOutput(removedJob.OutputPath);
            }
            return Task.FromResult(removedJobs.Count);
        }

        public BackgroundJobSnapshot Upsert(BackgroundJobSnapshot job)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (string.IsNullOrWhiteSpace(job.JobId))
            {
                throw new ArgumentException("任务 ID 不能为空。", nameof(job));
            }

            lock (_mutationLock)
            {
                string key = job.JobId.Trim();
                bool hadPrevious = _jobs.TryGetValue(key, out var previous);
                BackgroundJobSnapshot normalized = _jobs.AddOrUpdate(
                    key,
                    _ => NormalizeNewJob(job, key),
                    (_, current) => Normalize(job, current));
                try
                {
                    PersistJob(normalized);
                    if (BackgroundJobStatusCatalog.IsTerminal(normalized.Status))
                    {
                        PruneTerminalHistory();
                    }

                    return normalized;
                }
                catch
                {
                    // Enqueue callers reserve queue capacity before Upsert. Restore the
                    // in-memory snapshot when durable persistence fails so a rejected
                    // write cannot leave a phantom task consuming capacity forever.
                    if (hadPrevious)
                    {
                        _jobs[key] = previous;
                    }
                    else
                    {
                        _jobs.TryRemove(key, out _);
                    }

                    try
                    {
                        if (hadPrevious)
                        {
                            PersistJob(previous);
                        }
                        else if (_useDatabaseStore)
                        {
                            DeleteDatabaseJobs(new[] { key });
                        }
                        else
                        {
                            PersistJobs();
                        }
                    }
                    catch
                    {
                        // The original persistence exception is more useful to the
                        // caller. A later retry/startup load will reconcile durable
                        // state if the storage itself remains unavailable.
                    }

                    throw;
                }
            }
        }

        public BackgroundJobSnapshot Update(
            string jobId,
            Func<BackgroundJobSnapshot, BackgroundJobSnapshot> update)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
            ArgumentNullException.ThrowIfNull(update);

            lock (_mutationLock)
            {
                string key = jobId.Trim();
                while (_jobs.TryGetValue(key, out var current))
                {
                    var next = Normalize(update(current), current);
                    if (_jobs.TryUpdate(key, next, current))
                    {
                        try
                        {
                            if (ShouldPersistUpdate(current, next))
                            {
                                PersistJob(next);
                            }
                            if (BackgroundJobStatusCatalog.IsTerminal(next.Status))
                            {
                                PruneTerminalHistory();
                            }
                            return next;
                        }
                        catch
                        {
                            _jobs.TryUpdate(key, current, next);
                            throw;
                        }
                    }
                }

                return null;
            }
        }

        public void RegisterCancellationSource(string jobId, CancellationTokenSource source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
            ArgumentNullException.ThrowIfNull(source);

            _cancellationSources[jobId.Trim()] = source;
        }

        public void RemoveCancellationSource(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return;
            }

            if (_cancellationSources.TryRemove(jobId.Trim(), out var source))
            {
                source.Dispose();
            }
        }

        private bool TryCancel(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) ||
                !_cancellationSources.TryGetValue(jobId.Trim(), out var source))
            {
                return false;
            }

            try
            {
                source.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                // Completion may remove and dispose the source immediately after
                // the lookup. A completed task is already non-cancelable; do not
                // turn that harmless race into a 500 response.
                return false;
            }
        }

        private bool ShouldPersistUpdate(BackgroundJobSnapshot current, BackgroundJobSnapshot next)
        {
            if (BackgroundJobStatusCatalog.IsTerminal(next.Status) ||
                !string.Equals(current.Status, next.Status, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.OutputPath, next.OutputPath, StringComparison.Ordinal) ||
                !string.Equals(current.ErrorMessage, next.ErrorMessage, StringComparison.Ordinal))
            {
                return true;
            }

            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            long lastTicks = _lastPersistedUtcTicks.GetOrAdd(next.JobId, 0);
            if (lastTicks != 0 && nowTicks - lastTicks < TimeSpan.FromSeconds(1).Ticks)
            {
                return false;
            }

            return true;
        }

        private void PruneTerminalHistory()
        {
            lock (_historyCleanupLock)
            {
                var terminalJobs = _jobs.Values
                    .Where(job => BackgroundJobStatusCatalog.IsTerminal(job.Status))
                    .OrderByDescending(job => job.CompletedAt ?? job.CreatedAt)
                    .ThenByDescending(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (terminalJobs.Count == 0)
                {
                    return;
                }

                DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionOptions.RetentionDays);
                var removeIds = terminalJobs
                    .Where(job => (job.CompletedAt ?? job.CreatedAt) < cutoff)
                    .Select(job => job.JobId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var userGroup in terminalJobs
                    .Where(job => !removeIds.Contains(job.JobId))
                    .GroupBy(job => string.IsNullOrWhiteSpace(job.RequestedBy) ? "__system__" : job.RequestedBy.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var overflow in userGroup.Skip(_retentionOptions.PerUserLimit))
                    {
                        removeIds.Add(overflow.JobId);
                    }
                }

                foreach (var overflow in terminalJobs
                    .Where(job => !removeIds.Contains(job.JobId))
                    .Skip(_retentionOptions.GlobalLimit))
                {
                    removeIds.Add(overflow.JobId);
                }

                if (removeIds.Count == 0)
                {
                    return;
                }

                var removedJobs = new List<BackgroundJobSnapshot>(removeIds.Count);
                var persistedTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (string jobId in removeIds)
                {
                    if (_jobs.TryRemove(jobId, out var removedJob))
                    {
                        removedJobs.Add(removedJob);
                        if (_lastPersistedUtcTicks.TryGetValue(jobId, out long persistedTick))
                        {
                            persistedTicks[jobId] = persistedTick;
                        }
                    }
                }

                try
                {
                    DeletePersistedJobs(removedJobs.Select(job => job.JobId).ToArray());
                    foreach (var removedJob in removedJobs)
                    {
                        _lastPersistedUtcTicks.TryRemove(removedJob.JobId, out _);
                    }
                }
                catch
                {
                    foreach (var removedJob in removedJobs)
                    {
                        _jobs[removedJob.JobId] = removedJob;
                        if (persistedTicks.TryGetValue(removedJob.JobId, out long persistedTick))
                        {
                            _lastPersistedUtcTicks[removedJob.JobId] = persistedTick;
                        }
                    }
                    throw;
                }

                foreach (var removedJob in removedJobs)
                {
                    TryDeleteControlledBrowserOutput(removedJob.OutputPath);
                }
            }
        }

        private static BackgroundJobSnapshot Normalize(
            BackgroundJobSnapshot job,
            BackgroundJobSnapshot fallback)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentNullException.ThrowIfNull(fallback);

            return new BackgroundJobSnapshot
            {
                JobId = string.IsNullOrWhiteSpace(job.JobId) ? fallback.JobId : job.JobId.Trim(),
                Kind = job.Kind ?? fallback.Kind ?? string.Empty,
                Title = job.Title ?? fallback.Title ?? string.Empty,
                Status = string.IsNullOrWhiteSpace(job.Status) ? fallback.Status : job.Status,
                ProgressPercent = job.ProgressPercent,
                StatusText = job.StatusText ?? string.Empty,
                DetailText = job.DetailText ?? string.Empty,
                RequestedBy = job.RequestedBy ?? fallback.RequestedBy ?? string.Empty,
                RequestedByUserId = job.RequestedByUserId > 0
                    ? job.RequestedByUserId
                    : fallback.RequestedByUserId,
                CreatedAt = job.CreatedAt == default ? fallback.CreatedAt : job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                UpdatedAt = NextUpdatedAt(fallback.UpdatedAt, job.UpdatedAt),
                OutputPath = job.OutputPath ?? string.Empty,
                ErrorMessage = job.ErrorMessage ?? string.Empty,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = CoalesceRetryValue(job.RetryOperation, fallback.RetryOperation),
                RetryRequestJson = CoalesceRetryValue(job.RetryRequestJson, fallback.RetryRequestJson)
            };
        }

        private static BackgroundJobSnapshot NormalizeNewJob(BackgroundJobSnapshot job, string jobId)
        {
            return new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = job.Kind ?? string.Empty,
                Title = job.Title ?? string.Empty,
                Status = string.IsNullOrWhiteSpace(job.Status) ? BackgroundJobStatusCatalog.Queued : job.Status,
                ProgressPercent = job.ProgressPercent,
                StatusText = job.StatusText ?? string.Empty,
                DetailText = job.DetailText ?? string.Empty,
                RequestedBy = job.RequestedBy ?? string.Empty,
                RequestedByUserId = job.RequestedByUserId,
                CreatedAt = job.CreatedAt == default ? DateTimeOffset.UtcNow : job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                UpdatedAt = NextUpdatedAt(default, job.UpdatedAt),
                OutputPath = job.OutputPath ?? string.Empty,
                ErrorMessage = job.ErrorMessage ?? string.Empty,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = job.RetryOperation ?? string.Empty,
                RetryRequestJson = job.RetryRequestJson ?? string.Empty
            };
        }

        private static DateTimeOffset NextUpdatedAt(DateTimeOffset previous, DateTimeOffset requested)
        {
            DateTimeOffset candidate = requested == default ? DateTimeOffset.UtcNow : requested;
            if (previous != default && candidate <= previous)
            {
                candidate = previous.AddTicks(1);
            }

            return candidate;
        }

        private static string CoalesceRetryValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback ?? string.Empty
                : value.Trim();
        }
    }
}
