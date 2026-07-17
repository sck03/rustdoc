using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

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

        private void LoadPersistedJobs()
        {
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

        private void PersistJobs()
        {
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
