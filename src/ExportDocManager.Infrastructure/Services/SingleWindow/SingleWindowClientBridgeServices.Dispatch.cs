using System.Data;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
            var reservation = await ReserveClientDispatchAsync(
                batchId,
                profile,
                stationKey,
                cancellationToken);
            string importRootPath = ResolveConfiguredRoot(profile, reservation.BusinessType);
            var layout = ResolveBusinessLayout(importRootPath, createDirectories: true);
            string stagingDirectory = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "DispatchStaging",
                $"{reservation.BatchReference}-{Guid.NewGuid():N}");
            PathBoundaryHelper.EnsureWithinRoot(
                stagingDirectory,
                _pathProvider.SingleWindowRoot,
                "单一窗口客户端派发暂存目录越界。");
            IReadOnlyList<string> publishedFiles = [];
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var batch = await _businessDataAccessScope
                    .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                    .FirstAsync(item => item.Id == batchId, cancellationToken);
                var workingPackage = await EnsureWorkingPackageAsync(context, batch, cancellationToken);
                var stagedFiles = await CopyPayloadFilesToOutBoxAsync(
                    workingPackage.Directory,
                    workingPackage.Manifest,
                    stagingDirectory,
                    batch.BatchReference,
                    cancellationToken);
                publishedFiles = await PublishPayloadFilesAsync(
                    stagedFiles,
                    layout.OutBox,
                    batch.BatchReference,
                    cancellationToken);
                await CompleteClientDispatchAsync(
                    batchId,
                    profile,
                    stationKey,
                    layout.OutBox,
                    publishedFiles.Count,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                foreach (string publishedFile in publishedFiles.Reverse())
                {
                    AtomicFileHelper.TryDeleteFile(publishedFile);
                }
                await MarkClientDispatchFailedAsync(batchId, ex.Message, CancellationToken.None);
                throw;
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(stagingDirectory);
            }

            return new SingleWindowClientDispatchResult
            {
                BatchId = reservation.BatchId,
                BatchReference = reservation.BatchReference,
                TargetDirectory = layout.OutBox,
                ProfileName = profile.ProfileName,
                PayloadFileCount = publishedFiles.Count,
                AttachmentFileCount = reservation.AttachmentFileCount
            };
        }

        private async Task<ClientDispatchReservation> ReserveClientDispatchAsync(
            int batchId,
            SwClientProfile profile,
            string stationKey,
            CancellationToken cancellationToken)
        {
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.Id == batchId, token)
                        ?? throw new ResourceNotFoundException("未找到要写入交接 OutBox 的单一窗口批次。");
                    if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
                    {
                        throw new ServiceValidationException("单一窗口批次业务类型无效。");
                    }

                    EnsureBatchBelongsToCurrentStation(batch, profile, stationKey, businessType);
                    if (batch.Status is not SingleWindowBatchStatusCatalog.SubmitPackageImported and
                        not SingleWindowBatchStatusCatalog.ClientDispatchFailed)
                    {
                        throw new ResourceConflictException(
                            "只有本机已导入且尚未派发，或上次派发已完整回滚的提交包可以写入官方客户端。" );
                    }

                    batch.Status = SingleWindowBatchStatusCatalog.ClientDispatching;
                    batch.ClientProfileName = profile.ProfileName;
                    batch.AssignedStationKey = stationKey;
                    batch.AssignedProfileKey = profile.ProfileKey;
                    batch.AssignedCardIdentifier = profile.CardIdentifier;
                    batch.LastError = string.Empty;
                    batch.UpdatedAt = DateTimeOffset.UtcNow;
                    await context.SaveChangesAsync(token);
                    return new ClientDispatchReservation(
                        batch.Id,
                        batch.BatchReference,
                        businessType,
                        batch.AttachmentFileCount);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task CompleteClientDispatchAsync(
            int batchId,
            SwClientProfile profile,
            string stationKey,
            string outBoxPath,
            int payloadFileCount,
            CancellationToken cancellationToken)
        {
            await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.Id == batchId, token)
                        ?? throw new ResourceNotFoundException("客户端派发批次在确认阶段不存在。");
                    EnsureBatchBelongsToCurrentStation(
                        batch,
                        profile,
                        stationKey,
                        Enum.Parse<SingleWindowBusinessType>(batch.BusinessType, true));
                    if (batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching)
                    {
                        throw new ServiceConcurrencyException("客户端派发状态已被其他操作修改，不能确认完成。");
                    }

                    DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                    batch.Status = SingleWindowBatchStatusCatalog.QueuedToClient;
                    batch.ClientDispatchPath = outBoxPath;
                    batch.LastClientDispatchAt = nowUtc;
                    batch.LastError = string.Empty;
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
                        FilePath = outBoxPath,
                        CreatedOnMachine = Environment.MachineName,
                        PayloadFileCount = payloadFileCount,
                        AttachmentFileCount = batch.AttachmentFileCount,
                        WarningCount = batch.WarningCount,
                        ContentDigest = batch.SubmitPackageDigest,
                        CreatedAt = nowUtc,
                        ManifestJson = string.Empty
                    });
                    await context.SaveChangesAsync(token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task MarkClientDispatchFailedAsync(
            int batchId,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var batch = await context.SwSubmissionBatches
                    .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
                if (batch == null || batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching)
                {
                    return;
                }

                batch.Status = SingleWindowBatchStatusCatalog.ClientDispatchFailed;
                batch.LastError = string.IsNullOrWhiteSpace(errorMessage)
                    ? "写入官方客户端目录失败。"
                    : errorMessage.Trim()[..Math.Min(errorMessage.Trim().Length, 2000)];
                batch.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception trackingException)
            {
                _logger.LogError(
                    trackingException,
                    "Marking Single Window client dispatch {BatchId} as failed failed",
                    batchId);
            }
        }

        private static string ResolveConfiguredRoot(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            string resolved = SingleWindowClientProfilePathResolver.ResolveConfiguredRoot(profile, businessType);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new ServiceValidationException("本机操作卡尚未配置该业务的官方单一窗口客户端目录。");
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
                throw new ServiceValidationException("请先完成本持卡机的公司抬头、操作卡和官方客户端目录配置。");
            }

            if (!string.Equals(batch.AssignedStationKey, stationKey, StringComparison.Ordinal) ||
                !string.Equals(profile.StationKey, stationKey, StringComparison.Ordinal))
            {
                throw new PermissionDeniedException("该提交包不属于当前持卡机。");
            }

            if (!string.Equals(batch.AssignedProfileKey, profile.ProfileKey, StringComparison.Ordinal) ||
                !string.Equals(batch.AssignedCardIdentifier, profile.CardIdentifier, StringComparison.Ordinal))
            {
                throw new PermissionDeniedException(
                    "该批次绑定了其他公司或操作卡档案，请先切换到导入该批次的操作档案。");
            }

            if (!string.Equals(batch.CompanyScope, profile.CompanyScope, StringComparison.OrdinalIgnoreCase))
            {
                throw new PermissionDeniedException("提交包公司抬头与本机操作卡绑定公司不一致。");
            }

            bool canHandle = businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CanSubmitCustomsCoo,
                SingleWindowBusinessType.AgentConsignment => profile.CanSubmitAgentConsignment,
                _ => false
            };
            if (!canHandle)
            {
                throw new PermissionDeniedException("本机操作卡未启用该单一窗口业务能力。");
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
                ?? throw new ResourceNotFoundException("共享数据库中缺少该批次的提交包归档，无法在本操作机恢复。");
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
                    throw new InvalidDataException("提交包没有可写入交接 OutBox 的 XML 报文。");
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

        internal static async Task<IReadOnlyList<string>> PublishPayloadFilesAsync(
            IReadOnlyList<string> stagedFiles,
            string outBoxDirectory,
            string batchReference,
            CancellationToken cancellationToken,
            Action<int, string>? beforeCommit = null)
        {
            Directory.CreateDirectory(outBoxDirectory);
            var publishedFiles = new List<string>(stagedFiles?.Count ?? 0);
            var publishPlans = new List<(string PendingPath, string TargetPath)>(stagedFiles?.Count ?? 0);
            var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string stagedFile in stagedFiles ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string targetPath = BuildOutBoxFilePath(
                        outBoxDirectory,
                        Path.GetFileName(stagedFile),
                        batchReference,
                        reservedTargets);
                    reservedTargets.Add(targetPath);
                    string pendingPath = targetPath + $".pending-{Guid.NewGuid():N}";
                    await FileCopyHelper.CopyAsync(
                        stagedFile,
                        pendingPath,
                        overwrite: false,
                        cancellationToken);
                    publishPlans.Add((pendingPath, targetPath));
                }

                for (int index = 0; index < publishPlans.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var plan = publishPlans[index];
                    beforeCommit?.Invoke(index, plan.TargetPath);
                    File.Move(plan.PendingPath, plan.TargetPath, overwrite: false);
                    publishedFiles.Add(plan.TargetPath);
                }

                return publishedFiles;
            }
            catch
            {
                foreach (var plan in publishPlans)
                {
                    AtomicFileHelper.TryDeleteFile(plan.PendingPath);
                }
                foreach (string publishedFile in publishedFiles.AsEnumerable().Reverse())
                {
                    AtomicFileHelper.TryDeleteFile(publishedFile);
                }

                throw;
            }
        }

        private static string BuildOutBoxFilePath(
            string outBoxDirectory,
            string originalFileName,
            string batchReference,
            ISet<string>? reservedPaths = null)
        {
            string candidate = Path.Combine(outBoxDirectory, originalFileName);
            if (!File.Exists(candidate) &&
                !Directory.Exists(candidate) &&
                !(reservedPaths?.Contains(candidate) ?? false))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(originalFileName);
            string extension = Path.GetExtension(originalFileName);
            string safeBatchReference = string.IsNullOrWhiteSpace(batchReference)
                ? Guid.NewGuid().ToString("N")[..17]
                : batchReference.Trim();
            candidate = Path.Combine(outBoxDirectory, $"{baseName}_{safeBatchReference}{extension}");
            int suffix = 2;
            while (File.Exists(candidate) ||
                   Directory.Exists(candidate) ||
                   (reservedPaths?.Contains(candidate) ?? false))
            {
                candidate = Path.Combine(
                    outBoxDirectory,
                    $"{baseName}_{safeBatchReference}_{suffix++}{extension}");
            }

            return candidate;
        }

        private sealed record SingleWindowWorkingPackage(
            string Directory,
            SingleWindowPackageManifest Manifest);

        private sealed record ClientDispatchReservation(
            int BatchId,
            string BatchReference,
            SingleWindowBusinessType BusinessType,
            int AttachmentFileCount);
    }
}
