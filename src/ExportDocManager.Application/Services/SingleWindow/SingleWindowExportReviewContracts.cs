using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowExportReviewService
    {
        Task<SingleWindowExportReview> BuildSubmitReviewAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            CancellationToken cancellationToken = default);

        Task<int> RepairGroupsAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            IReadOnlyList<string> groupKeys,
            CancellationToken cancellationToken = default);
    }
}
