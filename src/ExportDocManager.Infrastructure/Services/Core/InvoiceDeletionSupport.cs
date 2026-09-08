using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    internal static class InvoiceDeletionSupport
    {
        public const string AttachmentRetentionGuidance = "该发票包含业务归档资料，必须保留原单据和全部附件版本。请使用作废操作，不支持直接删除或管理员清理。";

        public static Task<bool> HasRetainedAttachmentsAsync(AppDbContext context, int invoiceId, CancellationToken token) =>
            context.BusinessAttachments.AnyAsync(item => item.InvoiceId == invoiceId, token);

        public static async Task TrackWorkspaceDeletionAsync(
            AppDbContext context,
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (await HasRetainedAttachmentsAsync(context, invoiceId, cancellationToken))
                throw new InvoiceConflictException(AttachmentRetentionGuidance);

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
