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
        private async Task<AgentConsignmentDocument> LoadAgentConsignmentDocumentAsync(
            int invoiceId,
            bool includeExistingDocument,
            CancellationToken cancellationToken)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var source = await LoadEditorSourceContextAsync(context, invoiceId, cancellationToken).ConfigureAwait(false);

            var document = includeExistingDocument
                ? await context.AgentConsignmentDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoiceId, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            return CreateAgentConsignmentEditorDocument(
                invoiceId,
                source.Invoice,
                source.InvoiceItems,
                source.Customer,
                source.Exporter,
                document);
        }

        async Task<int> IAgentConsignmentDocumentService.SaveAsync(AgentConsignmentDocument document, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    var source = await LoadEditorSourceContextAsync(context, document.SourceInvoiceId, cancellationToken).ConfigureAwait(false);

                    var defaultsDocument = CreateAgentConsignmentEditorDocument(
                        document.SourceInvoiceId,
                        source.Invoice,
                        source.InvoiceItems,
                        source.Customer,
                        source.Exporter,
                        null);
                    var lockedFields = SingleWindowDraftStateHelper.BuildAgentConsignmentLockedFields(document, defaultsDocument);
                    string baselineJson = SingleWindowDraftStateHelper.BuildAgentConsignmentSourceBaselineJson(defaultsDocument);

                    var entity = await context.AgentConsignmentDocuments
                        .FirstOrDefaultAsync(item => item.SourceInvoiceId == document.SourceInvoiceId, cancellationToken)
                        .ConfigureAwait(false);

                    entity ??= new AgentConsignmentDocument
                    {
                        SourceInvoiceId = document.SourceInvoiceId
                    };

                    entity.InvoiceNo = source.Invoice.InvoiceNo ?? string.Empty;
                    entity.ContractNo = source.Invoice.ContractNo ?? string.Empty;
                    entity.Status = SingleWindowDraftMetadataHelper.ResolveStatusForSave(document.Status);
                    entity.CopCusCode = document.CopCusCode?.Trim() ?? string.Empty;
                    entity.Sign = document.Sign?.Trim() ?? string.Empty;
                    entity.OperType = string.IsNullOrWhiteSpace(document.OperType) ? "1" : document.OperType.Trim();
                    entity.GName = document.GName?.Trim() ?? string.Empty;
                    entity.CodeTS = document.CodeTS?.Trim() ?? string.Empty;
                    entity.DeclTotal = document.DeclTotal?.Trim() ?? string.Empty;
                    entity.IEDate = document.IEDate?.Trim() ?? string.Empty;
                    entity.ListNo = document.ListNo?.Trim() ?? string.Empty;
                    entity.TradeMode = document.TradeMode?.Trim() ?? string.Empty;
                    entity.OriCountry = document.OriCountry?.Trim() ?? string.Empty;
                    entity.TradeCode = document.TradeCode?.Trim() ?? string.Empty;
                    entity.AgentCode = document.AgentCode?.Trim() ?? string.Empty;
                    entity.Curr = document.Curr?.Trim() ?? string.Empty;
                    entity.QtyOrWeight = document.QtyOrWeight?.Trim() ?? string.Empty;
                    entity.PackingCondition = document.PackingCondition?.Trim() ?? string.Empty;
                    entity.OtherNote = document.OtherNote?.Trim() ?? string.Empty;
                    entity.ConsignTele = document.ConsignTele?.Trim() ?? string.Empty;
                    entity.EntryId = document.EntryId?.Trim() ?? string.Empty;
                    entity.ReceiveDate = document.ReceiveDate?.Trim() ?? string.Empty;
                    entity.PaperInfo = document.PaperInfo?.Trim() ?? string.Empty;
                    entity.OtherRecInfo = document.OtherRecInfo?.Trim() ?? string.Empty;
                    entity.DeclarePrice = document.DeclarePrice?.Trim() ?? string.Empty;
                    entity.PromiseNote = document.PromiseNote?.Trim() ?? string.Empty;
                    entity.DeclTele = document.DeclTele?.Trim() ?? string.Empty;
                    entity.WarningSummary = document.WarningSummary?.Trim() ?? string.Empty;
                    entity.WarningCount = SingleWindowDraftMetadataHelper.CountWarnings(entity.WarningSummary);
                    entity.DraftRevision = entity.Id <= 0 ? 1 : Math.Max(1, entity.DraftRevision + 1);
                    entity.ManualLockedFieldsJson = SingleWindowDraftStateHelper.SerializeLockedFields(lockedFields);
                    entity.SourceBaselineJson = baselineJson;
                    entity.SourceBaselineHash = SingleWindowDraftStateHelper.ComputeBaselineHash(baselineJson);
                    entity.LastGeneratedAt = DateTime.Now;

                    if (entity.Id <= 0)
                    {
                        await context.AgentConsignmentDocuments.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return entity.Id;
                },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpsertAgentConsignmentDocumentAsync(
            AcdSourceSnapshot snapshot,
            AcdMappedDocument document,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(document);

            var invoice = snapshot.Invoice ?? throw new InvalidOperationException("报关代理委托来源发票不能为空。");

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    if (!await _businessDataAccessScope.CanAccessInvoiceAsync(
                            context,
                            invoice.Id,
                            cancellationToken).ConfigureAwait(false))
                    {
                        throw new UnauthorizedAccessException("无权限生成该发票的报关代理委托草稿。");
                    }

                    var entity = await context.AgentConsignmentDocuments
                        .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoice.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (entity == null)
                    {
                        entity = new AgentConsignmentDocument
                        {
                            SourceInvoiceId = invoice.Id
                        };
                        await context.AgentConsignmentDocuments.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                    }

                    entity.InvoiceNo = invoice.InvoiceNo ?? string.Empty;
                    entity.ContractNo = invoice.ContractNo ?? string.Empty;
                    entity.Status = "Generated";
                    entity.CopCusCode = document.CopCusCode;
                    entity.Sign = document.Sign;
                    entity.OperType = document.OperType;
                    entity.GName = document.GName;
                    entity.CodeTS = document.CodeTS;
                    entity.DeclTotal = document.DeclTotal;
                    entity.IEDate = document.IEDate;
                    entity.ListNo = document.ListNo;
                    entity.TradeMode = document.TradeMode;
                    entity.OriCountry = document.OriCountry;
                    entity.TradeCode = document.TradeCode;
                    entity.AgentCode = document.AgentCode;
                    entity.Curr = document.Curr;
                    entity.QtyOrWeight = document.QtyOrWeight;
                    entity.PackingCondition = document.PackingCondition;
                    entity.OtherNote = document.OtherNote;
                    entity.ConsignTele = document.ConsignTele;
                    entity.EntryId = document.EntryId;
                    entity.ReceiveDate = document.ReceiveDate;
                    entity.PaperInfo = document.PaperInfo;
                    entity.OtherRecInfo = document.OtherRecInfo;
                    entity.DeclarePrice = document.DeclarePrice;
                    entity.PromiseNote = document.PromiseNote;
                    entity.DeclTele = document.DeclTele;
                    entity.WarningCount = document.Warnings.Count;
                    entity.WarningSummary = BuildWarningSummary(document.Warnings);
                    if (entity.DraftRevision <= 0)
                    {
                        entity.DraftRevision = 1;
                    }

                    entity.ManualLockedFieldsJson ??= string.Empty;
                    if (string.IsNullOrWhiteSpace(entity.SourceBaselineJson))
                    {
                        var baselineDocument = CreateAgentConsignmentDocumentFromMapped(
                            invoice.Id,
                            invoice,
                            rawExistingDocument: null,
                            mapped: document);
                        entity.SourceBaselineJson = SingleWindowDraftStateHelper.BuildAgentConsignmentSourceBaselineJson(baselineDocument);
                    }

                    if (string.IsNullOrWhiteSpace(entity.SourceBaselineHash))
                    {
                        entity.SourceBaselineHash = SingleWindowDraftStateHelper.ComputeBaselineHash(entity.SourceBaselineJson);
                    }

                    entity.LastGeneratedAt = DateTime.Now;

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return entity.Id;
                },
                cancellationToken).ConfigureAwait(false);
        }

        private AgentConsignmentDocument CreateAgentConsignmentEditorDocument(
            int invoiceId,
            Invoice invoice,
            IReadOnlyList<Item> invoiceItems,
            Customer customer,
            Exporter exporter,
            AgentConsignmentDocument existingDocument)
        {
            var rawExistingDocument = SingleWindowSourceCloneHelper.CloneAgentConsignmentDocument(existingDocument);

            var sourceSnapshot = new AcdSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(invoiceItems),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = null,
                Attachments = []
            };
            var sourceMapped = _agentConsignmentFieldMapper.Map(sourceSnapshot);
            var sourceDefaultsDocument = CreateAgentConsignmentDocumentFromMapped(
                invoiceId,
                invoice,
                rawExistingDocument: null,
                mapped: sourceMapped);

            var snapshot = new AcdSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(invoiceItems),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = SingleWindowDraftStateHelper.BuildAgentConsignmentLockedOverlay(existingDocument),
                Attachments = []
            };
            var mapped = _agentConsignmentFieldMapper.Map(snapshot);
            var document = CreateAgentConsignmentDocumentFromMapped(
                invoiceId,
                invoice,
                rawExistingDocument,
                mapped);

            var diff = SingleWindowDraftStateHelper.BuildAgentConsignmentSourceDiff(
                rawExistingDocument?.SourceBaselineJson,
                sourceDefaultsDocument);
            document.SourceDiffCount = diff.Count;
            document.SourceDiffSummary = diff.Summary;
            document.ManualLockedFieldCount = SingleWindowDraftStateHelper.BuildAgentConsignmentLockedFields(document, sourceDefaultsDocument).Count;
            return document;
        }

        private static AgentConsignmentDocument CreateAgentConsignmentDocumentFromMapped(
            int invoiceId,
            Invoice invoice,
            AgentConsignmentDocument rawExistingDocument,
            AcdMappedDocument mapped)
        {
            var document = rawExistingDocument != null
                ? SingleWindowSourceCloneHelper.CloneAgentConsignmentDocument(rawExistingDocument)
                : new AgentConsignmentDocument
                {
                    SourceInvoiceId = invoiceId
                };

            document.SourceInvoiceId = invoiceId;
            document.InvoiceNo = invoice.InvoiceNo ?? string.Empty;
            document.ContractNo = invoice.ContractNo ?? string.Empty;
            document.CopCusCode = mapped.CopCusCode;
            document.Sign = mapped.Sign;
            document.OperType = mapped.OperType;
            document.GName = mapped.GName;
            document.CodeTS = mapped.CodeTS;
            document.DeclTotal = mapped.DeclTotal;
            document.IEDate = mapped.IEDate;
            document.ListNo = mapped.ListNo;
            document.TradeMode = mapped.TradeMode;
            document.OriCountry = mapped.OriCountry;
            document.TradeCode = mapped.TradeCode;
            document.AgentCode = mapped.AgentCode;
            document.Curr = mapped.Curr;
            document.QtyOrWeight = mapped.QtyOrWeight;
            document.PackingCondition = mapped.PackingCondition;
            document.OtherNote = mapped.OtherNote;
            document.ConsignTele = mapped.ConsignTele;
            document.EntryId = mapped.EntryId;
            document.ReceiveDate = mapped.ReceiveDate;
            document.PaperInfo = mapped.PaperInfo;
            document.OtherRecInfo = mapped.OtherRecInfo;
            document.DeclarePrice = mapped.DeclarePrice;
            document.PromiseNote = mapped.PromiseNote;
            document.DeclTele = mapped.DeclTele;
            document.WarningCount = mapped.Warnings.Count;
            document.WarningSummary = BuildWarningSummary(mapped.Warnings);
            return document;
        }
    }
}
