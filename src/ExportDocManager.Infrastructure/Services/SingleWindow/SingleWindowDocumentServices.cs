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
    public sealed partial class SingleWindowDocumentPersistenceService :
        ISingleWindowDocumentPersistenceService,
        ICustomsCooDocumentService,
        IAgentConsignmentDocumentService
    {
        private readonly ICustomsCooFieldMapper _customsCooFieldMapper;
        private readonly IAgentConsignmentFieldMapper _agentConsignmentFieldMapper;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ISettingsService _settingsService;
        private readonly ICustomsCooProducerProfileService _producerProfileService;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private sealed record EditorSourceContext(
            Invoice Invoice,
            IReadOnlyList<Item> InvoiceItems,
            Customer Customer,
            Exporter Exporter);

        public SingleWindowDocumentPersistenceService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICustomsCooFieldMapper customsCooFieldMapper,
            IAgentConsignmentFieldMapper agentConsignmentFieldMapper,
            ISettingsService settingsService,
            ICustomsCooProducerProfileService producerProfileService,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _customsCooFieldMapper = customsCooFieldMapper ?? throw new ArgumentNullException(nameof(customsCooFieldMapper));
            _agentConsignmentFieldMapper = agentConsignmentFieldMapper ?? throw new ArgumentNullException(nameof(agentConsignmentFieldMapper));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _producerProfileService = producerProfileService ?? throw new ArgumentNullException(nameof(producerProfileService));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        async Task<CustomsCooDocument> ICustomsCooDocumentService.GetOrCreateAsync(int invoiceId, CancellationToken cancellationToken)
        {
            return await LoadCustomsCooDocumentAsync(invoiceId, includeExistingDocument: true, cancellationToken);
        }

        async Task<CustomsCooDocument> ICustomsCooDocumentService.BuildDefaultsAsync(int invoiceId, CancellationToken cancellationToken)
        {
            return await LoadCustomsCooDocumentAsync(invoiceId, includeExistingDocument: false, cancellationToken);
        }

        async Task<AgentConsignmentDocument> IAgentConsignmentDocumentService.GetOrCreateAsync(int invoiceId, CancellationToken cancellationToken)
        {
            return await LoadAgentConsignmentDocumentAsync(invoiceId, includeExistingDocument: true, cancellationToken);
        }

        async Task<AgentConsignmentDocument> IAgentConsignmentDocumentService.BuildDefaultsAsync(int invoiceId, CancellationToken cancellationToken)
        {
            return await LoadAgentConsignmentDocumentAsync(invoiceId, includeExistingDocument: false, cancellationToken);
        }

        private async Task<EditorSourceContext> LoadEditorSourceContextAsync(
            AppDbContext context,
            int invoiceId,
            CancellationToken cancellationToken)
        {
            var invoice = await _businessDataAccessScope
                .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == invoiceId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("未找到对应的发票。");

            var invoiceItems = await context.Items
                .AsNoTracking()
                .Where(item => item.InvoiceId == invoiceId)
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Customer customer = null;
            if (invoice.CustomerId > 0)
            {
                customer = await context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.CustomerId, cancellationToken)
                    .ConfigureAwait(false);
            }

            Exporter exporter = null;
            if (invoice.ExporterId > 0)
            {
                exporter = await context.Exporters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.ExporterId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new EditorSourceContext(invoice, invoiceItems, customer, exporter);
        }

        private static string BuildWarningSummary(IReadOnlyList<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, warnings.Take(20));
        }
    }
}

