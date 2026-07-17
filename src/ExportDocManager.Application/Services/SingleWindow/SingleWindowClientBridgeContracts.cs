using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowClientProfileService
    {
        Task<SwClientProfile> GetDefaultAsync(CancellationToken cancellationToken = default);

        Task<int> SaveDefaultAsync(
            string importRootPath,
            string receiptRootPath = "",
            SingleWindowBusinessType? businessType = null,
            CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowClientBridge
    {
        Task<SingleWindowClientDispatchResult> DispatchBatchToImportRootAsync(
            int batchId,
            string importRootPath,
            string profileName = "",
            CancellationToken cancellationToken = default);

        Task<SingleWindowReceiptCollectionResult> CollectReceiptFilesAsync(
            int batchId,
            string receiptRootPath,
            CancellationToken cancellationToken = default);
    }
}
