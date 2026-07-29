using System.Text.Json;
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
            CancellationToken cancellationToken = default)
        {
            EnsureSqliteStation();
            var profile = await _clientProfileService.GetActiveAsync(cancellationToken);
            string stationKey = await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要发送的单一窗口批次。");
            if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
            {
                throw new InvalidOperationException("单一窗口批次业务类型无效。");
            }

            EnsureBatchBelongsToCurrentStation(batch, profile, stationKey, businessType);
            if (batch.Status != SingleWindowBatchStatusCatalog.SubmitPackageImported)
            {
                throw new InvalidOperationException("只有本机已导入且尚未发送的提交包可以进入官方客户端目录。");
            }

            string importRootPath = ResolveConfiguredRoot(profile, businessType);

            var workingPackage = await EnsureWorkingPackageAsync(context, batch, cancellationToken);
            var layout = ResolveBusinessLayout(importRootPath, createDirectories: true);
            var dispatchedFiles = await CopyPayloadFilesToOutBoxAsync(
                workingPackage.Directory,
                workingPackage.Manifest,
                layout.OutBox,
                batch.BatchReference,
                cancellationToken);

            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                batch.Status = SingleWindowBatchStatusCatalog.QueuedToClient;
                batch.ClientProfileName = profile.ProfileName;
                batch.ClientDispatchPath = layout.OutBox;
                batch.LastClientDispatchAt = nowUtc;
                batch.AssignedStationKey = stationKey;
                batch.AssignedProfileKey = profile.ProfileKey;
                batch.AssignedCardIdentifier = profile.CardIdentifier;
                batch.UpdatedAt = nowUtc;

                context.SwHandoffPackageRecords.Add(new SwHandoffPackageRecord
                {
                    BatchId = batch.Id,
                    BatchReference = batch.BatchReference,
                    BusinessType = batch.BusinessType,
                    SourceInvoiceId = batch.SourceInvoiceId,
                    SourceDocumentType = batch.SourceDocumentType,
                    SourceDocumentId = batch.SourceDocumentId,
                    InvoiceNo = batch.InvoiceNo,
                    CompanyScope = batch.CompanyScope,
                    StationKey = stationKey,
                    PackageType = "ClientDispatch",
                    Direction = "ExportedToClient",
                    FilePath = layout.OutBox,
                    CreatedOnMachine = Environment.MachineName,
                    PayloadFileCount = dispatchedFiles.Count,
                    AttachmentFileCount = batch.AttachmentFileCount,
                    WarningCount = batch.WarningCount,
                    ContentDigest = batch.SubmitPackageDigest,
                    CreatedAt = nowUtc,
                    ManifestJson = string.Empty
                });

                await context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                foreach (string dispatchedFile in dispatchedFiles)
                {
                    AtomicFileHelper.TryDeleteFile(dispatchedFile);
                }

                throw;
            }

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

        private static string ResolveConfiguredRoot(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            string resolved = SingleWindowClientProfilePathResolver.ResolveConfiguredRoot(profile, businessType);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new InvalidOperationException("本机操作卡尚未配置该业务的官方单一窗口客户端目录。");
            }

            return SingleWindowClientProfilePathResolver.NormalizeClientRootPath(resolved);
        }

        private static void EnsureBatchBelongsToCurrentStation(
            SwSubmissionBatch batch,
            SwClientProfile profile,
            string stationKey,
            SingleWindowBusinessType businessType)
        {
            if (profile.Id <= 0 || !profile.IsEnabled)
            {
                throw new InvalidOperationException("请先完成本持卡机的公司抬头、操作卡和官方客户端目录配置。");
            }

            if (!string.Equals(batch.AssignedStationKey, stationKey, StringComparison.Ordinal) ||
                !string.Equals(profile.StationKey, stationKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("该提交包不属于当前持卡机。");
            }

            if (!string.Equals(batch.AssignedProfileKey, profile.ProfileKey, StringComparison.Ordinal) ||
                !string.Equals(batch.AssignedCardIdentifier, profile.CardIdentifier, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "该批次绑定了其他公司或操作卡档案，请先切换到导入该批次的操作档案。");
            }

            if (!string.Equals(batch.CompanyScope, profile.CompanyScope, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("提交包公司抬头与本机操作卡绑定公司不一致。");
            }

            bool canHandle = businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CanSubmitCustomsCoo,
                SingleWindowBusinessType.AgentConsignment => profile.CanSubmitAgentConsignment,
                _ => false
            };
            if (!canHandle)
            {
                throw new InvalidOperationException("本机操作卡未启用该单一窗口业务能力。");
            }
        }

        private async Task<SingleWindowWorkingPackage> EnsureWorkingPackageAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            string restoredDirectory = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "Inbox",
                batch.BatchReference);
            PathBoundaryHelper.EnsureWithinRoot(
                restoredDirectory,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包恢复目录越界。");

            if (Directory.Exists(restoredDirectory))
            {
                try
                {
                    var manifest = await ValidateWorkingDirectoryAsync(restoredDirectory, batch, cancellationToken);
                    return new SingleWindowWorkingPackage(restoredDirectory, manifest);
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
                {
                    AtomicFileHelper.TryDeleteDirectory(restoredDirectory);
                }
            }

            string packagePath = await ResolveLocalSubmitPackageAsync(context, batch, cancellationToken);
            Directory.CreateDirectory(restoredDirectory);
            try
            {
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, restoredDirectory, cancellationToken);
                var manifest = await ValidateWorkingDirectoryAsync(restoredDirectory, batch, cancellationToken);
                return new SingleWindowWorkingPackage(restoredDirectory, manifest);
            }
            catch
            {
                AtomicFileHelper.TryDeleteDirectory(restoredDirectory);
                throw;
            }
        }

        private async Task<string> ResolveLocalSubmitPackageAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(batch.SubmitPackagePath) && File.Exists(batch.SubmitPackagePath))
            {
                string localDigest = await SingleWindowPackageIntegrity.ComputeFileSha256Async(
                    batch.SubmitPackagePath,
                    cancellationToken);
                if (string.Equals(
                        localDigest,
                        batch.SubmitPackageArchiveSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return batch.SubmitPackagePath;
                }
            }

            var archive = await context.SwSubmitPackageArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.BatchId == batch.Id, cancellationToken)
                ?? throw new InvalidOperationException("共享数据库中缺少该批次的提交包归档，无法在本操作机恢复。");
            if (archive.Content == null || archive.Content.LongLength != archive.SizeBytes || archive.SizeBytes <= 0)
            {
                throw new InvalidDataException("共享数据库中的提交包归档大小无效。");
            }

            string archiveDigest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(archive.Content));
            if (!string.Equals(archiveDigest, archive.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archiveDigest, batch.SubmitPackageArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("共享数据库中的提交包归档摘要不匹配。");
            }

            string cachePath = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "PackageCache",
                $"{batch.BatchReference}.swpkg");
            PathBoundaryHelper.EnsureWithinRoot(
                cachePath,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包缓存路径越界。");
            await AtomicFileHelper.WriteFileAtomicAsync(
                cachePath,
                (tempPath, token) => File.WriteAllBytesAsync(tempPath, archive.Content, token),
                cancellationToken);
            return cachePath;
        }

        private static async Task<SingleWindowPackageManifest> ValidateWorkingDirectoryAsync(
            string workingDirectory,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(workingDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("恢复的单一窗口提交包缺少 manifest.json。", manifestPath);
            }

            var manifest = JsonSerializer.Deserialize<SingleWindowPackageManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken))
                ?? throw new InvalidDataException("恢复的单一窗口提交包 manifest 无效。");
            await SingleWindowPackageIntegrity.ValidateAsync(
                workingDirectory,
                manifest,
                SingleWindowPackageType.SubmitPackage,
                cancellationToken);

            bool matches = string.Equals(manifest.BatchReference, batch.BatchReference, StringComparison.Ordinal) &&
                           manifest.SourceInvoiceId == batch.SourceInvoiceId &&
                           manifest.SourceDocumentId == batch.SourceDocumentId &&
                           manifest.SubmissionVersion == batch.SubmissionVersion &&
                           string.Equals(manifest.BusinessType.ToString(), batch.BusinessType, StringComparison.Ordinal) &&
                           string.Equals(manifest.CompanyScope, batch.CompanyScope, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manifest.ContentDigest, batch.SubmitPackageDigest, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                throw new InvalidDataException("恢复的单一窗口提交包与领取工单的批次绑定不一致。");
            }

            return manifest;
        }

        private static async Task<IReadOnlyList<string>> CopyPayloadFilesToOutBoxAsync(
            string sourceDirectory,
            SingleWindowPackageManifest manifest,
            string outBoxDirectory,
            string batchReference,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outBoxDirectory);
            var targetFiles = new List<string>();
            try
            {
                foreach (var payload in manifest.PayloadFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string sourcePath = PathBoundaryHelper.ResolveProtocolRelativePath(
                        sourceDirectory,
                        payload.RelativePath,
                        "单一窗口提交报文路径越界。");
                    if (!string.Equals(Path.GetExtension(sourcePath), ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("官方客户端只接受提交包中声明的 XML 报文。");
                    }

                    string fileName = Path.GetFileName(sourcePath);
                    string targetPath = BuildOutBoxFilePath(outBoxDirectory, fileName, batchReference);
                    await FileCopyHelper.CopyAsync(sourcePath, targetPath, overwrite: false, cancellationToken);
                    targetFiles.Add(targetPath);
                }

                if (targetFiles.Count == 0)
                {
                    throw new InvalidDataException("提交包没有可发送到官方客户端的 XML 报文。");
                }

                return targetFiles;
            }
            catch
            {
                foreach (string targetFile in targetFiles)
                {
                    AtomicFileHelper.TryDeleteFile(targetFile);
                }

                throw;
            }
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

        private sealed record SingleWindowWorkingPackage(
            string Directory,
            SingleWindowPackageManifest Manifest);
    }
}
