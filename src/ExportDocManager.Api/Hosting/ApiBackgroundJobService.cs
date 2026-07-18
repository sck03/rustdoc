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
        private readonly IAppPathProvider _pathProvider;

        public ApiBackgroundJobService()
        {
        }

        public ApiBackgroundJobService(IAppPathProvider pathProvider)
        {
            if (pathProvider == null)
            {
                return;
            }

            _pathProvider = pathProvider;
            _storePath = Path.Combine(pathProvider.CacheRoot, "BackgroundJobs", "jobs.json");
            LoadPersistedJobs();
        }

        public ApiBackgroundJobService(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            IDbContextFactory<AppDbContext> contextFactory)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _useDatabaseStore = DatabaseModeHelper.UsesPostgreSql(
                databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings)));
            _storePath = _useDatabaseStore
                ? string.Empty
                : Path.Combine(pathProvider.CacheRoot, "BackgroundJobs", "jobs.json");
            LoadPersistedJobs();
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
            if (!_jobs.TryGetValue(key, out var job) || !job.CanCancel || BackgroundJobStatusCatalog.IsTerminal(job.Status))
            {
                return Task.FromResult(false);
            }

            if (_cancellationSources.TryGetValue(key, out var source))
            {
                source.Cancel();
            }

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
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                OutputPath = job.OutputPath,
                ErrorMessage = job.ErrorMessage,
                CanCancel = false,
                CanRetry = job.CanRetry,
                RetryOperation = job.RetryOperation,
                RetryRequestJson = job.RetryRequestJson
            };

            bool updated = _jobs.TryUpdate(key, next, job);
            if (updated)
            {
                PersistJob(next);
            }

            return Task.FromResult(updated);
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
            if (!_jobs.TryGetValue(key, out var job) || !BackgroundJobStatusCatalog.IsTerminal(job.Status))
            {
                return Task.FromResult(false);
            }

            bool removed = _jobs.TryRemove(key, out var removedJob);
            if (removed)
            {
                TryDeleteControlledBrowserOutput(removedJob?.OutputPath);
                DeletePersistedJobs(new[] { key });
            }

            return Task.FromResult(removed);
        }

        public Task<int> ClearTerminalAsync(
            string requestedBy = "",
            CancellationToken cancellationToken = default)
        {
            requestedBy = requestedBy?.Trim() ?? string.Empty;
            int removedCount = 0;
            var removedJobIds = new List<string>();
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

                if (_jobs.TryRemove(pair.Key, out var removedJob))
                {
                    TryDeleteControlledBrowserOutput(removedJob?.OutputPath);
                    removedJobIds.Add(pair.Key);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                DeletePersistedJobs(removedJobIds);
            }

            return Task.FromResult(removedCount);
        }

        private void TryDeleteControlledBrowserOutput(string outputPath)
        {
            if (_pathProvider == null || string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string root = Path.GetFullPath(Path.Combine(_pathProvider.ExportRoot, "Browser"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Cleanup is best effort; job history deletion must not fail because a file is locked.
            }
        }

        public BackgroundJobSnapshot Upsert(BackgroundJobSnapshot job)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (string.IsNullOrWhiteSpace(job.JobId))
            {
                throw new ArgumentException("任务 ID 不能为空。", nameof(job));
            }

            string key = job.JobId.Trim();
            var normalized = new BackgroundJobSnapshot
            {
                JobId = key,
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
                OutputPath = job.OutputPath ?? string.Empty,
                ErrorMessage = job.ErrorMessage ?? string.Empty,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = job.RetryOperation ?? string.Empty,
                RetryRequestJson = job.RetryRequestJson ?? string.Empty
            };

            _jobs.AddOrUpdate(key, normalized, (_, _) => normalized);
            PersistJob(normalized);
            return normalized;
        }

        public BackgroundJobSnapshot Update(
            string jobId,
            Func<BackgroundJobSnapshot, BackgroundJobSnapshot> update)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
            ArgumentNullException.ThrowIfNull(update);

            string key = jobId.Trim();
            while (_jobs.TryGetValue(key, out var current))
            {
                var next = Normalize(update(current), current);
                if (_jobs.TryUpdate(key, next, current))
                {
                    PersistJob(next);
                    return next;
                }
            }

            return null;
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
                OutputPath = job.OutputPath ?? string.Empty,
                ErrorMessage = job.ErrorMessage ?? string.Empty,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = CoalesceRetryValue(job.RetryOperation, fallback.RetryOperation),
                RetryRequestJson = CoalesceRetryValue(job.RetryRequestJson, fallback.RetryRequestJson)
            };
        }

        private static string CoalesceRetryValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback ?? string.Empty
                : value.Trim();
        }
    }
}
