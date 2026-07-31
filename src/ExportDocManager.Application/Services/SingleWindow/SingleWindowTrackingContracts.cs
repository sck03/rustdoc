using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowTrackingService
    {
        Task<SingleWindowSubmissionReservation> ReserveSubmissionAsync(
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            string sourceDocumentType,
            int draftRevision,
            string sourceBaselineHash,
            string invoiceNo,
            string contractNo,
            string companyScope,
            CancellationToken cancellationToken = default);

        Task MarkSubmissionReservationFailedAsync(
            int batchId,
            string errorMessage,
            CancellationToken cancellationToken = default);

        Task<SingleWindowPackageBinding> ResolveReceiptPackageBindingAsync(
            SingleWindowBusinessType businessType,
            string batchReference,
            string invoiceNo,
            CancellationToken cancellationToken = default);

        Task<int> RecordSubmitPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            string authenticationSecret,
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
