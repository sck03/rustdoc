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

                _jobs.Update(jobId, current => new BackgroundJobSnapshot
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
                });

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
                string outputPath = await executeAsync(scope.ServiceProvider, context);

                cancellationSource.Token.ThrowIfCancellationRequested();
                _jobs.Update(jobId, current => new BackgroundJobSnapshot
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
                    OutputPath = outputPath ?? current.OutputPath,
                    ErrorMessage = string.Empty,
                    CanCancel = false,
                    CanRetry = false,
                    RetryOperation = current.RetryOperation,
                    RetryRequestJson = current.RetryRequestJson
                });
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
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
                    OutputPath = current.OutputPath,
                    ErrorMessage = string.Empty,
                    CanCancel = false,
                    CanRetry = HasRetryDescriptor(current),
                    RetryOperation = current.RetryOperation,
                    RetryRequestJson = current.RetryRequestJson
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job failed. JobId={JobId}", jobId);
                _jobs.Update(jobId, current => new BackgroundJobSnapshot
                {
                    JobId = current.JobId,
                    Kind = current.Kind,
                    Title = current.Title,
                    Status = BackgroundJobStatusCatalog.Failed,
                    ProgressPercent = current.ProgressPercent,
                    StatusText = "失败",
                    DetailText = current.DetailText,
                    RequestedBy = current.RequestedBy,
                    RequestedByUserId = current.RequestedByUserId,
                    CreatedAt = current.CreatedAt,
                    StartedAt = current.StartedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    OutputPath = current.OutputPath,
                    ErrorMessage = ex.Message,
                    CanCancel = false,
                    CanRetry = HasRetryDescriptor(current),
                    RetryOperation = current.RetryOperation,
                    RetryRequestJson = current.RetryRequestJson
                });
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

        private static async Task<User> ResolveBackgroundUserAsync(
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
