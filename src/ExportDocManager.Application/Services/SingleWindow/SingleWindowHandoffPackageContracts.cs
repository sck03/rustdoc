using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowHandoffPackageService
    {
        Task<SingleWindowHandoffPackageResult> ExportSubmitPackageAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            string savePath,
            CancellationToken cancellationToken = default);

        Task<SingleWindowImportedPackage> ImportSubmitPackageAsync(
            string packagePath,
            string workingDirectory = "",
            CancellationToken cancellationToken = default);

        Task<SingleWindowHandoffPackageResult> ExportReceiptPackageAsync(
            SingleWindowBusinessType businessType,
            string batchReference,
            string invoiceNo,
            IReadOnlyList<string> receiptFiles,
            string savePath,
            CancellationToken cancellationToken = default);

        Task<SingleWindowImportedPackage> ImportReceiptPackageAsync(
            string packagePath,
            string workingDirectory = "",
            CancellationToken cancellationToken = default);
    }
}
