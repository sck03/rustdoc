using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowDocumentPersistenceService
    {
        private async Task<CustomsCooDocument> LoadCustomsCooDocumentAsync(
            int invoiceId,
            bool includeExistingDocument,
            CancellationToken cancellationToken)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var source = await LoadEditorSourceContextAsync(context, invoiceId, cancellationToken).ConfigureAwait(false);

            var document = includeExistingDocument
                ? await context.CustomsCooDocuments
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(item => item.Items)
                    .Include(item => item.NonpartyCorps)
                    .Include(item => item.Attachments)
                    .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoiceId, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            return CreateCustomsCooEditorDocument(
                invoiceId,
                source.Invoice,
                source.InvoiceItems,
                source.Customer,
                source.Exporter,
                document);
        }

        async Task<int> ICustomsCooDocumentService.SaveAsync(CustomsCooDocument document, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);

            var saveResult = await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    var source = await LoadEditorSourceContextAsync(context, document.SourceInvoiceId, cancellationToken).ConfigureAwait(false);

                    var defaultsDocument = CreateCustomsCooEditorDocument(
                        document.SourceInvoiceId,
                        source.Invoice,
                        source.InvoiceItems,
                        source.Customer,
                        source.Exporter,
                        null);
                    var lockedFields = SingleWindowDraftStateHelper.BuildCustomsCooLockedFields(document, defaultsDocument);
                    string baselineJson = SingleWindowDraftStateHelper.BuildCustomsCooSourceBaselineJson(defaultsDocument);

                    var entity = await context.CustomsCooDocuments
                        .FirstOrDefaultAsync(item => item.SourceInvoiceId == document.SourceInvoiceId, cancellationToken)
                        .ConfigureAwait(false);

                    entity ??= new CustomsCooDocument
                    {
                        SourceInvoiceId = document.SourceInvoiceId
                    };

                    entity.InvoiceNo = source.Invoice.InvoiceNo ?? string.Empty;
                    entity.ContractNo = source.Invoice.ContractNo ?? string.Empty;
                    entity.Status = SingleWindowDraftMetadataHelper.ResolveStatusForSave(document.Status);
                    ApplyEditableCustomsCooDocumentValues(entity, document);
                    entity.AplPromiseCode = string.IsNullOrWhiteSpace(document.AplPromiseCode) ? "1" : NormalizePersistedValue(document.AplPromiseCode);
                    entity.WarningSummary = NormalizePersistedValue(document.WarningSummary);
                    entity.WarningCount = SingleWindowDraftMetadataHelper.CountWarnings(entity.WarningSummary);
                    entity.DraftRevision = entity.Id <= 0 ? 1 : Math.Max(1, entity.DraftRevision + 1);
                    entity.ManualLockedFieldsJson = SingleWindowDraftStateHelper.SerializeLockedFields(lockedFields);
                    entity.SourceBaselineJson = baselineJson;
                    entity.SourceBaselineHash = SingleWindowDraftStateHelper.ComputeBaselineHash(baselineJson);
                    entity.LastGeneratedAt = DateTime.Now;

                    bool isNew = entity.Id <= 0;
                    if (isNew)
                    {
                        await context.CustomsCooDocuments.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await DeletePersistedCustomsCooChildrenAsync(context, entity.Id, cancellationToken).ConfigureAwait(false);
                    }

                    var items = BuildPersistedCustomsCooItems(document.Items, entity.Id);
                    if (items.Count > 0)
                    {
                        await context.CustomsCooItems.AddRangeAsync(items, cancellationToken).ConfigureAwait(false);
                    }

                    var nonpartyCorps = BuildPersistedCustomsCooNonpartyCorps(document.NonpartyCorps, entity.Id);
                    if (nonpartyCorps.Count > 0)
                    {
                        await context.CustomsCooNonpartyCorps.AddRangeAsync(nonpartyCorps, cancellationToken).ConfigureAwait(false);
                    }

                    var attachments = BuildPersistedCustomsCooAttachments(document.Attachments, entity.Id);
                    if (attachments.Count > 0)
                    {
                        await context.CustomsCooAttachments.AddRangeAsync(attachments, cancellationToken).ConfigureAwait(false);
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return (
                        DocumentId: entity.Id,
                        SourceInvoiceNo: source.Invoice.InvoiceNo,
                        SourceContractNo: source.Invoice.ContractNo);
                },
                cancellationToken).ConfigureAwait(false);

            await RememberCustomsCooDefaultsAsync(document).ConfigureAwait(false);
            await RememberCustomsCooProducerProfilesAsync(
                document,
                saveResult.SourceInvoiceNo,
                saveResult.SourceContractNo,
                cancellationToken).ConfigureAwait(false);
            return saveResult.DocumentId;
        }

        public async Task<int> UpsertCustomsCooDocumentAsync(
            CooSourceSnapshot snapshot,
            CooMappedDocument document,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(document);

            var invoice = snapshot.Invoice ?? throw new InvalidOperationException("海关原产地证来源发票不能为空。");

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    if (!await _businessDataAccessScope.CanAccessInvoiceAsync(
                            context,
                            invoice.Id,
                            cancellationToken).ConfigureAwait(false))
                    {
                        throw new UnauthorizedAccessException("无权限生成该发票的海关原产地证草稿。");
                    }

                    var entity = await context.CustomsCooDocuments
                        .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoice.Id, cancellationToken)
                        .ConfigureAwait(false);

                    bool isNew = entity == null;
                    entity ??= new CustomsCooDocument
                    {
                        SourceInvoiceId = invoice.Id
                    };

                    entity.InvoiceNo = invoice.InvoiceNo ?? string.Empty;
                    entity.ContractNo = invoice.ContractNo ?? string.Empty;
                    entity.Status = "Generated";
                    ApplyMappedCustomsCooDocumentValues(entity, document, preserveExistingCertNoWhenEmpty: true);
                    if (entity.DraftRevision <= 0)
                    {
                        entity.DraftRevision = 1;
                    }

                    entity.ManualLockedFieldsJson ??= string.Empty;
                    EnsureGeneratedCustomsCooBaseline(entity, invoice, snapshot.Items, document);
                    if (string.IsNullOrWhiteSpace(entity.SourceBaselineHash))
                    {
                        entity.SourceBaselineHash = SingleWindowDraftStateHelper.ComputeBaselineHash(entity.SourceBaselineJson);
                    }

                    entity.LastGeneratedAt = DateTime.Now;

                    if (isNew)
                    {
                        await context.CustomsCooDocuments.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await DeletePersistedCustomsCooChildrenAsync(context, entity.Id, cancellationToken).ConfigureAwait(false);
                    }

                    var items = BuildPersistedCustomsCooItems(snapshot.Items, document.Goods, entity.Id);
                    if (items.Count > 0)
                    {
                        await context.CustomsCooItems.AddRangeAsync(items, cancellationToken).ConfigureAwait(false);
                    }

                    var nonpartyCorps = BuildPersistedCustomsCooNonpartyCorps(document.NonpartyCorps, entity.Id);
                    if (nonpartyCorps.Count > 0)
                    {
                        await context.CustomsCooNonpartyCorps.AddRangeAsync(nonpartyCorps, cancellationToken).ConfigureAwait(false);
                    }

                    var attachments = BuildPersistedCustomsCooAttachments(document.Attachments, entity.Id);
                    if (attachments.Count > 0)
                    {
                        await context.CustomsCooAttachments.AddRangeAsync(attachments, cancellationToken).ConfigureAwait(false);
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return entity.Id;
                },
                cancellationToken).ConfigureAwait(false);
        }

        private CustomsCooDocument CreateCustomsCooEditorDocument(
            int invoiceId,
            Invoice invoice,
            IReadOnlyList<Item> invoiceItems,
            Customer customer,
            Exporter exporter,
            CustomsCooDocument existingDocument)
        {
            var attachmentSources = SingleWindowSourceCloneHelper.CloneAttachmentSources(existingDocument?.Attachments);
            var rawExistingDocument = SingleWindowSourceCloneHelper.CloneCustomsCooDocument(existingDocument);

            var sourceSnapshot = new CooSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(invoiceItems),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = null,
                Attachments = []
            };
            var sourceMapped = _customsCooFieldMapper.Map(sourceSnapshot);
            ApplyStoredCustomsCooDefaults(sourceMapped);
            var sourceDefaultsDocument = CreateCustomsCooDocumentFromMapped(
                invoiceId,
                invoice,
                invoiceItems,
                null,
                [],
                sourceMapped);

            var snapshot = new CooSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(invoiceItems),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = SingleWindowDraftStateHelper.BuildCustomsCooLockedOverlay(existingDocument, invoiceItems),
                Attachments = attachmentSources
            };
            var mapped = _customsCooFieldMapper.Map(snapshot);
            ApplyStoredCustomsCooDefaults(mapped);
            var result = CreateCustomsCooDocumentFromMapped(
                invoiceId,
                invoice,
                invoiceItems,
                rawExistingDocument,
                attachmentSources,
                mapped);

            var diff = SingleWindowDraftStateHelper.BuildCustomsCooSourceDiff(
                rawExistingDocument?.SourceBaselineJson,
                sourceDefaultsDocument);
            result.SourceDiffCount = diff.Count;
            result.SourceDiffSummary = diff.Summary;
            result.ManualLockedFieldCount = SingleWindowDraftStateHelper.BuildCustomsCooLockedFields(result, sourceDefaultsDocument).Count;

            return result;
        }
    }
}
