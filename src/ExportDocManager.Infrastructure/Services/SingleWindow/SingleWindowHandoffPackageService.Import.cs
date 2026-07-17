using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private async Task<SingleWindowImportedPackage> ImportPackageAsync(
            string packagePath,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("交接包不存在。", packagePath);
            }

            string targetDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? RuntimeCachePathHelper.CreateUniqueDirectory(
                    _pathProvider,
                    "SingleWindowPackages",
                    "sw-import")
                : Path.Combine(workingDirectory, $"sw-import-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(targetDirectory);
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, targetDirectory, cancellationToken);

                string manifestPath = Path.Combine(targetDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidDataException("交接包缺少 manifest.json。");
                }

                var manifest = JsonSerializer.Deserialize<SingleWindowPackageManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken),
                    JsonOptions) ?? throw new InvalidDataException("交接包 manifest.json 无效。");

                IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries =
                    manifest.PackageType == SingleWindowPackageType.ReceiptPackage
                        ? await LoadReceiptImportEntriesAsync(targetDirectory, manifest, cancellationToken)
                        : [];
                IReadOnlyList<SingleWindowReceiptParseResult> parsedReceipts = receiptEntries
                    .Select(item => item.Receipt)
                    .ToList();

                var tracking = await TryRecordPackageImportAsync(
                    packagePath,
                    targetDirectory,
                    manifest,
                    receiptEntries,
                    cancellationToken);

                return new SingleWindowImportedPackage
                {
                    WorkingDirectory = targetDirectory,
                    Manifest = manifest,
                    ParsedReceipts = parsedReceipts,
                    TrackingBatchId = tracking.BatchId,
                    TrackingStatus = tracking.Status,
                    PersistedReceiptCount = tracking.SavedReceiptCount
                };
            }
            catch
            {
                AtomicFileHelper.TryDeleteDirectory(targetDirectory);
                throw;
            }
        }

        private async Task<IReadOnlyList<SingleWindowReceiptImportEntry>> LoadReceiptImportEntriesAsync(
            string workingDirectory,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            var results = new List<SingleWindowReceiptImportEntry>(manifest.PayloadFiles.Count);
            foreach (var file in manifest.PayloadFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(file?.RelativePath))
                {
                    continue;
                }

                string path = Path.Combine(workingDirectory, file.RelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                string content = await File.ReadAllTextAsync(path, cancellationToken);
                results.Add(new SingleWindowReceiptImportEntry
                {
                    Receipt = _singleWindowReceiptParser.Parse(
                        manifest.BusinessType,
                        content,
                        Path.GetFileName(path)),
                    RawContent = content
                });
            }

            return results;
        }
    }
}
