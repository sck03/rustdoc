using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        private async Task RunAsync(
            BackgroundJobSnapshot initial,
            CancellationTokenSource cancellationSource,
            Func<IServiceProvider, ApiBackgroundJobExecutionContext, Task<string>> executeAsync,
            UserConcurrencyState userState)
        {
            string jobId = initial.JobId;
            var userConcurrency = userState.Gate;
            bool globalAcquired = false;
            bool userAcquired = false;
            bool browserAcquired = false;
            string producedOutputPath = string.Empty;
            try
            {
                await userConcurrency.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
                userAcquired = true;
                if (UsesBrowserCapacity(initial.Kind))
                {
                    await _browserConcurrency.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
                    browserAcquired = true;
                }
                await _globalConcurrency.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
                globalAcquired = true;

                BackgroundJobSnapshot? runningJob = _jobs.Update(jobId, current =>
                {
                    ThrowIfCancellationWon(current, cancellationSource);
                    if (BackgroundJobStatusCatalog.IsTerminal(current.Status))
                    {
                        return current;
                    }

                    return new BackgroundJobSnapshot
                    {
                        JobId = current.JobId,
                        Kind = current.Kind,
                        Title = current.Title,
                        Status = BackgroundJobStatusCatalog.Running,
                        ProgressPercent = current.ProgressPercent,
                        StatusText = "运行中",
                        DetailText = current.DetailText,
                        RequestedBy = current.RequestedBy,
                        RequestedByUserId = current.RequestedByUserId,
                        CreatedAt = current.CreatedAt,
                        StartedAt = DateTimeOffset.UtcNow,
                        OutputPath = current.OutputPath,
                        ErrorMessage = string.Empty,
                        CanCancel = true,
                        CanRetry = false,
                        RetryOperation = current.RetryOperation,
                        RetryRequestJson = current.RetryRequestJson
                    };
                });
                if (runningJob == null ||
                    !string.Equals(runningJob.Status, BackgroundJobStatusCatalog.Running, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var backgroundUser = await ResolveBackgroundUserAsync(
                        scope.ServiceProvider,
                        initial.RequestedByUserId,
                        initial.RequestedBy,
                        cancellationSource.Token)
                    .ConfigureAwait(false);
                if (initial.RequestedByUserId > 0 && backgroundUser == null)
                {
                    throw new InvalidOperationException("任务提交账号已停用或不存在，任务已阻止执行。");
                }
                using var backgroundUserScope = ApiCurrentUserContext.UseBackgroundUser(backgroundUser);
                var context = new ApiBackgroundJobExecutionContext(_jobs, initial, cancellationSource.Token);
                producedOutputPath = await executeAsync(scope.ServiceProvider, context) ?? string.Empty;

                BackgroundJobSnapshot? completedJob = _jobs.Update(jobId, current =>
                {
                    ThrowIfCancellationWon(current, cancellationSource);
                    if (BackgroundJobStatusCatalog.IsTerminal(current.Status))
                    {
                        return current;
                    }

                    return new BackgroundJobSnapshot
                    {
                        JobId = current.JobId,
                        Kind = current.Kind,
                        Title = current.Title,
                        Status = BackgroundJobStatusCatalog.Succeeded,
                        ProgressPercent = 100,
                        StatusText = "已完成",
                        DetailText = current.DetailText,
                        RequestedBy = current.RequestedBy,
                        RequestedByUserId = current.RequestedByUserId,
                        CreatedAt = current.CreatedAt,
                        StartedAt = current.StartedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        OutputPath = string.IsNullOrWhiteSpace(producedOutputPath)
                            ? current.OutputPath
                            : producedOutputPath,
                        ErrorMessage = string.Empty,
                        CanCancel = false,
                        CanRetry = false,
                        RetryOperation = current.RetryOperation,
                        RetryRequestJson = current.RetryRequestJson
                    };
                });
                if (completedJob == null ||
                    !string.Equals(completedJob.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase))
                {
                    _jobs.CleanupControlledOutputPath(producedOutputPath);
                }
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
                _jobs.CleanupControlledOutputPath(producedOutputPath);
                _jobs.CleanupControlledOutputForJob(jobId);
                _jobs.Update(jobId, current => new BackgroundJobSnapshot
                {
                    JobId = current.JobId,
                    Kind = current.Kind,
                    Title = current.Title,
                    Status = BackgroundJobStatusCatalog.Canceled,
                    ProgressPercent = current.ProgressPercent,
                    StatusText = "已取消",
                    DetailText = current.DetailText,
                    RequestedBy = current.RequestedBy,
                    RequestedByUserId = current.RequestedByUserId,
                    CreatedAt = current.CreatedAt,
                    StartedAt = current.StartedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    OutputPath = string.Empty,
                    ErrorMessage = string.Empty,
                    CanCancel = false,
                    CanRetry = HasRetryDescriptor(current),
                    RetryOperation = current.RetryOperation,
                    RetryRequestJson = current.RetryRequestJson
                });
            }
            catch (Exception ex)
            {
                _jobs.CleanupControlledOutputPath(producedOutputPath);
                _jobs.CleanupControlledOutputForJob(jobId);
                BackgroundJobSnapshot? failedJob = _jobs.Update(jobId, current =>
                {
                    bool cancellationWon = cancellationSource.IsCancellationRequested ||
                        string.Equals(
                            current.Status,
                            BackgroundJobStatusCatalog.Canceling,
                            StringComparison.OrdinalIgnoreCase);
                    return new BackgroundJobSnapshot
                    {
                        JobId = current.JobId,
                        Kind = current.Kind,
                        Title = current.Title,
                        Status = cancellationWon
                            ? BackgroundJobStatusCatalog.Canceled
                            : BackgroundJobStatusCatalog.Failed,
                        ProgressPercent = current.ProgressPercent,
                        StatusText = cancellationWon ? "已取消" : "失败",
                        DetailText = current.DetailText,
                        RequestedBy = current.RequestedBy,
                        RequestedByUserId = current.RequestedByUserId,
                        CreatedAt = current.CreatedAt,
                        StartedAt = current.StartedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        OutputPath = string.Empty,
                        ErrorMessage = cancellationWon ? string.Empty : ex.Message,
                        CanCancel = false,
                        CanRetry = HasRetryDescriptor(current),
                        RetryOperation = current.RetryOperation,
                        RetryRequestJson = current.RetryRequestJson
                    };
                });
                if (failedJob != null &&
                    string.Equals(failedJob.Status, BackgroundJobStatusCatalog.Failed, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(ex, "Background job failed. JobId={JobId}", jobId);
                }
            }
            finally
            {
                if (globalAcquired)
                {
                    _globalConcurrency.Release();
                }
                if (browserAcquired)
                {
                    _browserConcurrency.Release();
                }
                if (userAcquired)
                {
                    userConcurrency.Release();
                }
                _jobs.RemoveCancellationSource(jobId);
                ReleaseQueueSlot(initial.RequestedBy, userState);
            }
        }

        private static bool HasRetryDescriptor(BackgroundJobSnapshot job)
        {
            return !string.IsNullOrWhiteSpace(job?.RetryOperation)
                && !string.IsNullOrWhiteSpace(job.RetryRequestJson);
        }

        private static void ThrowIfCancellationWon(
            BackgroundJobSnapshot job,
            CancellationTokenSource cancellationSource)
        {
            if (string.Equals(job?.Status, BackgroundJobStatusCatalog.Canceling, StringComparison.OrdinalIgnoreCase) &&
                !cancellationSource.IsCancellationRequested)
            {
                cancellationSource.Cancel();
            }

            cancellationSource.Token.ThrowIfCancellationRequested();
        }

        private static async Task<User?> ResolveBackgroundUserAsync(
            IServiceProvider provider,
            int requestedByUserId,
            string requestedBy,
            CancellationToken cancellationToken)
        {
            requestedBy = requestedBy?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedBy))
            {
                return null;
            }

            var userService = provider.GetService<IUserService>();
            if (userService == null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (requestedByUserId > 0)
            {
                return await userService.GetActiveUserByIdAsync(requestedByUserId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await userService.GetUserByUsernameAsync(requestedBy).ConfigureAwait(false);
        }
    }
}
