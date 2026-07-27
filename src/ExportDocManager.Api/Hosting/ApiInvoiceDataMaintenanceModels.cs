namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiInvoicePurgeRequest
    {
        public string InvoiceNoConfirmation { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    public sealed record ApiInvoiceDataMaintenancePreviewResponse(
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

    public sealed record ApiInvoicePurgeResponse(
        bool Success,
        int InvoiceId,
        string InvoiceNo,
        string PreviousStatus,
        string Message,
        string StoragePolicy);
}
