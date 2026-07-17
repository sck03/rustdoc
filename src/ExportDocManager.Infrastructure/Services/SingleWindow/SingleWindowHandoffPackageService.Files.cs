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
            if (attachments == null || attachments.Count == 0)
            {
                return [];
            }

            string attachmentDirectory = Path.Combine(tempDirectory, "attachments");
            Directory.CreateDirectory(attachmentDirectory);
            var packageFiles = new List<SingleWindowPackageFile>();
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var attachment in attachments.Where(item => item?.Exists == true))
            {
                string fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                    ? Path.GetFileName(attachment.FilePath)
                    : Path.GetFileName(attachment.FileName);
                fileName = CopyFileToPackageDirectory(
                    attachment.FilePath,
                    usedFileNames,
                    fileName);
                string destination = Path.Combine(attachmentDirectory, fileName);
                await FileCopyHelper.CopyAsync(attachment.FilePath, destination, overwrite: true, cancellationToken);
                packageFiles.Add(new SingleWindowPackageFile
                {
                    RelativePath = Path.Combine("attachments", fileName),
                    MediaType = string.IsNullOrWhiteSpace(attachment.MediaType) ? "application/octet-stream" : attachment.MediaType,
                    Description = attachment.Description
                });
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
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in receiptFiles.Where(File.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string copiedFileName = CopyFileToPackageDirectory(
                    file,
                    usedFileNames,
                    Path.GetFileName(file));
                string destination = Path.Combine(receiptsDirectory, copiedFileName);
                await FileCopyHelper.CopyAsync(file, destination, overwrite: true, cancellationToken);
                copiedFiles.Add(new SingleWindowPackageFile
                {
                    RelativePath = Path.Combine("receipts", copiedFileName),
                    MediaType = "application/xml",
                    Description = Path.GetFileName(file)
                });
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

        private static string BuildBatchReference(SingleWindowBusinessType businessType, int submissionVersion)
        {
            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "COO" : "ACD";
            string versionText = $"V{Math.Max(1, submissionVersion):000}";
            string guidPart = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string batchReference = $"{prefix}-{versionText}-{DateTime.Now:yyyyMMddHHmmss}-{guidPart}".ToUpperInvariant();
            return batchReference.Length <= 40
                ? batchReference
                : batchReference[..40];
        }
    }
}
