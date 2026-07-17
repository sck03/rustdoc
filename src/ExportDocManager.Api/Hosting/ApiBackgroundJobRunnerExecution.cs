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
            Func<IServiceProvider, ApiBackgroundJobExecutionContext, Task<string>> executeAsync)
        {
            string jobId = initial.JobId;
            try
            {
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
                        initial.RequestedBy,
                        cancellationSource.Token)
                    .ConfigureAwait(false);
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
                _jobs.RemoveCancellationSource(jobId);
            }
        }

        private static bool HasRetryDescriptor(BackgroundJobSnapshot job)
        {
            return !string.IsNullOrWhiteSpace(job?.RetryOperation)
                && !string.IsNullOrWhiteSpace(job.RetryRequestJson);
        }

        private static async Task<User> ResolveBackgroundUserAsync(
            IServiceProvider provider,
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
            return await userService.GetUserByUsernameAsync(requestedBy).ConfigureAwait(false);
        }
    }
}
