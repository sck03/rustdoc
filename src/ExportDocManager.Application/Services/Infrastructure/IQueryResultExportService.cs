using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed record QueryResultExportResult(
        int ExportedCount,
        string DestinationPath);

    public interface IQueryResultExportService
    {
        Task<QueryResultExportResult> ExportToExcelAsync(
            QueryPageQuery query,
            string filePath,
            IProgress<OperationProgressUpdate> progress = null,
            CancellationToken cancellationToken = default);

        Task<QueryResultExportBytesResult> ExportToExcelBytesAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default);
    }

    public sealed record QueryResultExportBytesResult(byte[] Content, int ExportedCount);
}
