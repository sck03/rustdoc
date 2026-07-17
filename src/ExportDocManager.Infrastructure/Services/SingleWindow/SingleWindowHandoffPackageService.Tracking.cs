using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private sealed record PackageImportTrackingResult(
            int? BatchId,
            string Status,
            int SavedReceiptCount);

        private async Task<int> ResolveNextSubmissionVersionAsync(
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _singleWindowTrackingService.ResolveNextSubmissionVersionAsync(
                    businessType,
                    sourceInvoiceId,
                    sourceDocumentId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Resolving next submission version failed for invoice {InvoiceId} and business {BusinessType}", sourceInvoiceId, businessType);
                return 1;
            }
        }

        private async Task<int?> TryRecordSubmitPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _singleWindowTrackingService.RecordSubmitPackageExportAsync(
                    packagePath,
                    manifest,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Persisting submit package export failed for {PackagePath}", packagePath);
                return null;
            }
        }

        private async Task<int?> TryRecordReceiptPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _singleWindowTrackingService.RecordReceiptPackageExportAsync(
                    packagePath,
                    manifest,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Persisting receipt package export failed for {PackagePath}", packagePath);
                return null;
            }
        }

        private async Task<PackageImportTrackingResult> TryRecordPackageImportAsync(
            string packagePath,
            string workingDirectory,
            SingleWindowPackageManifest manifest,
            IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken)
        {
            try
            {
                if (manifest.PackageType == SingleWindowPackageType.SubmitPackage)
                {
                    int? batchId = await _singleWindowTrackingService.RecordSubmitPackageImportAsync(
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
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Persisting single-window package tracking failed for {PackagePath}", packagePath);
                return new PackageImportTrackingResult(null, string.Empty, 0);
            }
        }
    }
}
