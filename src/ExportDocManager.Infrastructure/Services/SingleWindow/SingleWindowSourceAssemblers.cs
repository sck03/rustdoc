using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class CustomsCooSourceAssembler : ICustomsCooSourceAssembler
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public CustomsCooSourceAssembler(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<CooSourceSnapshot> BuildAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var invoice = await _businessDataAccessScope
                .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要生成海关原产地证报文的发票。");

            var items = await context.Items
                .AsNoTracking()
                .Where(item => item.InvoiceId == invoiceId)
                .ToListAsync(cancellationToken);

            Customer customer = null;
            if (invoice.CustomerId > 0)
            {
                customer = await context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.CustomerId, cancellationToken);
            }

            Exporter exporter = null;
            if (invoice.ExporterId > 0)
            {
                exporter = await context.Exporters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.ExporterId, cancellationToken);
            }

            var existingDocument = await context.CustomsCooDocuments
                .AsNoTracking()
                .AsSplitQuery()
                .Include(item => item.Items)
                .Include(item => item.Attachments)
                .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoiceId, cancellationToken);

            return new CooSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(items),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = SingleWindowDraftStateHelper.BuildCustomsCooLockedOverlay(existingDocument, items),
                Attachments = SingleWindowSourceCloneHelper.CloneAttachmentSources(existingDocument?.Attachments)
            };
        }
    }

    public sealed class AgentConsignmentSourceAssembler : IAgentConsignmentSourceAssembler
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public AgentConsignmentSourceAssembler(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<AcdSourceSnapshot> BuildAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var invoice = await _businessDataAccessScope
                .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken)
                ?? throw new InvalidOperationException("未找到要生成报关代理委托报文的发票。");

            var items = await context.Items
                .AsNoTracking()
                .Where(item => item.InvoiceId == invoiceId)
                .ToListAsync(cancellationToken);

            Customer customer = null;
            if (invoice.CustomerId > 0)
            {
                customer = await context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.CustomerId, cancellationToken);
            }

            Exporter exporter = null;
            if (invoice.ExporterId > 0)
            {
                exporter = await context.Exporters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.ExporterId, cancellationToken);
            }

            var existingDocument = await context.AgentConsignmentDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceInvoiceId == invoiceId, cancellationToken);

            return new AcdSourceSnapshot
            {
                Invoice = SingleWindowSourceCloneHelper.CloneInvoice(invoice),
                Items = SingleWindowSourceCloneHelper.CloneItems(items),
                Customer = SingleWindowSourceCloneHelper.CloneCustomer(customer),
                Exporter = SingleWindowSourceCloneHelper.CloneExporter(exporter),
                ExistingDocument = SingleWindowDraftStateHelper.BuildAgentConsignmentLockedOverlay(existingDocument),
                Attachments = []
            };
        }
    }
}
