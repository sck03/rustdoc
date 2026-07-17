using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService
    {
        private static async Task<SwSubmissionBatch> FindOrCreateBatchAsync(
            AppDbContext context,
            SingleWindowPackageManifest manifest,
            CancellationToken cancellationToken)
        {
            string batchReference = NormalizeBatchReference(manifest.BatchReference);
            var batch = await context.SwSubmissionBatches
                .FirstOrDefaultAsync(item => item.BatchReference == batchReference, cancellationToken);

            if (batch == null && manifest.SourceInvoiceId > 0)
            {
                batch = await context.SwSubmissionBatches
                    .Where(item =>
                        item.SourceInvoiceId == manifest.SourceInvoiceId &&
                        item.BusinessType == manifest.BusinessType.ToString() &&
                        (manifest.SubmissionVersion <= 0 || item.SubmissionVersion == manifest.SubmissionVersion))
                    .OrderByDescending(item => item.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (batch == null && !string.IsNullOrWhiteSpace(manifest.InvoiceNo))
            {
                batch = await context.SwSubmissionBatches
                    .Where(item =>
                        item.InvoiceNo == manifest.InvoiceNo &&
                        item.BusinessType == manifest.BusinessType.ToString() &&
                        (manifest.SubmissionVersion <= 0 || item.SubmissionVersion == manifest.SubmissionVersion))
                    .OrderByDescending(item => item.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (batch != null)
            {
                if (string.IsNullOrWhiteSpace(batch.BatchReference))
                {
                    batch.BatchReference = batchReference;
                }

                if (string.IsNullOrWhiteSpace(batch.SourceDocumentType) && !string.IsNullOrWhiteSpace(manifest.SourceDocumentType))
                {
                    batch.SourceDocumentType = manifest.SourceDocumentType;
                }

                if (batch.SourceDocumentId <= 0 && manifest.SourceDocumentId > 0)
                {
                    batch.SourceDocumentId = manifest.SourceDocumentId;
                }

                if (batch.SubmissionVersion <= 0 && manifest.SubmissionVersion > 0)
                {
                    batch.SubmissionVersion = manifest.SubmissionVersion;
                }

                if (batch.DraftRevision < manifest.DraftRevision)
                {
                    batch.DraftRevision = manifest.DraftRevision;
                }

                if (string.IsNullOrWhiteSpace(batch.SourceBaselineHash) && !string.IsNullOrWhiteSpace(manifest.SourceBaselineHash))
                {
                    batch.SourceBaselineHash = manifest.SourceBaselineHash;
                }

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
                Status = manifest.PackageType == SingleWindowPackageType.SubmitPackage
                    ? SingleWindowBatchStatusCatalog.SubmitPackageImported
                    : SingleWindowBatchStatusCatalog.ReceiptImported,
                PayloadFileCount = manifest.PayloadFiles.Count,
                AttachmentFileCount = manifest.AttachmentFiles.Count,
                WarningCount = manifest.Warnings.Count,
                SourceBaselineHash = manifest.SourceBaselineHash ?? string.Empty,
                CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty,
                CreatedAt = manifest.CreatedAt,
                UpdatedAt = DateTime.Now
            };

            await context.SwSubmissionBatches.AddAsync(batch, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return batch;
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
                if (receipt == null)
                {
                    continue;
                }

                entries.Add(new ReceiptImportEntry(
                    receipt,
                    CreateReceiptLogIdentity(
                        receipt.SourceFileName,
                        receipt.ReferenceNo,
                        receipt.ReceiptCode),
                    entry.RawContent));
            }

            return entries;
        }

        private static async Task<HashSet<ReceiptLogIdentity>> LoadExistingReceiptKeysAsync(
            AppDbContext context,
            int batchId,
            IReadOnlyList<ReceiptImportEntry> receiptEntries,
            CancellationToken cancellationToken)
        {
            if (receiptEntries == null || receiptEntries.Count == 0)
            {
                return [];
            }

            var sourceFileNames = receiptEntries
                .Select(item => item.Identity.SourceFileName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existingLogs = context.SwReceiptLogs
                .AsNoTracking()
                .Where(log => log.BatchId == batchId);

            if (sourceFileNames.Count > 0)
            {
                existingLogs = existingLogs.Where(log => sourceFileNames.Contains(log.SourceFileName));
            }

            return (await existingLogs
                    .Select(log => new
                    {
                        log.SourceFileName,
                        log.ReferenceNo,
                        log.ReceiptCode
                    })
                    .ToListAsync(cancellationToken))
                .Select(item => CreateReceiptLogIdentity(item.SourceFileName, item.ReferenceNo, item.ReceiptCode))
                .ToHashSet();
        }

        private static ReceiptLogIdentity CreateReceiptLogIdentity(
            string sourceFileName,
            string referenceNo,
            string receiptCode)
        {
            return new ReceiptLogIdentity(
                NormalizeReceiptKeyPart(sourceFileName),
                NormalizeReceiptKeyPart(referenceNo),
                NormalizeReceiptKeyPart(receiptCode));
        }

        private static string NormalizeReceiptKeyPart(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static SwHandoffPackageRecord BuildPackageRecord(
            int batchId,
            string packagePath,
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
                PackageType = manifest.PackageType.ToString(),
                Direction = direction,
                FilePath = packagePath ?? string.Empty,
                CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty,
                PayloadFileCount = manifest.PayloadFiles.Count,
                AttachmentFileCount = manifest.AttachmentFiles.Count,
                WarningCount = manifest.Warnings.Count,
                CreatedAt = DateTime.Now,
                ManifestJson = JsonSerializer.Serialize(manifest, JsonOptions)
            };
        }

        private static string NormalizeBatchReference(string batchReference)
        {
            return string.IsNullOrWhiteSpace(batchReference)
                ? $"SW-{Guid.NewGuid():N}".ToUpperInvariant()
                : batchReference.Trim();
        }

        private static SingleWindowReceiptParseResult SelectPrimaryReceipt(IReadOnlyList<SingleWindowReceiptParseResult> parsedReceipts)
        {
            return parsedReceipts
                .Where(item => item != null)
                .OrderByDescending(item => GetStatusRank(item.BusinessStatus))
                .ThenByDescending(item => item.OccurredAt ?? DateTime.MinValue)
                .FirstOrDefault();
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
                SingleWindowReceiptBusinessStatus.Approved => 6,
                SingleWindowReceiptBusinessStatus.Rejected => 5,
                SingleWindowReceiptBusinessStatus.PendingReview => 4,
                SingleWindowReceiptBusinessStatus.Failed => 3,
                SingleWindowReceiptBusinessStatus.Accepted => 2,
                SingleWindowReceiptBusinessStatus.Received => 1,
                _ => 0
            };
        }

        private static async Task ApplyReceiptWriteBackAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            SingleWindowReceiptParseResult primaryReceipt,
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

        private async Task UpsertOperationTicketAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            string targetStatus,
            CancellationToken cancellationToken,
            string lastError = "")
        {
            if (batch == null)
            {
                return;
            }

            string mappedBusinessType = MapBusinessType(batch.BusinessType);
            var ticket = await context.SwOperationTickets
                .FirstOrDefaultAsync(
                    item => item.BatchId == batch.Id ||
                            (item.SourceInvoiceId == batch.SourceInvoiceId &&
                             item.DocumentId == batch.SourceDocumentId &&
                             item.BusinessType == mappedBusinessType),
                    cancellationToken);

            ticket ??= new SwOperationTicket
            {
                BusinessType = mappedBusinessType,
                SourceInvoiceId = batch.SourceInvoiceId,
                DocumentId = batch.SourceDocumentId,
                RequestedAt = batch.CreatedAt == default ? DateTime.Now : batch.CreatedAt,
                RequestedBy = ResolveCurrentOperatorName(batch.CreatedOnMachine)
            };

            ticket.BusinessType = mappedBusinessType;
            ticket.SourceInvoiceId = batch.SourceInvoiceId;
            ticket.DocumentId = batch.SourceDocumentId;
            ticket.BatchId = batch.Id;
            ticket.Status = SingleWindowCollaborationStatusCatalog.Normalize(targetStatus);

            string currentOperator = ResolveCurrentOperatorName(batch.CreatedOnMachine);
            if (!string.IsNullOrWhiteSpace(currentOperator) &&
                string.IsNullOrWhiteSpace(ticket.AssignedOperator) &&
                ticket.Status != SingleWindowCollaborationStatusCatalog.Pending)
            {
                ticket.AssignedOperator = currentOperator;
            }

            if (ticket.Status == SingleWindowCollaborationStatusCatalog.Submitted ||
                ticket.Status == SingleWindowCollaborationStatusCatalog.Completed ||
                ticket.Status == SingleWindowCollaborationStatusCatalog.Failed)
            {
                ticket.SubmittedAt ??= DateTime.Now;
                if (!string.IsNullOrWhiteSpace(currentOperator))
                {
                    ticket.AssignedOperator = currentOperator;
                }
            }

            if (ticket.Status == SingleWindowCollaborationStatusCatalog.Completed ||
                ticket.Status == SingleWindowCollaborationStatusCatalog.Failed)
            {
                ticket.CompletedAt ??= DateTime.Now;
            }

            ticket.LastError = ticket.Status == SingleWindowCollaborationStatusCatalog.Failed
                ? (lastError ?? string.Empty).Trim()
                : string.Empty;

            if (ticket.Id <= 0)
            {
                await context.SwOperationTickets.AddAsync(ticket, cancellationToken);
            }
        }

        private static string MapBatchStatusToTicketStatus(string batchStatus)
        {
            return SingleWindowBatchStatusCatalog.Normalize(batchStatus) switch
            {
                SingleWindowBatchStatusCatalog.Approved => SingleWindowCollaborationStatusCatalog.Completed,
                SingleWindowBatchStatusCatalog.Rejected => SingleWindowCollaborationStatusCatalog.Failed,
                SingleWindowBatchStatusCatalog.Failed => SingleWindowCollaborationStatusCatalog.Failed,
                SingleWindowBatchStatusCatalog.Accepted => SingleWindowCollaborationStatusCatalog.Submitted,
                SingleWindowBatchStatusCatalog.PendingReview => SingleWindowCollaborationStatusCatalog.Submitted,
                SingleWindowBatchStatusCatalog.Received => SingleWindowCollaborationStatusCatalog.Submitted,
                SingleWindowBatchStatusCatalog.ReceiptImported => SingleWindowCollaborationStatusCatalog.Submitted,
                SingleWindowBatchStatusCatalog.ReceiptPackageExported => SingleWindowCollaborationStatusCatalog.Submitted,
                _ => SingleWindowCollaborationStatusCatalog.Pending
            };
        }

        private static string MapBusinessType(string businessType)
        {
            return businessType switch
            {
                nameof(SingleWindowBusinessType.CustomsCoo) => "海关原产地证",
                nameof(SingleWindowBusinessType.AgentConsignment) => "报关代理委托",
                _ => businessType ?? string.Empty
            };
        }

        private string ResolveCurrentOperatorName(string fallbackMachineName)
        {
            var currentUser = _businessDataAccessScope.CurrentUser;
            return (currentUser?.FullName ??
                    currentUser?.Username ??
                    fallbackMachineName ??
                    Environment.MachineName ??
                    string.Empty).Trim();
        }

        private sealed class ReceiptImportEntry
        {
            public ReceiptImportEntry(
                SingleWindowReceiptParseResult receipt,
                ReceiptLogIdentity identity,
                string rawContent)
            {
                Receipt = receipt;
                Identity = identity;
                RawContent = rawContent ?? string.Empty;
            }

            public SingleWindowReceiptParseResult Receipt { get; }

            public ReceiptLogIdentity Identity { get; }

            public string RawContent { get; }
        }

        private readonly record struct ReceiptLogIdentity(
            string SourceFileName,
            string ReferenceNo,
            string ReceiptCode);
    }
}
