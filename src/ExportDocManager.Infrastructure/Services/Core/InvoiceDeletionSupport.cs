using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    internal static class InvoiceDeletionSupport
    {
        public static async Task TrackSingleWindowWorkspaceDeletionAsync(
            AppDbContext context,
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            var customsCooDocument = await context.CustomsCooDocuments
                .FirstOrDefaultAsync(
                    document => document.SourceInvoiceId == invoiceId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (customsCooDocument != null)
            {
                context.CustomsCooDocuments.Remove(customsCooDocument);
            }

            var agentConsignmentDocument = await context.AgentConsignmentDocuments
                .FirstOrDefaultAsync(
                    document => document.SourceInvoiceId == invoiceId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (agentConsignmentDocument != null)
            {
                context.AgentConsignmentDocuments.Remove(agentConsignmentDocument);
            }

            var handoffPackageRecords = await context.SwHandoffPackageRecords
                .Where(record => record.SourceInvoiceId == invoiceId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (handoffPackageRecords.Count > 0)
            {
                context.SwHandoffPackageRecords.RemoveRange(handoffPackageRecords);
            }

            var submissionBatches = await context.SwSubmissionBatches
                .Where(batch => batch.SourceInvoiceId == invoiceId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (submissionBatches.Count > 0)
            {
                context.SwSubmissionBatches.RemoveRange(submissionBatches);
            }
        }
    }
}
