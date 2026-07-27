using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public sealed record InvoiceDataMaintenancePreview(
        int Id,
        string InvoiceNo,
        string Type,
        string Status,
        string StatusDisplayName,
        DateTime InvoiceDate,
        string CustomerName,
        bool CanPurge,
        string Guidance,
        string StoragePolicy);

    public sealed record InvoicePurgeCommand(
        int InvoiceId,
        string InvoiceNoConfirmation,
        string Reason);

    public sealed record InvoicePurgeResult(
        bool Success,
        int InvoiceId,
        string InvoiceNo,
        string PreviousStatus,
        string Message,
        string StoragePolicy);

    public interface IInvoiceDataMaintenanceService
    {
        Task<InvoiceDataMaintenancePreview> GetPurgePreviewAsync(
            int invoiceId,
            CancellationToken cancellationToken = default);

        Task<InvoicePurgeResult> PurgeCancelledInvoiceAsync(
            InvoicePurgeCommand command,
            CancellationToken cancellationToken = default);
    }
}
