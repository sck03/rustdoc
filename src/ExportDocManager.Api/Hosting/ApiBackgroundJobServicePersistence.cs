using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobService
    {
        private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _persistenceLock = new();
        private readonly string _storePath;
        private readonly bool _useDatabaseStore;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        private void LoadPersistedJobs()
        {
            if (_useDatabaseStore)
            {
                LoadDatabaseJobs();
                return;
            }

            if (string.IsNullOrWhiteSpace(_storePath) || !File.Exists(_storePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_storePath);
                var jobs = JsonSerializer.Deserialize<List<BackgroundJobSnapshot>>(json, PersistenceJsonOptions);
                if (jobs == null || jobs.Count == 0)
                {
                    return;
                }

                bool needsRewrite = false;
                var restartTime = DateTimeOffset.UtcNow;
                foreach (var job in jobs)
                {
                    if (job == null || string.IsNullOrWhiteSpace(job.JobId))
                    {
                        continue;
                    }

                    var normalized = NormalizeRestoredJob(job, restartTime, out bool changed);
                    _jobs[normalized.JobId] = normalized;
                    needsRewrite |= changed;
                }

                if (needsRewrite)
                {
                    PersistJobs();
                }
            }
            catch (IOException)
            {
                // A corrupt or locked history file must not prevent the local API from starting.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }

        private void PersistJob(BackgroundJobSnapshot job)
        {
            if (_useDatabaseStore)
            {
                PersistDatabaseJob(job);
                return;
            }

            PersistJobs();
        }

        private void DeletePersistedJobs(IReadOnlyCollection<string> jobIds)
        {
            if (jobIds == null || jobIds.Count == 0)
            {
                return;
            }

            if (_useDatabaseStore)
            {
                DeleteDatabaseJobs(jobIds);
                return;
            }

            PersistJobs();
        }

        private void PersistJobs()
        {
            if (_useDatabaseStore)
            {
                foreach (var job in _jobs.Values)
                {
                    PersistDatabaseJob(job);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(_storePath))
            {
                return;
            }

            var jobs = _jobs.Values
                .OrderByDescending(job => job.CreatedAt)
                .ThenByDescending(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string json = JsonSerializer.Serialize(jobs, PersistenceJsonOptions);

            lock (_persistenceLock)
            {
                AtomicFileHelper.WriteAllTextAtomic(_storePath, json);
            }
        }

        private void LoadDatabaseJobs()
        {
            using var context = _contextFactory.CreateDbContext();
            var restartTime = DateTimeOffset.UtcNow;
            var changedJobs = new List<BackgroundJobSnapshot>();
            foreach (var record in context.ApiBackgroundJobs.AsNoTracking().ToList())
            {
                var normalized = NormalizeRestoredJob(ToSnapshot(record), restartTime, out bool changed);
                _jobs[normalized.JobId] = normalized;
                if (changed)
                {
                    changedJobs.Add(normalized);
                }
            }

            foreach (var job in changedJobs)
            {
                PersistDatabaseJob(job);
            }
        }

        private void PersistDatabaseJob(BackgroundJobSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            lock (_persistenceLock)
            {
                using var context = _contextFactory.CreateDbContext();
                var record = context.ApiBackgroundJobs.Find(snapshot.JobId);
                if (record == null)
                {
                    record = new ApiBackgroundJobRecord { JobId = snapshot.JobId };
                    context.ApiBackgroundJobs.Add(record);
                }

                ApplySnapshot(record, snapshot);
                context.SaveChanges();
            }
        }

        private void DeleteDatabaseJobs(IReadOnlyCollection<string> jobIds)
        {
            lock (_persistenceLock)
            {
                using var context = _contextFactory.CreateDbContext();
                var records = context.ApiBackgroundJobs
                    .Where(record => jobIds.Contains(record.JobId))
                    .ToList();
                if (records.Count == 0)
                {
                    return;
                }

                context.ApiBackgroundJobs.RemoveRange(records);
                context.SaveChanges();
            }
        }

        private static BackgroundJobSnapshot ToSnapshot(ApiBackgroundJobRecord record) => new()
        {
            JobId = record.JobId,
            Kind = record.Kind,
            Title = record.Title,
            Status = record.Status,
            ProgressPercent = record.ProgressPercent,
            StatusText = record.StatusText,
            DetailText = record.DetailText,
            RequestedBy = record.RequestedBy,
            RequestedByUserId = record.RequestedByUserId,
            CreatedAt = record.CreatedAt,
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            OutputPath = record.OutputPath,
            ErrorMessage = record.ErrorMessage,
            CanCancel = record.CanCancel,
            CanRetry = record.CanRetry,
            RetryOperation = record.RetryOperation,
            RetryRequestJson = record.RetryRequestJson
        };

        private static void ApplySnapshot(ApiBackgroundJobRecord record, BackgroundJobSnapshot snapshot)
        {
            record.Kind = snapshot.Kind ?? string.Empty;
            record.Title = snapshot.Title ?? string.Empty;
            record.Status = snapshot.Status ?? string.Empty;
            record.ProgressPercent = snapshot.ProgressPercent;
            record.StatusText = snapshot.StatusText ?? string.Empty;
            record.DetailText = snapshot.DetailText ?? string.Empty;
            record.RequestedBy = snapshot.RequestedBy ?? string.Empty;
            record.RequestedByUserId = snapshot.RequestedByUserId;
            record.CreatedAt = snapshot.CreatedAt;
            record.StartedAt = snapshot.StartedAt;
            record.CompletedAt = snapshot.CompletedAt;
            record.OutputPath = snapshot.OutputPath ?? string.Empty;
            record.ErrorMessage = snapshot.ErrorMessage ?? string.Empty;
            record.CanCancel = snapshot.CanCancel;
            record.CanRetry = snapshot.CanRetry;
            record.RetryOperation = snapshot.RetryOperation ?? string.Empty;
            record.RetryRequestJson = snapshot.RetryRequestJson ?? string.Empty;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private static BackgroundJobSnapshot NormalizeRestoredJob(
            BackgroundJobSnapshot job,
            DateTimeOffset restartTime,
            out bool changed)
        {
            var normalized = Normalize(
                job,
                new BackgroundJobSnapshot
                {
                    JobId = job.JobId,
                    CreatedAt = job.CreatedAt == default ? restartTime : job.CreatedAt
                });

            bool isTerminal = BackgroundJobStatusCatalog.IsTerminal(normalized.Status);
            if (isTerminal)
            {
                changed = normalized.CanCancel;
                return new BackgroundJobSnapshot
                {
                    JobId = normalized.JobId,
                    Kind = normalized.Kind,
                    Title = normalized.Title,
                    Status = normalized.Status,
                    ProgressPercent = normalized.ProgressPercent,
                    StatusText = normalized.StatusText,
                    DetailText = normalized.DetailText,
                    RequestedBy = normalized.RequestedBy,
                    RequestedByUserId = normalized.RequestedByUserId,
                    CreatedAt = normalized.CreatedAt,
                    StartedAt = normalized.StartedAt,
                    CompletedAt = normalized.CompletedAt,
                    OutputPath = normalized.OutputPath,
                    ErrorMessage = normalized.ErrorMessage,
                    CanCancel = false,
                    CanRetry = normalized.CanRetry && HasRetryDescriptor(normalized),
                    RetryOperation = normalized.RetryOperation,
                    RetryRequestJson = normalized.RetryRequestJson
                };
            }

            changed = true;
            return new BackgroundJobSnapshot
            {
                JobId = normalized.JobId,
                Kind = normalized.Kind,
                Title = normalized.Title,
                Status = BackgroundJobStatusCatalog.Failed,
                ProgressPercent = normalized.ProgressPercent,
                StatusText = "未完成",
                DetailText = string.IsNullOrWhiteSpace(normalized.DetailText)
                    ? "API sidecar 重启前任务未正常结束。"
                    : normalized.DetailText,
                RequestedBy = normalized.RequestedBy,
                RequestedByUserId = normalized.RequestedByUserId,
                CreatedAt = normalized.CreatedAt,
                StartedAt = normalized.StartedAt,
                CompletedAt = restartTime,
                OutputPath = normalized.OutputPath,
                ErrorMessage = "API sidecar 重启前任务未正常结束，请重新提交任务。",
                CanCancel = false,
                CanRetry = HasRetryDescriptor(normalized),
                RetryOperation = normalized.RetryOperation,
                RetryRequestJson = normalized.RetryRequestJson
            };
        }

        private static bool HasRetryDescriptor(BackgroundJobSnapshot job)
        {
            return !string.IsNullOrWhiteSpace(job?.RetryOperation)
                && !string.IsNullOrWhiteSpace(job.RetryRequestJson);
        }
    }
}
