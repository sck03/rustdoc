using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService
    {
        public async Task<int> RecordSubmitPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            string authenticationSecret,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            SingleWindowPackageIntegrity.ValidateAuthentication(
                manifest,
                authenticationSecret,
                "提交包认证签名无效，已拒绝归档。" );
            var archive = await ReadSubmitPackageArchiveAsync(packagePath, cancellationToken)
                .ConfigureAwait(false);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    await EnsureCanAccessManifestAsync(context, manifest, cancellationToken);

                    string batchReference = NormalizeBatchReference(manifest.BatchReference);
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.BatchReference == batchReference, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new ResourceNotFoundException("单一窗口提交版本尚未预留或当前账号无权访问。");
                    if (batch.Status != SingleWindowBatchStatusCatalog.Preparing)
                    {
                        throw new ResourceConflictException("单一窗口提交版本已完成或已失败，不能重复写入提交包。");
                    }

                    EnsureManifestMatchesBatch(batch, manifest, requireSourcePackageDigest: false);
                    batch.Status = SingleWindowBatchStatusCatalog.SubmitPackageExported;
                    batch.PayloadFileCount = manifest.PayloadFiles.Count;
                    batch.AttachmentFileCount = manifest.AttachmentFiles.Count;
                    batch.WarningCount = manifest.Warnings.Count;
                    batch.CompanyScope = manifest.CompanyScope ?? string.Empty;
                    batch.SubmitPackageDigest = manifest.ContentDigest ?? string.Empty;
                    batch.SubmitPackagePath = packagePath ?? string.Empty;
                    batch.CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty;
                    batch.AssignedStationKey = manifest.StationKey ?? string.Empty;
                    batch.AssignedProfileKey = manifest.ClientProfileKey ?? string.Empty;
                    batch.AssignedCardIdentifier = manifest.CardIdentifier ?? string.Empty;
                    batch.ClientProfileName = manifest.ClientProfileName ?? string.Empty;
                    batch.ProtectedAssignmentSecret = _secretProtector.Protect(authenticationSecret);
                    batch.LastError = string.Empty;
                    batch.UpdatedAt = DateTime.UtcNow;
                    await StoreSubmitPackageArchiveAsync(context, batch, archive, cancellationToken)
                        .ConfigureAwait(false);

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        manifest,
                        direction: "Exported"));
                    await context.SaveChangesAsync(cancellationToken);
                    return batch.Id;
                },
                cancellationToken);
        }

        public async Task<int> RecordSubmitPackageImportAsync(
            string packagePath,
            SingleWindowImportedPackage imported,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(imported);
            var stationBinding = await EnsureLocalOperationStationCanImportAsync(
                    imported.Manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            var archive = await ReadSubmitPackageArchiveAsync(packagePath, cancellationToken)
                .ConfigureAwait(false);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    await EnsureCanAccessManifestAsync(context, imported.Manifest, cancellationToken);

                    var batch = await FindOrCreateSubmitBatchAsync(
                        context,
                        imported.Manifest,
                        cancellationToken);

                    EnsureSubmitPackageCanBindToStation(
                        batch,
                        stationBinding.StationKey,
                        stationBinding.Profile.ProfileKey,
                        stationBinding.Profile.CardIdentifier);

                    batch.Status = SingleWindowBatchStatusCatalog.SubmitPackageImported;
                    batch.UpdatedAt = DateTime.Now;
                    batch.SubmissionVersion = batch.SubmissionVersion > 0 ? batch.SubmissionVersion : imported.Manifest.SubmissionVersion;
                    batch.DraftRevision = Math.Max(batch.DraftRevision, imported.Manifest.DraftRevision);
                    batch.SourceBaselineHash = string.IsNullOrWhiteSpace(batch.SourceBaselineHash)
                        ? imported.Manifest.SourceBaselineHash ?? string.Empty
                        : batch.SourceBaselineHash;
                    batch.CompanyScope = imported.Manifest.CompanyScope ?? string.Empty;
                    batch.SubmitPackageDigest = imported.Manifest.ContentDigest ?? string.Empty;
                    batch.AssignedStationKey = stationBinding.StationKey;
                    batch.AssignedProfileKey = stationBinding.Profile.ProfileKey;
                    batch.AssignedCardIdentifier = stationBinding.Profile.CardIdentifier;
                    batch.ClientProfileName = stationBinding.Profile.ProfileName;
                    batch.ProtectedAssignmentSecret = stationBinding.Profile.ProtectedHandoffSecret;
                    if (string.IsNullOrWhiteSpace(batch.SubmitPackagePath))
                    {
                        batch.SubmitPackagePath = packagePath ?? string.Empty;
                    }

                    batch.WorkingDirectoryPath = string.Empty;
                    await StoreSubmitPackageArchiveAsync(context, batch, archive, cancellationToken)
                        .ConfigureAwait(false);

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        imported.Manifest,
                        direction: "Imported"));

                    await context.SaveChangesAsync(cancellationToken);
                    return batch.Id;
                },
                cancellationToken);
        }

        public async Task<int> RecordReceiptPackageExportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    await EnsureCanAccessManifestAsync(context, manifest, cancellationToken);

                    var batch = await FindReceiptBatchAsync(
                        context,
                        manifest,
                        cancellationToken);

                    batch.Status = string.IsNullOrWhiteSpace(batch.LastBusinessStatus)
                        ? SingleWindowBatchStatusCatalog.ReceiptPackageExported
                        : batch.Status;
                    batch.LastReceiptPackagePath = packagePath ?? string.Empty;
                    batch.UpdatedAt = DateTime.Now;

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        manifest,
                        direction: "Exported"));

                    await context.SaveChangesAsync(cancellationToken);
                    return batch.Id;
                },
                cancellationToken);
        }

        public async Task<SingleWindowTrackingImportResult> RecordReceiptPackageImportAsync(
            string packagePath,
            SingleWindowPackageManifest manifest,
            IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    await EnsureCanAccessManifestAsync(context, manifest, cancellationToken);

                    var batch = await FindReceiptBatchAsync(context, manifest, cancellationToken);
                    var normalizedReceiptEntries = BuildReceiptImportEntries(receiptEntries);
                    if (normalizedReceiptEntries.Count == 0)
                    {
                        throw new InvalidDataException("回执包没有可解析的有效回执，未写入任何业务状态。");
                    }
                    if (normalizedReceiptEntries.Any(item => item.Receipt.BusinessType != manifest.BusinessType))
                    {
                        throw new InvalidDataException("回执内容的业务类型与回执包 manifest 不一致。");
                    }
                    ValidateReceiptReferences(batch, manifest, normalizedReceiptEntries);
                    var existingReceiptKeys = await LoadExistingReceiptKeysAsync(
                        context,
                        batch.Id,
                        normalizedReceiptEntries,
                        cancellationToken);
                    int savedReceiptCount = 0;

                    foreach (var entry in normalizedReceiptEntries)
                    {
                        if (!existingReceiptKeys.Add(entry.ContentSha256))
                        {
                            continue;
                        }

                        var receipt = entry.Receipt;
                        context.SwReceiptLogs.Add(new SwReceiptLog
                        {
                            BatchId = batch.Id,
                            BusinessType = receipt.BusinessType.ToString(),
                            ReceiptKind = receipt.ReceiptKind.ToString(),
                            ReferenceNo = NormalizeReceiptKeyPart(receipt.ReferenceNo),
                            ReceiptCode = NormalizeReceiptKeyPart(receipt.ReceiptCode),
                            ReceiptMessage = receipt.ReceiptMessage ?? string.Empty,
                            BusinessStatus = receipt.BusinessStatus.ToString(),
                            SourceFileName = NormalizeReceiptKeyPart(receipt.SourceFileName),
                            ImportedAt = DateTime.UtcNow,
                            OccurredAt = receipt.OccurredAt,
                            RawContent = entry.RawContent,
                            ContentSha256 = entry.ContentSha256
                        });
                        savedReceiptCount++;
                    }

                    var normalizedReceipts = normalizedReceiptEntries
                        .Select(item => item.Receipt)
                        .ToList();
                    var primaryReceipt = SelectPrimaryReceipt(normalizedReceipts);
                    string status = ResolveMonotonicBatchStatus(batch, primaryReceipt);
                    bool shouldUpdateSummary = ShouldUpdateReceiptSummary(batch, primaryReceipt);
                    batch.Status = status;
                    if (shouldUpdateSummary)
                    {
                        batch.LastBusinessStatus = primaryReceipt?.BusinessStatus.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(batch.ReferenceNo) &&
                            !string.IsNullOrWhiteSpace(primaryReceipt?.ReferenceNo))
                        {
                            batch.ReferenceNo = primaryReceipt.ReferenceNo.Trim();
                        }
                        batch.LastReceiptKind = primaryReceipt?.ReceiptKind.ToString() ?? string.Empty;
                        batch.LastReceiptCode = primaryReceipt?.ReceiptCode ?? string.Empty;
                        batch.LastReceiptMessage = primaryReceipt?.ReceiptMessage ?? string.Empty;
                        batch.LastReceiptAt = primaryReceipt?.OccurredAt ?? DateTime.UtcNow;
                    }
                    batch.LastReceiptPackagePath = packagePath ?? string.Empty;
                    batch.UpdatedAt = DateTime.UtcNow;

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        manifest,
                        direction: "Imported"));

                    await ApplyReceiptWriteBackAsync(
                        context,
                        batch,
                        shouldUpdateSummary ? primaryReceipt : null,
                        status,
                        cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    return new SingleWindowTrackingImportResult
                    {
                        BatchId = batch.Id,
                        Status = status,
                        SavedReceiptCount = savedReceiptCount
                    };
                },
                cancellationToken);
        }

        private async Task EnsureCanAccessManifestAsync(
            AppDbContext context,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(manifest);

            if (manifest.SourceInvoiceId <= 0 ||
                !_businessDataAccessScope.ShouldFilterBusinessData())
            {
                return;
            }

            bool canAccess = await _businessDataAccessScope.CanAccessInvoiceAsync(
                    context,
                    manifest.SourceInvoiceId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!canAccess)
            {
                throw new PermissionDeniedException("无权限写入该发票的单一窗口跟踪记录。");
            }
        }

        private static void ValidateReceiptReferences(
            SwSubmissionBatch batch,
            SingleWindowPackageManifest manifest,
            IReadOnlyList<ReceiptImportEntry> receiptEntries)
        {
            string manifestReference = manifest.ReceiptReferenceNo?.Trim() ?? string.Empty;
            var parsedReferences = receiptEntries
                .Select(item => item.Receipt.ReferenceNo?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (parsedReferences.Length > 1)
            {
                throw new InvalidDataException("回执包包含多个不同的官方业务编号，不能写入同一批次。" );
            }

            if (parsedReferences.Length == 1 &&
                !string.Equals(parsedReferences[0], manifestReference, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("回执内容的官方业务编号与回执包 manifest 不一致。" );
            }

            string parsedReference = parsedReferences.SingleOrDefault() ?? manifestReference;
            if (!string.IsNullOrWhiteSpace(batch.ReferenceNo) &&
                !string.IsNullOrWhiteSpace(parsedReference) &&
                !string.Equals(batch.ReferenceNo, parsedReference, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("回执官方业务编号不属于当前批次。" );
            }
        }

        internal static void EnsureSubmitPackageCanBindToStation(
            SwSubmissionBatch batch,
            string stationKey,
            string profileKey,
            string cardIdentifier)
        {
            bool hasExistingBinding = !string.IsNullOrWhiteSpace(batch.AssignedStationKey) ||
                                      !string.IsNullOrWhiteSpace(batch.AssignedProfileKey) ||
                                      !string.IsNullOrWhiteSpace(batch.AssignedCardIdentifier);
            if (!hasExistingBinding)
            {
                return;
            }

            bool sameBinding = string.Equals(
                                   batch.AssignedStationKey,
                                   stationKey,
                                   StringComparison.Ordinal) &&
                               string.Equals(
                                   batch.AssignedProfileKey,
                                   profileKey,
                                   StringComparison.Ordinal) &&
                               string.Equals(
                                   batch.AssignedCardIdentifier,
                                   cardIdentifier,
                                   StringComparison.Ordinal);
            if (!sameBinding)
            {
                throw new ResourceConflictException(
                    "该提交包已绑定其他持卡机或操作卡档案，不能通过重复导入改绑。");
            }

            bool isAwaitingFirstImport = string.Equals(
                batch.Status,
                SingleWindowBatchStatusCatalog.SubmitPackageExported,
                StringComparison.Ordinal);
            bool isAlreadyImported = string.Equals(
                batch.Status,
                SingleWindowBatchStatusCatalog.SubmitPackageImported,
                StringComparison.Ordinal);
            if (!isAwaitingFirstImport && !isAlreadyImported)
            {
                throw new ResourceConflictException(
                    "该提交包已经发送或进入回执阶段，不能重复导入并回退批次状态。");
            }
        }

        private async Task<SingleWindowStationBinding> EnsureLocalOperationStationCanImportAsync(
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            if (!_isSqlite)
            {
                throw new ServiceValidationException("提交包只能导入独立 SQLite 持卡机；PostgreSQL 网络版不承担官方客户端操作。");
            }

            var profile = await _clientProfileService.GetActiveAsync(cancellationToken)
                .ConfigureAwait(false);
            if (profile.Id <= 0 || !profile.IsEnabled)
            {
                throw new ServiceValidationException("请先在本持卡机配置公司抬头、操作卡和官方客户端目录，再导入提交包。");
            }

            if (!string.Equals(
                    profile.CompanyScope?.Trim(),
                    manifest.CompanyScope?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new PermissionDeniedException("提交包公司抬头与本持卡机绑定公司不一致，已拒绝导入。");
            }

            if (!string.Equals(profile.StationKey, manifest.StationKey, StringComparison.Ordinal) ||
                !string.Equals(profile.ProfileKey, manifest.ClientProfileKey, StringComparison.Ordinal) ||
                !string.Equals(profile.CardIdentifier, manifest.CardIdentifier, StringComparison.Ordinal) ||
                !string.Equals(profile.ProfileName, manifest.ClientProfileName, StringComparison.Ordinal))
            {
                throw new PermissionDeniedException(
                    "提交包已预分派给其他持卡机、操作档案或操作卡，当前档案不能领取。" );
            }

            bool canHandle = manifest.BusinessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CanSubmitCustomsCoo,
                SingleWindowBusinessType.AgentConsignment => profile.CanSubmitAgentConsignment,
                _ => false
            };
            if (!canHandle)
            {
                throw new PermissionDeniedException("本持卡机操作卡未启用该单一窗口业务能力。");
            }

            string stationKey = await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(profile.StationKey, stationKey, StringComparison.Ordinal))
            {
                throw new ResourceConflictException("本持卡机身份与操作卡档案不一致，请重新保存操作卡配置。");
            }

            SingleWindowPackageIntegrity.ValidateAuthentication(
                manifest,
                SingleWindowStationAssignmentCode.UnprotectProfileSecret(profile, _secretProtector),
                "提交包来源认证失败或授权码已失效，已拒绝导入。" );

            return new SingleWindowStationBinding(stationKey, profile);
        }

        private static async Task<SubmitPackageArchivePayload> ReadSubmitPackageArchiveAsync(
            string packagePath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException("单一窗口提交包归档文件不存在。", packagePath);
            }

            var info = new FileInfo(packagePath);
            const long maximumBytes = 50L * 1024L * 1024L;
            if (info.Length <= 0 || info.Length > maximumBytes)
            {
                throw new InvalidDataException("单一窗口提交包必须大于 0 且不超过 50 MB。");
            }

            byte[] content = await File.ReadAllBytesAsync(packagePath, cancellationToken)
                .ConfigureAwait(false);
            string sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
            return new SubmitPackageArchivePayload(content, sha256);
        }

        private static async Task StoreSubmitPackageArchiveAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            SubmitPackageArchivePayload archive,
            CancellationToken cancellationToken)
        {
            var existing = await context.SwSubmitPackageArchives
                .FirstOrDefaultAsync(item => item.BatchId == batch.Id, cancellationToken)
                .ConfigureAwait(false);
            if (existing != null &&
                !string.Equals(existing.Sha256, archive.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("同一提交批次的归档包内容发生变化，已拒绝覆盖。");
            }

            if (existing == null)
            {
                await context.SwSubmitPackageArchives.AddAsync(new SwSubmitPackageArchive
                {
                    BatchId = batch.Id,
                    SizeBytes = archive.Content.LongLength,
                    Sha256 = archive.Sha256,
                    Content = archive.Content,
                    CreatedAtUtc = DateTime.UtcNow
                }, cancellationToken).ConfigureAwait(false);
            }

            batch.SubmitPackageArchiveSha256 = archive.Sha256;
            batch.SubmitPackageArchiveSizeBytes = archive.Content.LongLength;
        }

        private sealed record SubmitPackageArchivePayload(byte[] Content, string Sha256);

        private sealed record SingleWindowStationBinding(string StationKey, SwClientProfile Profile);
    }
}
