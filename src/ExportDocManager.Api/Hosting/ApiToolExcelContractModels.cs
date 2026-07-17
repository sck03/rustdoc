namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiExcelImportPreviewRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public sealed class ApiExcelOutputRequest
    {
        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiExcelConvertBookingSheetRequest
    {
        public string SourcePath { get; set; } = string.Empty;

        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiInvoiceBookingSheetRequest
    {
        public int InvoiceId { get; set; }

        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiExcelImportPreviewResponse
    {
        public ApiExcelImportPreviewResponse(
            string sourcePath,
            bool success,
            ApiInvoiceDetailDto invoice,
            ApiImportedCustomerDto customer,
            ApiImportedExporterDto exporter,
            ApiExcelImportAnalysisReportDto analysisReport,
            IReadOnlyList<string> errors,
            string storagePolicy)
        {
            SourcePath = sourcePath ?? string.Empty;
            Success = success;
            Invoice = invoice;
            Customer = customer;
            Exporter = exporter;
            AnalysisReport = analysisReport;
            Errors = errors ?? Array.Empty<string>();
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public string SourcePath { get; }

        public bool Success { get; }

        public ApiInvoiceDetailDto Invoice { get; }

        public ApiImportedCustomerDto Customer { get; }

        public ApiImportedExporterDto Exporter { get; }

        public ApiExcelImportAnalysisReportDto AnalysisReport { get; }

        public IReadOnlyList<string> Errors { get; }

        public string StoragePolicy { get; }
    }

    public sealed record ApiExcelImportAnalysisReportDto(
        string SchemaVersion,
        string AnalyzerId,
        string SelectedWorksheetName,
        decimal Confidence,
        IReadOnlyList<ApiExcelImportSheetAnalysisDto> Sheets,
        IReadOnlyList<ApiExcelImportFieldAnalysisDto> Fields,
        ApiExcelImportItemTableAnalysisDto ItemTable,
        IReadOnlyList<ApiExcelImportAnalysisIssueDto> Issues);

    public sealed record ApiExcelImportSheetAnalysisDto(
        string Name,
        int UsedRowCount,
        int UsedColumnCount,
        int FieldCandidateCount,
        bool HasItemTable,
        decimal Confidence);

    public sealed record ApiExcelImportFieldAnalysisDto(
        string FieldKey,
        string DisplayName,
        string Value,
        string WorksheetName,
        int Row,
        int Column,
        decimal Confidence,
        string Source);

    public sealed record ApiExcelImportItemTableAnalysisDto(
        string WorksheetName,
        int HeaderRow,
        int HeaderDepth,
        int DataStartRow,
        decimal Confidence,
        ApiExcelImportItemColumnAnalysisDto Columns);

    public sealed record ApiExcelImportItemColumnAnalysisDto(
        int PoNumberCol,
        int StyleNoCol,
        int StyleNameCol,
        int FabricCompositionCol,
        int StyleNameCNCol,
        int BrandCol,
        int HSCodeCol,
        int OriginCol,
        int QuantityCol,
        int UnitENCol,
        int UnitCNCol,
        int CartonsCol,
        int CtnUnitENCol,
        int LengthCol,
        int WidthCol,
        int HeightCol,
        int DimensionCol,
        int VolumeCol,
        int GWPerCtnCol,
        int GWTotalCol,
        int NWPerCtnCol,
        int NWTotalCol,
        int UnitPriceCol,
        int TotalPriceCol);

    public sealed record ApiExcelImportAnalysisIssueDto(
        string Severity,
        string Code,
        string Message,
        string FieldKey);

    public sealed class ApiImportedCustomerDto
    {
        public int Id { get; init; }

        public string CustomerNameEN { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string NotifyPartyName { get; init; } = string.Empty;

        public string AddressEN { get; init; } = string.Empty;

        public string NotifyPartyAddress { get; init; } = string.Empty;

        public string ContactPerson { get; init; } = string.Empty;

        public string Phone { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string TaxId { get; init; } = string.Empty;

        public string Notes { get; init; } = string.Empty;
    }

    public sealed class ApiImportedExporterDto
    {
        public int Id { get; init; }

        public string ExporterNameEN { get; init; } = string.Empty;

        public string ExporterNameCN { get; init; } = string.Empty;

        public string AddressEN { get; init; } = string.Empty;

        public string AddressCN { get; init; } = string.Empty;

        public string ContactPerson { get; init; } = string.Empty;

        public string CreditCode { get; init; } = string.Empty;

        public string CustomsCode { get; init; } = string.Empty;

        public string Phone { get; init; } = string.Empty;

        public string BankName { get; init; } = string.Empty;

        public string BankAccount { get; init; } = string.Empty;

        public string SwiftCode { get; init; } = string.Empty;

        public string Notes { get; init; } = string.Empty;

        public string DocSealPath { get; init; } = string.Empty;

        public string CustomsSealPath { get; init; } = string.Empty;
    }
}
