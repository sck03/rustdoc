using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowTrackingService
    {
        Task<int> ResolveNextSubmissionVersionAsync(
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            CancellationToken cancellationToken = default);

        Task<int> RecordSubmitPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken = default);

        Task<int> RecordSubmitPackageImportAsync(
            string packagePath,
            SingleWindowImportedPackage imported,
            CancellationToken cancellationToken = default);

        Task<int> RecordReceiptPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken = default);

        Task<SingleWindowTrackingImportResult> RecordReceiptPackageImportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowOperationCenterService
    {
        Task<SingleWindowOperationCenterPageResult> QueryPageAsync(
            SingleWindowOperationCenterPageQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SingleWindowOperationCenterRow>> QueryAsync(
            SingleWindowOperationCenterQuery query,
            CancellationToken cancellationToken = default);

        Task<SingleWindowOperationCenterDetail> GetDetailAsync(
            int batchId,
            CancellationToken cancellationToken = default);
    }
}
