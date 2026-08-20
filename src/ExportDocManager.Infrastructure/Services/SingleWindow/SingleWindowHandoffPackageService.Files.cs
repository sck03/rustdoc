using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private static async Task<IReadOnlyList<SingleWindowPackageFile>> CopyAttachmentsAsync(
            string tempDirectory,
            IReadOnlyList<SingleWindowAttachmentSource> attachments,
            CancellationToken cancellationToken)
        {
            var selectedAttachments = SingleWindowAttachmentResourcePolicy.ValidateAndSelect(attachments);
            if (selectedAttachments.Count == 0)
            {
                return [];
            }

            string attachmentDirectory = Path.Combine(tempDirectory, "attachments");
            Directory.CreateDirectory(attachmentDirectory);
            var packageFiles = new List<SingleWindowPackageFile>();
            var usedFileNames = new HashSet<string>(PortablePathKey.Comparer);

            long copiedBytes = 0;
            foreach (var attachment in selectedAttachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? Path.GetFileName(attachment.FilePath)
                    : Path.GetFileName(attachment.FileName);
                fileName = CopyFileToPackageDirectory(
                    attachment.FilePath,
                    usedFileNames,
                    fileName);
                string destination = Path.Combine(attachmentDirectory, fileName);
                await using (var source = new FileStream(
                    attachment.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long maximumRemainingBytes = Math.Min(
                        SingleWindowAttachmentResourcePolicy.MaximumSingleAttachmentBytes,
                        SingleWindowAttachmentResourcePolicy.MaximumTotalAttachmentBytes - copiedBytes);
                    copiedBytes += await BoundedStreamHelper.CopyToAsync(
                        source,
                        output,
                        maximumRemainingBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                packageFiles.Add(await SingleWindowPackageIntegrity.DescribeFileAsync(
                    destination,
                    PathBoundaryHelper.ToProtocolRelativePath("attachments", fileName),
                    string.IsNullOrWhiteSpace(attachment.MediaType) ? "application/octet-stream" : attachment.MediaType,
                    attachment.Description,
                    cancellationToken));
            }

            return packageFiles;
        }

        private static async Task<IReadOnlyList<SingleWindowPackageFile>> CopyReceiptFilesAsync(
            string receiptsDirectory,
            IEnumerable<string> receiptFiles,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(receiptsDirectory);

            var copiedFiles = new List<SingleWindowPackageFile>();
            var usedFileNames = new HashSet<string>(PortablePathKey.Comparer);
            foreach (var file in receiptFiles.Where(File.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SingleWindowPackageIntegrity.ValidateReceiptSourceFileAsync(file, cancellationToken);
                string copiedFileName = CopyFileToPackageDirectory(
                    file,
                    usedFileNames,
                    Path.GetFileName(file));
                string destination = Path.Combine(receiptsDirectory, copiedFileName);
                await FileCopyHelper.CopyAsync(file, destination, overwrite: true, cancellationToken);
                copiedFiles.Add(await SingleWindowPackageIntegrity.DescribeFileAsync(
                    destination,
                    PathBoundaryHelper.ToProtocolRelativePath("receipts", copiedFileName),
                    "application/xml",
                    Path.GetFileName(file),
                    cancellationToken));
            }

            return copiedFiles;
        }

        private static string CopyFileToPackageDirectory(
            string sourcePath,
            ISet<string> usedFileNames,
            string preferredFileName)
        {
            string fileName = string.IsNullOrWhiteSpace(preferredFileName)
                ? Path.GetFileName(sourcePath)
                : Path.GetFileName(preferredFileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "file";
            }

            fileName = CrossPlatformFileNamePolicy.SanitizeFileNamePart(fileName, '_', "file");

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string candidate = fileName;
            int suffix = 2;
            while (!usedFileNames.Add(candidate))
            {
                candidate = $"{baseName}_{suffix++}{extension}";
            }

            return candidate;
        }

        private string BuildBatchReference(SingleWindowBusinessType businessType, int submissionVersion)
        {
            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "COO" : "ACD";
            string versionText = $"V{Math.Max(1, submissionVersion):000}";
            string guidPart = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string batchReference = $"{prefix}-{versionText}-{_clock.Now:yyyyMMddHHmmss}-{guidPart}".ToUpperInvariant();
            return batchReference.Length <= 40
                ? batchReference
                : batchReference[..40];
        }
    }
}
