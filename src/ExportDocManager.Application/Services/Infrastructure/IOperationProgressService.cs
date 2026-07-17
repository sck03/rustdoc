namespace ExportDocManager.Services.Infrastructure
{
    public sealed class OperationProgressUpdate
    {
        public string StatusText { get; init; } = string.Empty;

        public string DetailText { get; init; } = string.Empty;

        public int? ProgressPercent { get; init; }
    }

    public interface IOperationProgressService
    {
        Task RunAsync(
            string title,
            string initialStatus,
            Func<IProgress<OperationProgressUpdate>, CancellationToken, Task> operation,
            bool allowCancel = true);

        Task<T> RunAsync<T>(
            string title,
            string initialStatus,
            Func<IProgress<OperationProgressUpdate>, CancellationToken, Task<T>> operation,
            bool allowCancel = true);
    }
}
