using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobService
    {
        private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly Lock _persistenceLock = new();
        private readonly string _storePath;
        private readonly bool _useDatabaseStore;
        private readonly IDbContextFactory<AppDbContext>? _contextFactory;

        private void LoadPersistedJobs()
        {
            if (_useDatabaseStore)
            {
                try
                {
                    LoadDatabaseJobs();
                }
                catch (PostgresException exception) when (
                    string.Equals(
                        exception.SqlState,
                        PostgresErrorCodes.UndefinedTable,
                        StringComparison.Ordinal))
                {
                    // A new shared deployment intentionally starts before the first
                    // administrator login creates the schema. Keep the in-memory job
                    // catalog empty until then; every other PostgreSQL failure remains
                    // fatal so an unavailable or misconfigured database is not hidden.
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(_storePath) || !TryConfirmPersistedStorePresence(out bool storeExists) || !storeExists)
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_storePath);
                var jobs = JsonSerializer.Deserialize<List<BackgroundJobSnapshot?>>(json, PersistenceJsonOptions);
                if (jobs is null)
                {
                    // A literal JSON null is not an empty first-start store.  It
                    // means the existing history cannot be validated, so expose
                    // the failure through readiness instead of presenting a
                    // silently incomplete catalog.
                    MarkPersistenceLoadFailure("invalid-record");
                    return;
                }

                if (jobs.Count == 0)
                {
                    return;
                }

                bool needsRewrite = false;
                var restartTime = _timeProvider.GetUtcNow();
                var restoredJobs = new List<BackgroundJobSnapshot>(jobs.Count);
                var jobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BackgroundJobSnapshot? job in jobs)
                {
                    if (job is null || string.IsNullOrWhiteSpace(job.JobId))
                    {
                        MarkPersistenceLoadFailure("invalid-record");
                        return;
                    }

                    var normalized = NormalizeRestoredJob(job, restartTime, out bool changed);
                    normalized = CleanupRestoredFailedOutput(normalized, ref changed);
                    if (!jobIds.Add(normalized.JobId))
                    {
                        // Duplicate IDs would otherwise make the last JSON item
                        // overwrite an earlier one and leave the durable history
                        // ambiguous.  Refuse the whole store until it is repaired.
                        MarkPersistenceLoadFailure("invalid-record");
                        return;
                    }

                    restoredJobs.Add(normalized);
                    needsRewrite |= changed;
                }

                foreach (BackgroundJobSnapshot restoredJob in restoredJobs)
                {
                    _jobs[restoredJob.JobId] = restoredJob;
                }

                if (needsRewrite)
                {
                    PersistJobs();
                }

                PruneTerminalHistory();
            }
            catch (IOException)
            {
                MarkPersistenceLoadFailure("read-failed");
            }
            catch (UnauthorizedAccessException)
            {
                MarkPersistenceLoadFailure("access-denied");
            }
            catch (JsonException)
            {
                MarkPersistenceLoadFailure("invalid-json");
            }
            catch (InvalidDataException)
            {
                MarkPersistenceLoadFailure("invalid-record");
            }
            catch (ArgumentException)
            {
                MarkPersistenceLoadFailure("invalid-store");
            }
            catch (NotSupportedException)
            {
                MarkPersistenceLoadFailure("invalid-store");
            }
            catch (OverflowException)
            {
                MarkPersistenceLoadFailure("invalid-record");
            }
            catch (System.Security.SecurityException)
            {
                MarkPersistenceLoadFailure("access-denied");
            }
        }

        private bool TryConfirmPersistedStorePresence(out bool exists)
        {
            exists = false;
            try
            {
                FileAttributes attributes = File.GetAttributes(_storePath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    MarkPersistenceLoadFailure("unsafe-store");
                    return false;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    MarkPersistenceLoadFailure("invalid-store");
                    return false;
                }

                exists = true;
                return true;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                MarkPersistenceLoadFailure("access-denied");
                return false;
            }
            catch (IOException)
            {
                MarkPersistenceLoadFailure("read-failed");
                return false;
            }
            catch (ArgumentException)
            {
                MarkPersistenceLoadFailure("invalid-path");
                return false;
            }
            catch (NotSupportedException)
            {
                MarkPersistenceLoadFailure("invalid-path");
                return false;
            }
        }

        private void MarkPersistenceLoadFailure(string code)
        {
            _persistenceLoadFailureCode = string.IsNullOrWhiteSpace(code) ? "unavailable" : code;
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
            lock (_persistenceLock)
            {
                // Capture and write while holding the same lock.  Otherwise an
                // older dictionary snapshot can finish serialization after a
                // newer update and overwrite the newer durable state.
                var jobs = _jobs.Values
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_useDatabaseStore)
                {
                    foreach (var job in jobs)
                    {
                        PersistDatabaseJobLocked(job);
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(_storePath))
                {
                    return;
                }

                string json = JsonSerializer.Serialize(jobs, PersistenceJsonOptions);
                try
                {
                    AtomicFileHelper.WriteAllTextAtomic(_storePath, json);
                }
                catch (IOException)
                {
                    MarkPersistenceLoadFailure("write-failed");
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    MarkPersistenceLoadFailure("access-denied");
                    throw;
                }
                catch (ArgumentException)
                {
                    MarkPersistenceLoadFailure("invalid-path");
                    throw;
                }
                catch (NotSupportedException)
                {
                    MarkPersistenceLoadFailure("invalid-path");
                    throw;
                }
                catch (InvalidDataException)
                {
                    MarkPersistenceLoadFailure("unsafe-store");
                    throw;
                }
                catch (System.Security.SecurityException)
                {
                    MarkPersistenceLoadFailure("access-denied");
                    throw;
                }
                long persistedAt = _timeProvider.GetUtcNow().UtcTicks;
                foreach (var job in jobs)
                {
                    _lastPersistedUtcTicks[job.JobId] = persistedAt;
                }
            }
        }

        private void LoadDatabaseJobs()
        {
            using var context = GetContextFactory().CreateDbContext();
            var restartTime = _timeProvider.GetUtcNow();
            var changedJobs = new List<BackgroundJobSnapshot>();
            foreach (var record in context.ApiBackgroundJobs.AsNoTracking().ToList())
            {
                var normalized = NormalizeRestoredJob(ToSnapshot(record), restartTime, out bool changed);
                normalized = CleanupRestoredFailedOutput(normalized, ref changed);
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

            PruneTerminalHistory();
        }

        private void PersistDatabaseJob(BackgroundJobSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            lock (_persistenceLock)
            {
                PersistDatabaseJobLocked(snapshot);
            }
        }

        private void PersistDatabaseJobLocked(BackgroundJobSnapshot snapshot)
        {
            using var context = GetContextFactory().CreateDbContext();
            var record = context.ApiBackgroundJobs.Find(snapshot.JobId);
            if (record != null && record.UpdatedAt >= snapshot.UpdatedAt)
            {
                // Another writer has already committed a newer state.  Do not
                // let a delayed snapshot roll the job back.
                return;
            }

            if (record == null)
            {
                record = new ApiBackgroundJobRecord { JobId = snapshot.JobId };
                context.ApiBackgroundJobs.Add(record);
            }

            ApplySnapshot(record, snapshot);
            context.SaveChanges();
            _lastPersistedUtcTicks[snapshot.JobId] = _timeProvider.GetUtcNow().UtcTicks;
        }

        private void DeleteDatabaseJobs(IReadOnlyCollection<string> jobIds)
        {
            lock (_persistenceLock)
            {
                using var context = GetContextFactory().CreateDbContext();
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
            UpdatedAt = record.UpdatedAt,
            OutputPath = record.OutputPath,
            ErrorMessage = record.ErrorMessage,
            CanCancel = record.CanCancel,
            CanRetry = record.CanRetry,
            RetryOperation = record.RetryOperation,
            RetryRequestJson = record.RetryRequestJson
        };

        private void ApplySnapshot(ApiBackgroundJobRecord record, BackgroundJobSnapshot snapshot)
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
            record.UpdatedAt = snapshot.UpdatedAt == default
                ? _timeProvider.GetUtcNow()
                : snapshot.UpdatedAt;
            record.OutputPath = snapshot.OutputPath ?? string.Empty;
            record.ErrorMessage = snapshot.ErrorMessage ?? string.Empty;
            record.CanCancel = snapshot.CanCancel;
            record.CanRetry = snapshot.CanRetry;
            record.RetryOperation = snapshot.RetryOperation ?? string.Empty;
            record.RetryRequestJson = snapshot.RetryRequestJson ?? string.Empty;
        }

        private BackgroundJobSnapshot NormalizeRestoredJob(
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
                bool canRetry = normalized.CanRetry && HasRetryDescriptor(normalized);
                changed = normalized.CanCancel || canRetry != normalized.CanRetry;
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
                    UpdatedAt = changed
                        ? NextUpdatedAt(normalized.UpdatedAt, restartTime)
                        : normalized.UpdatedAt,
                    OutputPath = normalized.OutputPath,
                    ErrorMessage = normalized.ErrorMessage,
                    CanCancel = false,
                    CanRetry = canRetry,
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
                UpdatedAt = NextUpdatedAt(normalized.UpdatedAt, restartTime),
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

        private BackgroundJobSnapshot CleanupRestoredFailedOutput(BackgroundJobSnapshot job, ref bool changed)
        {
            if (string.Equals(job.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase) ||
                !BackgroundJobStatusCatalog.IsTerminal(job.Status) ||
                string.IsNullOrWhiteSpace(job.OutputPath))
            {
                return job;
            }

            TryDeleteControlledBrowserOutput(job.OutputPath);
            changed = true;
            return new BackgroundJobSnapshot
            {
                JobId = job.JobId,
                Kind = job.Kind,
                Title = job.Title,
                Status = job.Status,
                ProgressPercent = job.ProgressPercent,
                StatusText = job.StatusText,
                DetailText = job.DetailText,
                RequestedBy = job.RequestedBy,
                RequestedByUserId = job.RequestedByUserId,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                UpdatedAt = NextUpdatedAt(job.UpdatedAt, _timeProvider.GetUtcNow()),
                OutputPath = string.Empty,
                ErrorMessage = job.ErrorMessage,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = job.RetryOperation,
                RetryRequestJson = job.RetryRequestJson
            };
        }

        private IDbContextFactory<AppDbContext> GetContextFactory() =>
            _contextFactory ?? throw new InvalidOperationException("后台任务数据库存储未配置。");
    }
}
