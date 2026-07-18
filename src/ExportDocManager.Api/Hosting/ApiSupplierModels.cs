namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSupplierDto(int Id, string Name, string CountryRegion, string Category,
        string Website, string Status, string MainProducts, string Notes, int VersionNumber);
    public sealed record ApiSupplierSaveRequest(int Id, string Name, string CountryRegion, string Category,
        string Website, string Status, string MainProducts, string Notes, int ExpectedVersion = 0);
    public sealed record ApiSupplierContactDto(int Id, int SupplierCompanyId, string Name, string Title,
        string Email, string Phone, string InstantMessaging, bool IsPrimary, int VersionNumber);
    public sealed record ApiSupplierContactSaveRequest(int Id, int SupplierCompanyId, string Name, string Title,
        string Email, string Phone, string InstantMessaging, bool IsPrimary, int ExpectedVersion = 0);
    public sealed record ApiSupplierImportRowDto(int RowNumber, string Name, string CountryRegion, string Category,
        string Website, string Status, string MainProducts, string Notes, string ContactName, string ContactTitle,
        string ContactEmail, string ContactPhone, bool IsDuplicate, string Error);
    public sealed record ApiSupplierImportPreviewDto(int TotalRows, int ValidRows, int DuplicateRows, IReadOnlyList<ApiSupplierImportRowDto> Rows);
    public sealed record ApiSupplierImportRequest(IReadOnlyList<ApiSupplierImportRowDto> Rows);
    public sealed record ApiSupplierImportResultDto(int CreatedSuppliers, int CreatedContacts, int SkippedRows);
    public sealed record ApiSupplierBatchStatusRequest(IReadOnlyList<int> Ids, string Status);
    public sealed record ApiSupplierBatchStatusResult(int AffectedCount, string Status);
    public sealed record ApiSupplierProductOptionDto(int Id, string ProductCode, string NameCN, string NameEN);
    public sealed record ApiSupplierProductLinkDto(int Id, int SupplierCompanyId, int ProductId, string ProductCode,
        string ProductNameCN, string ProductNameEN, string SupplierProductCode, decimal ReferencePrice,
        string Currency, int LeadTimeDays, string Status, int VersionNumber);
    public sealed record ApiSupplierProductLinkSaveRequest(int Id, int SupplierCompanyId, int ProductId,
        string SupplierProductCode, decimal ReferencePrice, string Currency, int LeadTimeDays, string Status,
        int ExpectedVersion = 0);
    public sealed record ApiSupplierAssessmentDto(int Id, int SupplierCompanyId, DateTimeOffset AssessedAt,
        string AssessmentKind, int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        decimal AverageScore, string Conclusion, string Notes, string AssessedBy,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int VersionNumber);
    public sealed record ApiSupplierAssessmentSaveRequest(int Id, int SupplierCompanyId, DateTimeOffset AssessedAt,
        string AssessmentKind, int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        string Conclusion, string Notes, int ExpectedVersion = 0);
    public sealed record ApiSupplierAssessmentOverviewItemDto(int SupplierCompanyId, string SupplierName,
        string SupplierStatus, string Category, int AssessmentCount, DateTimeOffset LatestAssessedAt,
        string LatestAssessmentKind, int QualityScore, int DeliveryScore, int ServiceScore, int PriceScore,
        decimal AverageScore, string Conclusion, string Notes);
    public sealed record ApiSupplierAssessmentOverviewDto(int TotalSuppliers, int AssessedSuppliers,
        int UnassessedSuppliers, int PreferredCount, int QualifiedCount, int WatchCount, int PausedCount,
        decimal AverageQualityScore, decimal AverageDeliveryScore, decimal AverageServiceScore,
        decimal AveragePriceScore, IReadOnlyList<ApiSupplierAssessmentOverviewItemDto> Items);
}
