using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService
    {
        private static async Task<SwSubmissionBatch> FindOrCreateSubmitBatchAsync(
            AppDbContext context,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            if (manifest.PackageType != SingleWindowPackageType.SubmitPackage)
            {
                throw new ServiceValidationException("只有提交包可以建立新的单一窗口跟踪批次。");
            }

            string batchReference = NormalizeBatchReference(manifest.BatchReference);
            var batch = await context.SwSubmissionBatches
                .FirstOrDefaultAsync(item => item.BatchReference == batchReference, cancellationToken);

            if (batch != null)
            {
                EnsureManifestMatchesBatch(batch, manifest, requireSourcePackageDigest: false);
                return batch;
            }

            batch = new SwSubmissionBatch
            {
                BatchReference = batchReference,
                BusinessType = manifest.BusinessType.ToString(),
                SourceInvoiceId = manifest.SourceInvoiceId,
                SourceDocumentType = manifest.SourceDocumentType ?? string.Empty,
                SourceDocumentId = manifest.SourceDocumentId,
                SubmissionVersion = manifest.SubmissionVersion,
                DraftRevision = manifest.DraftRevision,
                InvoiceNo = manifest.InvoiceNo ?? string.Empty,
                ContractNo = manifest.ContractNo ?? string.Empty,
                Status = SingleWindowBatchStatusCatalog.SubmitPackageImported,
                PayloadFileCount = manifest.PayloadFiles.Count,
                AttachmentFileCount = manifest.AttachmentFiles.Count,
                WarningCount = manifest.Warnings.Count,
                SourceBaselineHash = manifest.SourceBaselineHash ?? string.Empty,
                CompanyScope = manifest.CompanyScope ?? string.Empty,
                SubmitPackageDigest = manifest.ContentDigest ?? string.Empty,
                AssignedStationKey = manifest.StationKey ?? string.Empty,
                AssignedProfileKey = manifest.ClientProfileKey ?? string.Empty,
                AssignedCardIdentifier = manifest.CardIdentifier ?? string.Empty,
                ClientProfileName = manifest.ClientProfileName ?? string.Empty,
                CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty,
                CreatedAt = manifest.CreatedAt,
                UpdatedAt = DateTime.Now
            };

            await context.SwSubmissionBatches.AddAsync(batch, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return batch;
        }

        private async Task<SwSubmissionBatch> FindReceiptBatchAsync(
            AppDbContext context,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            if (manifest.PackageType != SingleWindowPackageType.ReceiptPackage)
            {
                throw new ServiceValidationException("当前交接包不是单一窗口回执包。");
            }

            string batchReference = NormalizeBatchReference(manifest.BatchReference);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                .FirstOrDefaultAsync(item => item.BatchReference == batchReference, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("回执包对应的原提交批次不存在或当前账号无权访问。");
            EnsureManifestMatchesBatch(batch, manifest, requireSourcePackageDigest: true);
            if ((!string.IsNullOrWhiteSpace(batch.AssignedStationKey) &&
                 !string.Equals(batch.AssignedStationKey, manifest.StationKey, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(batch.AssignedProfileKey) &&
                 !string.Equals(batch.AssignedProfileKey, manifest.ClientProfileKey, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(batch.AssignedCardIdentifier) &&
                 !string.Equals(batch.AssignedCardIdentifier, manifest.CardIdentifier, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("回执包持卡机、操作档案或操作卡绑定与原批次记录不一致。");
            }

            SingleWindowPackageIntegrity.ValidateAuthentication(
                manifest,
                UnprotectAssignmentSecret(batch),
                "回执包来源认证失败，已拒绝写入办公室归档。" );

            batch.AssignedStationKey = manifest.StationKey ?? string.Empty;
            batch.AssignedProfileKey = manifest.ClientProfileKey ?? string.Empty;
            batch.AssignedCardIdentifier = manifest.CardIdentifier ?? string.Empty;
            batch.ClientProfileName = manifest.ClientProfileName ?? string.Empty;
            return batch;
        }

        private static void EnsureManifestMatchesBatch(
            SwSubmissionBatch batch,
            SingleWindowPackageManifest manifest,
            bool requireSourcePackageDigest)
        {
            bool matches = string.Equals(batch.BatchReference, manifest.BatchReference?.Trim(), StringComparison.Ordinal) &&
                           string.Equals(batch.BusinessType, manifest.BusinessType.ToString(), StringComparison.Ordinal) &&
                           batch.SourceInvoiceId == manifest.SourceInvoiceId &&
                           batch.SourceDocumentId == manifest.SourceDocumentId &&
                           string.Equals(batch.SourceDocumentType, manifest.SourceDocumentType ?? string.Empty, StringComparison.Ordinal) &&
                           batch.SubmissionVersion == manifest.SubmissionVersion &&
                           batch.DraftRevision == manifest.DraftRevision &&
                           string.Equals(batch.SourceBaselineHash, manifest.SourceBaselineHash ?? string.Empty, StringComparison.Ordinal) &&
                           string.Equals(batch.InvoiceNo, manifest.InvoiceNo ?? string.Empty, StringComparison.Ordinal) &&
                           string.Equals(batch.ContractNo, manifest.ContractNo ?? string.Empty, StringComparison.Ordinal) &&
                           string.Equals(batch.CompanyScope, manifest.CompanyScope ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                throw new InvalidDataException("单一窗口交接包与数据库提交批次的来源、版本或公司绑定不一致。");
            }

            string expectedDigest = requireSourcePackageDigest
                ? manifest.SourcePackageDigest
                : manifest.ContentDigest;
            if (!string.IsNullOrWhiteSpace(batch.SubmitPackageDigest) &&
                !string.Equals(batch.SubmitPackageDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("单一窗口交接包绑定的原提交包摘要不一致。");
            }

            if (requireSourcePackageDigest && string.IsNullOrWhiteSpace(batch.SubmitPackageDigest))
            {
                throw new InvalidDataException("数据库提交批次缺少原提交包摘要，不能接收回执。");
            }
        }

        private static List<ReceiptImportEntry> BuildReceiptImportEntries(
            IReadOnlyList<SingleWindowReceiptImportEntry> receiptEntries)
        {
            if (receiptEntries == null || receiptEntries.Count == 0)
            {
                return [];
            }

            var entries = new List<ReceiptImportEntry>(receiptEntries.Count);
            foreach (var entry in receiptEntries)
            {
                var receipt = entry?.Receipt;
                if (receipt == null ||
                    receipt.ReceiptKind == SingleWindowReceiptKind.Unknown)
                {
                    continue;
                }

                string rawContent = entry?.RawContent ?? string.Empty;
                entries.Add(new ReceiptImportEntry(
                    receipt,
                    SingleWindowPackageIntegrity.ComputeTextSha256(rawContent),
                    rawContent));
            }

            return entries;
        }

        private static async Task<HashSet<string>> LoadExistingReceiptKeysAsync(
            AppDbContext context,
            int batchId,
            IReadOnlyList<ReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken)
        {
            if (receiptEntries == null || receiptEntries.Count == 0)
            {
                return [];
            }

            var contentHashes = receiptEntries
                .Select(item => item.ContentSha256)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existingLogs = context.SwReceiptLogs
                .AsNoTracking()
                .Where(log => log.BatchId == batchId);

            if (contentHashes.Count > 0)
            {
                existingLogs = existingLogs.Where(log => contentHashes.Contains(log.ContentSha256));
            }

            return (await existingLogs
                    .Select(log => log.ContentSha256)
                    .ToListAsync(cancellationToken))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeReceiptKeyPart(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static SwHandoffPackageRecord BuildPackageRecord(
            int batchId,
            string? packagePath,
            SingleWindowPackageManifest manifest,
            string direction)
        {
            return new SwHandoffPackageRecord
            {
                BatchId = batchId,
                BatchReference = NormalizeBatchReference(manifest.BatchReference),
                BusinessType = manifest.BusinessType.ToString(),
                SourceInvoiceId = manifest.SourceInvoiceId,
                SourceDocumentType = manifest.SourceDocumentType ?? string.Empty,
                SourceDocumentId = manifest.SourceDocumentId,
                InvoiceNo = manifest.InvoiceNo ?? string.Empty,
                CompanyScope = manifest.CompanyScope ?? string.Empty,
                StationKey = manifest.StationKey ?? string.Empty,
                PackageType = manifest.PackageType.ToString(),
                Direction = direction,
                FilePath = packagePath ?? string.Empty,
                CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty,
                PayloadFileCount = manifest.PayloadFiles.Count,
                AttachmentFileCount = manifest.AttachmentFiles.Count,
                WarningCount = manifest.Warnings.Count,
                ContentDigest = manifest.ContentDigest ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                ManifestJson = JsonSerializer.Serialize(manifest, JsonOptions)
            };
        }

        private static string NormalizeBatchReference(string batchReference)
        {
            return string.IsNullOrWhiteSpace(batchReference)
                ? $"SW-{Guid.NewGuid():N}".ToUpperInvariant()
                : batchReference.Trim();
        }

        internal static SingleWindowReceiptParseResult? SelectPrimaryReceipt(IReadOnlyList<SingleWindowReceiptParseResult>? parsedReceipts)
        {
            var terminalStatuses = (parsedReceipts ?? [])
                .Where(item => item != null && IsTerminalStatus(item.BusinessStatus))
                .Select(item => item.BusinessStatus)
                .Distinct()
                .ToArray();
            if (terminalStatuses.Length > 1)
            {
                throw new InvalidDataException("同一回执包同时包含放行和退单终态，已拒绝写入。" );
            }

            return (parsedReceipts ?? [])
                .Where(item => item != null)
                .OrderByDescending(item => GetStatusRank(item.BusinessStatus))
                .ThenByDescending(item => item.OccurredAt ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        internal static bool ShouldUpdateReceiptSummary(
            SwSubmissionBatch batch,
            SingleWindowReceiptParseResult? candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            int currentRank = Enum.TryParse<SingleWindowReceiptBusinessStatus>(
                batch.LastBusinessStatus,
                ignoreCase: true,
                out var currentStatus)
                ? GetStatusRank(currentStatus)
                : 0;
            int candidateRank = GetStatusRank(candidate.BusinessStatus);
            if (IsTerminalStatus(currentStatus))
            {
                if (candidate.BusinessStatus != currentStatus)
                {
                    return false;
                }

                return !batch.LastReceiptAt.HasValue ||
                       !candidate.OccurredAt.HasValue ||
                       candidate.OccurredAt.Value >= batch.LastReceiptAt.Value;
            }

            if (candidateRank != currentRank)
            {
                return candidateRank > currentRank;
            }

            return !batch.LastReceiptAt.HasValue ||
                   !candidate.OccurredAt.HasValue ||
                   candidate.OccurredAt.Value >= batch.LastReceiptAt.Value;
        }

        private static string ResolveMonotonicBatchStatus(
            SwSubmissionBatch batch,
            SingleWindowReceiptParseResult? candidate)
        {
            if (candidate is null || !ShouldUpdateReceiptSummary(batch, candidate))
            {
                return batch.Status;
            }

            return MapReceiptStatus(candidate.BusinessStatus);
        }

        private static string ResolveBatchStatus(IReadOnlyList<SingleWindowReceiptParseResult> parsedReceipts)
        {
            if (parsedReceipts == null || parsedReceipts.Count == 0)
            {
                return SingleWindowBatchStatusCatalog.ReceiptImported;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.Approved))
            {
                return SingleWindowBatchStatusCatalog.Approved;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.Rejected))
            {
                return SingleWindowBatchStatusCatalog.Rejected;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.PendingReview))
            {
                return SingleWindowBatchStatusCatalog.PendingReview;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.Failed))
            {
                return SingleWindowBatchStatusCatalog.Failed;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.Accepted))
            {
                return SingleWindowBatchStatusCatalog.Accepted;
            }

            if (parsedReceipts.Any(item => item.BusinessStatus == SingleWindowReceiptBusinessStatus.Received))
            {
                return SingleWindowBatchStatusCatalog.Received;
            }

            return SingleWindowBatchStatusCatalog.ReceiptImported;
        }

        private static int GetStatusRank(SingleWindowReceiptBusinessStatus businessStatus)
        {
            return businessStatus switch
            {
                SingleWindowReceiptBusinessStatus.Approved => 5,
                SingleWindowReceiptBusinessStatus.Rejected => 5,
                SingleWindowReceiptBusinessStatus.Failed => 4,
                SingleWindowReceiptBusinessStatus.PendingReview => 3,
                SingleWindowReceiptBusinessStatus.Accepted => 2,
                SingleWindowReceiptBusinessStatus.Received => 1,
                _ => 0
            };
        }

        private static bool IsTerminalStatus(SingleWindowReceiptBusinessStatus businessStatus)
        {
            return businessStatus is SingleWindowReceiptBusinessStatus.Approved or
                SingleWindowReceiptBusinessStatus.Rejected;
        }

        private static string MapReceiptStatus(SingleWindowReceiptBusinessStatus status)
        {
            return status switch
            {
                SingleWindowReceiptBusinessStatus.Approved => SingleWindowBatchStatusCatalog.Approved,
                SingleWindowReceiptBusinessStatus.Rejected => SingleWindowBatchStatusCatalog.Rejected,
                SingleWindowReceiptBusinessStatus.Failed => SingleWindowBatchStatusCatalog.Failed,
                SingleWindowReceiptBusinessStatus.PendingReview => SingleWindowBatchStatusCatalog.PendingReview,
                SingleWindowReceiptBusinessStatus.Accepted => SingleWindowBatchStatusCatalog.Accepted,
                SingleWindowReceiptBusinessStatus.Received => SingleWindowBatchStatusCatalog.Received,
                _ => SingleWindowBatchStatusCatalog.ReceiptImported
            };
        }

        private static async Task ApplyReceiptWriteBackAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            SingleWindowReceiptParseResult? primaryReceipt,
            string batchStatus,
            CancellationToken cancellationToken)
        {
            if (primaryReceipt == null || batch.SourceDocumentId <= 0 || string.IsNullOrWhiteSpace(batch.SourceDocumentType))
            {
                return;
            }

            if (string.Equals(batch.SourceDocumentType, nameof(AgentConsignmentDocument), StringComparison.Ordinal))
            {
                var document = await context.AgentConsignmentDocuments
                    .FirstOrDefaultAsync(item => item.Id == batch.SourceDocumentId, cancellationToken);
                if (document == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(primaryReceipt.ReferenceNo))
                {
                    document.ConsignNo = primaryReceipt.ReferenceNo;
                }

                document.CounterpartyStatus = primaryReceipt.BusinessStatus.ToString();
                document.Status = batchStatus;
            }
            else if (string.Equals(batch.SourceDocumentType, nameof(CustomsCooDocument), StringComparison.Ordinal))
            {
                var document = await context.CustomsCooDocuments
                    .FirstOrDefaultAsync(item => item.Id == batch.SourceDocumentId, cancellationToken);
                if (document == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(primaryReceipt.ReferenceNo))
                {
                    document.CertNo = primaryReceipt.ReferenceNo;
                }

                document.Status = batchStatus;
            }
        }

        private sealed class ReceiptImportEntry
        {
            public ReceiptImportEntry(
                SingleWindowReceiptParseResult receipt,
                string contentSha256,
                string rawContent)
            {
                Receipt = receipt;
                ContentSha256 = contentSha256 ?? string.Empty;
                RawContent = rawContent ?? string.Empty;
            }

            public SingleWindowReceiptParseResult Receipt { get; }

            public string ContentSha256 { get; }

            public string RawContent { get; }
        }

    }
}
