using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private sealed record PackageImportTrackingResult(
            int? BatchId,
            string Status,
            int SavedReceiptCount);

        private async Task<PackageImportTrackingResult> TryRecordPackageImportAsync(
            string packagePath,
            string workingDirectory,
            SingleWindowPackageManifest manifest,
            IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken)
        {
            if (manifest.PackageType == SingleWindowPackageType.SubmitPackage)
            {
                int batchId = await _singleWindowTrackingService.RecordSubmitPackageImportAsync(
                    packagePath,
                    new SingleWindowImportedPackage
                    {
                        WorkingDirectory = workingDirectory,
                        Manifest = manifest,
                        ParsedReceipts = []
                    },
                    cancellationToken);
                return new PackageImportTrackingResult(batchId, string.Empty, 0);
            }

            var trackingResult = await _singleWindowTrackingService.RecordReceiptPackageImportAsync(
                packagePath,
                manifest,
                receiptEntries,
                cancellationToken);
            return new PackageImportTrackingResult(
                trackingResult.BatchId,
                trackingResult.Status,
                trackingResult.SavedReceiptCount);
        }
    }
}
