using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService
    {
        public async Task<int> RecordSubmitPackageExportAsync(
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

                    string batchReference = NormalizeBatchReference(manifest.BatchReference);
                    int submissionVersion = manifest.SubmissionVersion > 0
                        ? manifest.SubmissionVersion
                        : await ResolveNextSubmissionVersionCoreAsync(
                            context,
                            manifest.BusinessType,
                            manifest.SourceInvoiceId,
                            manifest.SourceDocumentId,
                            cancellationToken);

                    var batch = new SwSubmissionBatch
                    {
                        BatchReference = batchReference,
                        BusinessType = manifest.BusinessType.ToString(),
                        SourceInvoiceId = manifest.SourceInvoiceId,
                        SourceDocumentType = manifest.SourceDocumentType ?? string.Empty,
                        SourceDocumentId = manifest.SourceDocumentId,
                        SubmissionVersion = submissionVersion,
                        DraftRevision = manifest.DraftRevision,
                        InvoiceNo = manifest.InvoiceNo ?? string.Empty,
                        ContractNo = manifest.ContractNo ?? string.Empty,
                        Status = SingleWindowBatchStatusCatalog.SubmitPackageExported,
                        PayloadFileCount = manifest.PayloadFiles.Count,
                        AttachmentFileCount = manifest.AttachmentFiles.Count,
                        WarningCount = manifest.Warnings.Count,
                        SourceBaselineHash = manifest.SourceBaselineHash ?? string.Empty,
                        SubmitPackagePath = packagePath ?? string.Empty,
                        CreatedOnMachine = manifest.CreatedOnMachine ?? string.Empty,
                        CreatedAt = manifest.CreatedAt,
                        UpdatedAt = DateTime.Now
                    };

                    await context.SwSubmissionBatches.AddAsync(batch, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        manifest,
                        direction: "Exported"));
                    await UpsertOperationTicketAsync(
                        context,
                        batch,
                        SingleWindowCollaborationStatusCatalog.Pending,
                        cancellationToken);
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

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    await EnsureCanAccessManifestAsync(context, imported.Manifest, cancellationToken);

                    var batch = await FindOrCreateBatchAsync(
                        context,
                        imported.Manifest,
                        cancellationToken);

                    batch.Status = SingleWindowBatchStatusCatalog.SubmitPackageImported;
                    batch.UpdatedAt = DateTime.Now;
                    batch.SubmissionVersion = batch.SubmissionVersion > 0 ? batch.SubmissionVersion : imported.Manifest.SubmissionVersion;
                    batch.DraftRevision = Math.Max(batch.DraftRevision, imported.Manifest.DraftRevision);
                    batch.SourceBaselineHash = string.IsNullOrWhiteSpace(batch.SourceBaselineHash)
                        ? imported.Manifest.SourceBaselineHash ?? string.Empty
                        : batch.SourceBaselineHash;
                    if (string.IsNullOrWhiteSpace(batch.SubmitPackagePath))
                    {
                        batch.SubmitPackagePath = packagePath ?? string.Empty;
                    }

                    batch.WorkingDirectoryPath = imported.WorkingDirectory ?? string.Empty;

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        imported.Manifest,
                        direction: "Imported"));

                    await UpsertOperationTicketAsync(
                        context,
                        batch,
                        SingleWindowCollaborationStatusCatalog.Pending,
                        cancellationToken);
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

                    var batch = await FindOrCreateBatchAsync(
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

                    await UpsertOperationTicketAsync(
                        context,
                        batch,
                        MapBatchStatusToTicketStatus(batch.Status),
                        cancellationToken);
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

                    var batch = await FindOrCreateBatchAsync(context, manifest, cancellationToken);
                    var normalizedReceiptEntries = BuildReceiptImportEntries(receiptEntries);
                    var existingReceiptKeys = await LoadExistingReceiptKeysAsync(
                        context,
                        batch.Id,
                        normalizedReceiptEntries,
                        cancellationToken);
                    int savedReceiptCount = 0;

                    foreach (var entry in normalizedReceiptEntries)
                    {
                        if (!existingReceiptKeys.Add(entry.Identity))
                        {
                            continue;
                        }

                        var receipt = entry.Receipt;
                        context.SwReceiptLogs.Add(new SwReceiptLog
                        {
                            BatchId = batch.Id,
                            BusinessType = receipt.BusinessType.ToString(),
                            ReceiptKind = receipt.ReceiptKind.ToString(),
                            ReferenceNo = entry.Identity.ReferenceNo,
                            ReceiptCode = entry.Identity.ReceiptCode,
                            ReceiptMessage = receipt.ReceiptMessage ?? string.Empty,
                            BusinessStatus = receipt.BusinessStatus.ToString(),
                            SourceFileName = entry.Identity.SourceFileName,
                            ImportedAt = DateTime.Now,
                            OccurredAt = receipt.OccurredAt,
                            RawContent = entry.RawContent
                        });
                        savedReceiptCount++;
                    }

                    var normalizedReceipts = normalizedReceiptEntries
                        .Select(item => item.Receipt)
                        .ToList();
                    string status = ResolveBatchStatus(normalizedReceipts);
                    var primaryReceipt = SelectPrimaryReceipt(normalizedReceipts);
                    batch.Status = status;
                    batch.LastBusinessStatus = primaryReceipt?.BusinessStatus.ToString() ?? string.Empty;
                    batch.ReferenceNo = primaryReceipt?.ReferenceNo ?? batch.ReferenceNo;
                    batch.LastReceiptKind = primaryReceipt?.ReceiptKind.ToString() ?? string.Empty;
                    batch.LastReceiptCode = primaryReceipt?.ReceiptCode ?? string.Empty;
                    batch.LastReceiptMessage = primaryReceipt?.ReceiptMessage ?? string.Empty;
                    batch.LastReceiptAt = normalizedReceipts
                        .Where(item => item?.OccurredAt != null)
                        .Select(item => item.OccurredAt)
                        .Max();
                    batch.LastReceiptPackagePath = packagePath ?? string.Empty;
                    batch.UpdatedAt = DateTime.Now;

                    context.SwHandoffPackageRecords.Add(BuildPackageRecord(
                        batchId: batch.Id,
                        packagePath,
                        manifest,
                        direction: "Imported"));

                    await ApplyReceiptWriteBackAsync(context, batch, primaryReceipt, status, cancellationToken);
                    await UpsertOperationTicketAsync(
                        context,
                        batch,
                        MapBatchStatusToTicketStatus(status),
                        cancellationToken,
                        primaryReceipt?.ReceiptMessage ?? batch.LastReceiptMessage);

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
                throw new UnauthorizedAccessException("无权限写入该发票的单一窗口跟踪记录。");
            }
        }
    }
}
