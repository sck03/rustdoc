using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public static class BackgroundJobStatusCatalog
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
        public const string Canceling = "Canceling";
        public const string Canceled = "Canceled";

        public static bool IsTerminal(string status)
        {
            return string.Equals(status, Succeeded, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class BackgroundJobQuery
    {
        public string Status { get; set; } = string.Empty;

        public string Keyword { get; set; } = string.Empty;

        public string RequestedBy { get; set; } = string.Empty;

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class BackgroundJobSnapshot
    {
        public string JobId { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Status { get; init; } = BackgroundJobStatusCatalog.Queued;

        public int? ProgressPercent { get; init; }

        public string StatusText { get; init; } = string.Empty;

        public string DetailText { get; init; } = string.Empty;

        public string RequestedBy { get; init; } = string.Empty;

        public int RequestedByUserId { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public string OutputPath { get; init; } = string.Empty;

        public string ErrorMessage { get; init; } = string.Empty;

        public bool CanCancel { get; init; }

        public bool CanRetry { get; init; }

        public string RetryOperation { get; init; } = string.Empty;

        public string RetryRequestJson { get; init; } = string.Empty;
    }

    public interface IBackgroundJobService
    {
        Task<PagedResult<BackgroundJobSnapshot>> QueryAsync(
            BackgroundJobQuery query,
            CancellationToken cancellationToken = default);

        Task<BackgroundJobSnapshot> GetAsync(
            string jobId,
            CancellationToken cancellationToken = default);

        Task<bool> RequestCancelAsync(
            string jobId,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(
            string jobId,
            CancellationToken cancellationToken = default);

        Task<int> ClearTerminalAsync(
            string requestedBy = "",
            CancellationToken cancellationToken = default);
    }
}
