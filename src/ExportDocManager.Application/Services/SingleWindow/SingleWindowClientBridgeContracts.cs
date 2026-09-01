using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowStationIdentityService
    {
        Task<string> GetCurrentStationKeyAsync(CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowClientProfileService
    {
        Task<IReadOnlyList<SwClientProfile>> ListAsync(CancellationToken cancellationToken = default);

        Task<SwClientProfile> GetActiveAsync(CancellationToken cancellationToken = default);

        Task<int> SaveAsync(
            SingleWindowClientProfileUpdate update,
            CancellationToken cancellationToken = default);

        Task ActivateAsync(
            string profileKey,
            CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowClientBridge
    {
        Task<SingleWindowClientDispatchResult> DispatchBatchToImportRootAsync(
            int batchId,
            CancellationToken cancellationToken = default);

        Task<SingleWindowReceiptCollectionResult> CollectReceiptFilesAsync(
            int batchId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reconciles dispatch leases left by a crashed desktop process.  The method is
        /// idempotent and returns the number of batches whose state changed.
        /// </summary>
        Task<int> RecoverExpiredDispatchesAsync(CancellationToken cancellationToken = default);
    }
}
