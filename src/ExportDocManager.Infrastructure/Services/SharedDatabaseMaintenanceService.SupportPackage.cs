using System.IO.Compression;
using System.Text.Json;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        public async Task<SupportPackageResult> CreateSupportPackageAsync(CancellationToken cancellationToken = default)
        {
            return await CreateSupportPackageAsync(new SupportPackageOptions(), cancellationToken).ConfigureAwait(false);
        }

        public async Task<SupportPackageResult> CreateSupportPackageAsync(
            SupportPackageOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= new SupportPackageOptions();
            cancellationToken.ThrowIfCancellationRequested();
            string timestamp = _clock.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string fileName = $"{timestamp}_{Guid.NewGuid():N}_support_package.zip";
            string path = Path.Combine(SupportPackageRoot, fileName);
            string tempPath = AtomicFileHelper.GetSiblingTempFilePath(path);

            try
            {
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    await WriteJsonEntryAsync(archive, "diagnostics/runtime.json", CreateRuntimeDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/database.json", CreateDatabaseDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/paths.json", CreatePathDiagnostics(), cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "diagnostics/settings-redacted.json", ReadRedactedSettings(), cancellationToken).ConfigureAwait(false);
                    await WriteJobSnapshotAsync(archive, cancellationToken).ConfigureAwait(false);
                    await WriteRecentLogsAsync(archive, cancellationToken).ConfigureAwait(false);
                    await WriteOptionalSupportPackageEntriesAsync(archive, options, cancellationToken).ConfigureAwait(false);
                }

                AtomicFileHelper.ReplaceFile(tempPath, path);
                var file = new FileInfo(path);
                return new SupportPackageResult(
                    true,
                    $"支持包已导出：{file.Name}",
                    file.Name,
                    file.FullName,
                    file.Length,
                    SupportPackageRoot,
                    SupportPackageStoragePolicy);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static async Task WriteJsonEntryAsync<T>(
            ZipArchive archive,
            string entryName,
            T value,
            CancellationToken cancellationToken)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

    }
}
