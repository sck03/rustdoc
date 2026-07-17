using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiInvoiceListItemDto(
        int Id,
        string InvoiceNo,
        string ContractNo,
        DateTime InvoiceDate,
        string CustomerName,
        string ExporterName,
        string DestinationCountry,
        string PortOfLoading,
        string PortOfDestination,
        string Currency,
        decimal TotalAmount,
        string Type,
        string Status);

    public sealed class ApiInvoiceDetailDto
    {
        public int Id { get; init; }
        public int? OwnerUserId { get; init; }
        public string DepartmentId { get; init; } = string.Empty;
        public string CompanyScope { get; init; } = string.Empty;
        public string InvoiceNo { get; init; } = string.Empty;
        public string ContractNo { get; init; } = string.Empty;
        public DateTime InvoiceDate { get; init; }
        public string LetterOfCreditNo { get; init; } = string.Empty;
        public string LetterOfCreditSourcePath { get; init; } = string.Empty;
        public string LetterOfCreditContent { get; init; } = string.Empty;
        public string IssuingBank { get; init; } = string.Empty;
        public string CustomsBrokerName { get; init; } = string.Empty;
        public string CustomsBrokerCode { get; init; } = string.Empty;
        public string Spare1 { get; init; } = string.Empty;
        public string Spare2 { get; init; } = string.Empty;
        public string Spare3 { get; init; } = string.Empty;
        public string CustomFieldsJson { get; init; } = string.Empty;
        public string PaymentTerms { get; init; } = string.Empty;
        public string PortOfLoading { get; init; } = string.Empty;
        public string PortOfDestination { get; init; } = string.Empty;
        public string DestinationCountry { get; init; } = string.Empty;
        public string ShippingMarks { get; init; } = string.Empty;
        public string ShippingMarksType { get; init; } = string.Empty;
        public string ShippingMarksImage { get; init; } = string.Empty;
        public string TradeTerms { get; init; } = string.Empty;
        public string TransportMode { get; init; } = string.Empty;
        public DateTime ShipmentDate { get; init; }
        public int ExporterId { get; init; }
        public int CustomerId { get; init; }
        public decimal TotalCartons { get; init; }
        public decimal TotalQuantity { get; init; }
        public decimal TotalGrossWeight { get; init; }
        public decimal TotalNetWeight { get; init; }
        public decimal TotalVolume { get; init; }
        public decimal TotalAmount { get; init; }
        public decimal TotalPurchaseAmount { get; init; }
        public decimal TotalTaxRefundAmount { get; init; }
        public decimal TotalProfit { get; init; }
        public string Currency { get; init; } = string.Empty;
        public string SpecialTerms { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string SupervisionMode { get; init; } = string.Empty;
        public string CustomerNameEN { get; init; } = string.Empty;
        public string CustomerAddressEN { get; init; } = string.Empty;
        public string NotifyPartyName { get; init; } = string.Empty;
        public string NotifyPartyAddress { get; init; } = string.Empty;
        public string ExporterNameEN { get; init; } = string.Empty;
        public string ExporterNameCN { get; init; } = string.Empty;
        public string ExporterAddressEN { get; init; } = string.Empty;
        public string ExporterAddressCN { get; init; } = string.Empty;
        public string ExporterCreditCode { get; init; } = string.Empty;
        public string ExporterCustomsCode { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string BankAccount { get; init; } = string.Empty;
        public string SwiftCode { get; init; } = string.Empty;
        public decimal? ExchangeRate { get; init; }
        public string Status { get; init; } = string.Empty;
        public string RowVersion { get; init; } = string.Empty;
        public IReadOnlyList<ApiInvoiceItemDto> Items { get; init; } = Array.Empty<ApiInvoiceItemDto>();
    }

    public sealed class ApiInvoiceItemDto
    {
        public int Id { get; init; }
        public int InvoiceId { get; init; }
        public string PoNumber { get; init; } = string.Empty;
        public string StyleNo { get; init; } = string.Empty;
        public string StyleName { get; init; } = string.Empty;
        public string FabricComposition { get; init; } = string.Empty;
        public string StyleNameCN { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string HSCode { get; init; } = string.Empty;
        public string Origin { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string UnitEN { get; init; } = string.Empty;
        public string UnitCN { get; init; } = string.Empty;
        public decimal PcsPerCtn { get; init; }
        public decimal Cartons { get; init; }
        public string CtnUnitEN { get; init; } = string.Empty;
        public string CtnUnitCN { get; init; } = string.Empty;
        public decimal Length { get; init; }
        public decimal Width { get; init; }
        public decimal Height { get; init; }
        public decimal Volume { get; init; }
        public decimal GWPerCtn { get; init; }
        public decimal NWPerCtn { get; init; }
        public decimal GWTotal { get; init; }
        public decimal NWTotal { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal TotalPrice { get; init; }
        public decimal PurchasePrice { get; init; }
        public decimal PurchaseTotal { get; init; }
        public decimal TaxRebateRate { get; init; }
        public decimal TaxRefundAmount { get; init; }
        public string Spare1 { get; init; } = string.Empty;
        public string Spare2 { get; init; } = string.Empty;
        public string Spare3 { get; init; } = string.Empty;
        public string CustomFieldsJson { get; init; } = string.Empty;
    }

    public sealed record ApiInvoiceSaveResponse(
        bool Success,
        int Id,
        bool IsUpdate,
        ApiInvoiceDetailDto Invoice);

    public sealed record ApiInvoiceCloneRequest(
        string NewInvoiceNo,
        InvoiceCloneOptions Options);

    public sealed record ApiInvoiceCloneResponse(
        bool Success,
        int Id,
        ApiInvoiceDetailDto Invoice,
        string Message);

    public sealed record ApiInvoiceCloneTypeRequest(
        string TargetType,
        InvoiceCloneOptions Options);

    public sealed record ApiInvoiceCloneTypeResponse(
        bool Success,
        int Id,
        ApiInvoiceDetailDto Invoice,
        string Message);

    public sealed class ApiShippingMarkImageSaveRequest
    {
        public string ImageDataUrl { get; init; } = string.Empty;
    }

    public sealed class ApiShippingMarkImagePreviewRequest
    {
        public string ImagePath { get; init; } = string.Empty;
    }

    public sealed record ApiShippingMarkImageSaveResponse(
        string ImagePath,
        string FileName,
        string ContentType,
        long SizeBytes,
        string StoragePolicy);

    public sealed record ApiShippingMarkImagePreviewResponse(
        string ImagePath,
        string FileName,
        string ContentType,
        long SizeBytes,
        string DataUrl,
        string StoragePolicy);

    public sealed class ApiInvoiceTransferPathRequest
    {
        public string PackagePath { get; init; } = string.Empty;
    }

    public sealed class ApiInvoiceTransferImportRequest
    {
        public string PackagePath { get; init; } = string.Empty;

        public string ConflictAction { get; init; } = "NewInvoiceNo";

        public string NewInvoiceNo { get; init; } = string.Empty;

        public bool AllowInvalidChecksum { get; init; }
    }

    public sealed record ApiInvoiceTransferPreviewDto(
        string InvoiceNo,
        string Type,
        int ItemCount,
        bool CustomerExists,
        bool ExporterExists,
        bool InvoiceExists,
        bool InvoiceMatches,
        int ExistingInvoiceId);

    public sealed record ApiInvoiceTransferPreviewResponse(
        bool ChecksumValid,
        string ChecksumMessage,
        ApiInvoiceTransferPreviewDto Preview,
        string StoragePolicy);

    public sealed record ApiInvoiceTransferExportResponse(
        bool Success,
        int InvoiceId,
        string PackagePath,
        string StoragePolicy,
        string Message);

    public sealed record ApiInvoiceTransferImportResultDto(
        bool Success,
        string Message,
        int? InvoiceId,
        string FinalInvoiceNo,
        string ActionTaken);

    public sealed record ApiInvoiceTransferImportResponse(
        bool Success,
        ApiInvoiceTransferImportResultDto Result,
        ApiInvoiceTransferPreviewDto Preview,
        string StoragePolicy,
        string Message);
}
