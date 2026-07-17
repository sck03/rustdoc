using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService :
        ISingleWindowTrackingService,
        ISingleWindowOperationCenterService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public SingleWindowTrackingService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<int> ResolveNextSubmissionVersionAsync(
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await ResolveNextSubmissionVersionCoreAsync(
                context,
                businessType,
                sourceInvoiceId,
                sourceDocumentId,
                cancellationToken);
        }

        private async Task<int> ResolveNextSubmissionVersionCoreAsync(
            AppDbContext context,
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            CancellationToken cancellationToken)
        {
            string businessTypeText = businessType.ToString();

            var batches = context.SwSubmissionBatches
                .AsNoTracking()
                .Where(item => item.BusinessType == businessTypeText);
            batches = _businessDataAccessScope.ApplySubmissionBatchScope(batches, context);

            if (sourceInvoiceId > 0)
            {
                batches = batches.Where(item => item.SourceInvoiceId == sourceInvoiceId);
            }
            else if (sourceDocumentId > 0)
            {
                batches = batches.Where(item => item.SourceDocumentId == sourceDocumentId);
            }

            int currentVersion = await batches
                .Select(item => (int?)item.SubmissionVersion)
                .MaxAsync(cancellationToken)
                ?? 0;

            return Math.Max(1, currentVersion + 1);
        }
    }
}
