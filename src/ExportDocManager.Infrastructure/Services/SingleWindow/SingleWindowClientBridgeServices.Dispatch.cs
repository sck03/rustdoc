using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        public async Task<SingleWindowClientDispatchResult> DispatchBatchToImportRootAsync(
            int batchId,
            string importRootPath,
            string profileName = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(importRootPath))
            {
                throw new InvalidOperationException("导入目录不能为空。");
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要发送的单一窗口批次。");

            string sourceDirectory = await EnsureWorkingDirectoryAsync(context, batch, cancellationToken);
            var layout = ResolveBusinessLayout(importRootPath, createDirectories: true);
            var dispatchedFiles = await CopyPayloadFilesToOutBoxAsync(
                sourceDirectory,
                layout.OutBox,
                batch.BatchReference,
                cancellationToken);

            batch.Status = SingleWindowBatchStatusCatalog.QueuedToClient;
            batch.ClientProfileName = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName.Trim();
            batch.ClientDispatchPath = layout.OutBox;
            batch.LastClientDispatchAt = DateTime.Now;
            batch.UpdatedAt = DateTime.Now;

            context.SwHandoffPackageRecords.Add(new SwHandoffPackageRecord
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                BusinessType = batch.BusinessType,
                SourceInvoiceId = batch.SourceInvoiceId,
                SourceDocumentType = batch.SourceDocumentType,
                SourceDocumentId = batch.SourceDocumentId,
                InvoiceNo = batch.InvoiceNo,
                PackageType = "ClientDispatch",
                Direction = "ExportedToClient",
                FilePath = layout.OutBox,
                CreatedOnMachine = Environment.MachineName,
                PayloadFileCount = dispatchedFiles.Count,
                AttachmentFileCount = batch.AttachmentFileCount,
                WarningCount = batch.WarningCount,
                CreatedAt = DateTime.Now,
                ManifestJson = string.Empty
            });

            await context.SaveChangesAsync(cancellationToken);

            return new SingleWindowClientDispatchResult
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                TargetDirectory = layout.OutBox,
                ProfileName = batch.ClientProfileName,
                PayloadFileCount = dispatchedFiles.Count,
                AttachmentFileCount = batch.AttachmentFileCount
            };
        }

        private async Task<string> EnsureWorkingDirectoryAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(batch.WorkingDirectoryPath) &&
                Directory.Exists(batch.WorkingDirectoryPath))
            {
                return batch.WorkingDirectoryPath;
            }

            if (string.IsNullOrWhiteSpace(batch.SubmitPackagePath) || !File.Exists(batch.SubmitPackagePath))
            {
                throw new InvalidOperationException("当前批次没有可用的提交包工作目录，也找不到提交包文件。");
            }

            string restoredDirectory = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "Inbox",
                batch.BatchReference);

            if (!Directory.Exists(restoredDirectory))
            {
                Directory.CreateDirectory(restoredDirectory);
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(batch.SubmitPackagePath, restoredDirectory, cancellationToken);
            }

            batch.WorkingDirectoryPath = restoredDirectory;
            await context.SaveChangesAsync(cancellationToken);
            return restoredDirectory;
        }

        private static async Task<IReadOnlyList<string>> CopyPayloadFilesToOutBoxAsync(
            string sourceDirectory,
            string outBoxDirectory,
            string batchReference,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outBoxDirectory);
            string payloadDirectory = Path.Combine(sourceDirectory, "payloads");
            var sourceFiles = Directory.Exists(payloadDirectory)
                ? Directory.GetFiles(payloadDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                : Directory.GetFiles(sourceDirectory, "*.xml", SearchOption.TopDirectoryOnly);

            var targetFiles = new List<string>();
            foreach (var file in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(file);
                string targetPath = BuildOutBoxFilePath(outBoxDirectory, fileName, batchReference);
                await FileCopyHelper.CopyAsync(file, targetPath, overwrite: false, cancellationToken);
                targetFiles.Add(targetPath);
            }

            return targetFiles;
        }

        private static string BuildOutBoxFilePath(string outBoxDirectory, string originalFileName, string batchReference)
        {
            string candidate = Path.Combine(outBoxDirectory, originalFileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(originalFileName);
            string extension = Path.GetExtension(originalFileName);
            string safeBatchReference = string.IsNullOrWhiteSpace(batchReference)
                ? DateTime.Now.ToString("yyyyMMddHHmmssfff")
                : batchReference.Trim();
            return Path.Combine(outBoxDirectory, $"{baseName}_{safeBatchReference}{extension}");
        }
    }
}
