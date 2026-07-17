namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiQueryInvoiceRowDto
    {
        public int Id { get; init; }
        public string InvoiceNo { get; init; } = string.Empty;
        public string InvoiceDate { get; init; } = string.Empty;
        public string ContractNo { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public string ExporterName { get; init; } = string.Empty;
        public string DestinationCountry { get; init; } = string.Empty;
        public string TradeTerms { get; init; } = string.Empty;
        public string ShipmentDate { get; init; } = string.Empty;
        public string TransportMode { get; init; } = string.Empty;
        public decimal TotalCartons { get; init; }
        public decimal TotalQuantity { get; init; }
        public decimal TotalAmount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
    }

    public class ApiQueryInvoiceFilterRequest
    {
        public DateTime? StartDate { get; init; }
        public DateTime? EndDate { get; init; }
        public int? CustomerId { get; init; }
        public int? ExporterId { get; init; }
        public string Keyword { get; init; } = string.Empty;
        public string ContractNo { get; init; } = string.Empty;
        public string InvoiceType { get; init; } = string.Empty;
        public string TransportMode { get; init; } = string.Empty;
        public string StyleName { get; init; } = string.Empty;
        public string StyleNo { get; init; } = string.Empty;
    }

    public sealed class ApiQueryInvoiceExportRequest : ApiQueryInvoiceFilterRequest
    {
        public string DestinationPath { get; init; } = string.Empty;
    }

    public sealed record ApiQueryInvoiceExportResponse(
        bool Success,
        string Message,
        int ExportedCount,
        string DestinationPath,
        string StoragePolicy);
}
