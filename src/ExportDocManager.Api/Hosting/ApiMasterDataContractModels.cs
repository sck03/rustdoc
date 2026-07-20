namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiCustomerDto(
        int Id,
        string CustomerNameEN,
        string DisplayName,
        string NotifyPartyName,
        string AddressEN,
        string NotifyPartyAddress,
        string ContactPerson,
        string Phone,
        string Email,
        string TaxId,
        string Notes,
        string RowVersion);

    public sealed record ApiExporterDto(
        int Id,
        string ExporterNameEN,
        string ExporterNameCN,
        string AddressEN,
        string AddressCN,
        string ContactPerson,
        string CreditCode,
        string CustomsCode,
        string Phone,
        string BankName,
        string BankAccount,
        string SwiftCode,
        string Notes,
        string DocSealPath,
        string CustomsSealPath,
        string RowVersion);

    public sealed record ApiPayeeDto(
        int Id,
        string Category,
        string Name,
        string BankName,
        string RMBAccount,
        string USDAccount,
        string ContactPerson,
        string Phone,
        string Notes);

    public sealed record ApiProductDto(
        int Id,
        string ProductCode,
        string NameEN,
        string NameCN,
        string Description,
        string HSCode,
        string Elements,
        string SupervisionConditions,
        string InspectionCategory,
        decimal TaxRebateRate,
        string Material,
        string Brand,
        string Origin,
        string UnitEN,
        string UnitCN,
        decimal Length,
        decimal Width,
        decimal Height,
        decimal GWPerCtn,
        decimal NWPerCtn,
        decimal PcsPerCtn,
        string PackageUnitEN,
        string PackageUnitCN,
        decimal DefaultPrice,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public sealed record ApiPortDto(
        int Id,
        string NameEN,
        string NameCN,
        string Country,
        string Code);

    public sealed record ApiUnitDto(
        int Id,
        string NameEN,
        string NameCN,
        string Code);

    public sealed record ApiHsCodeDto(
        int Id,
        string Code,
        string NormalizedCode,
        string Name,
        string Unit,
        string Description,
        string Elements,
        string SupervisionConditions,
        string InspectionCategory,
        string RebateRate,
        DateTime? UpdateTime,
        string DetailUrl,
        string Status = "Active",
        string SourceName = "",
        int? EffectiveYear = null,
        DateTime? LastVerifiedAt = null,
        string ReplacedByCodes = "",
        string NormalTariffRate = "",
        string PreferentialTariffRate = "",
        string ExportTariffRate = "",
        string ConsumptionTaxRate = "",
        string ValueAddedTaxRate = "",
        string Notes = "");

    public sealed record ApiHsCodeImportPathRequest(
        string FilePath);

    public sealed record ApiHsCodeImportPreviewPathRequest(
        string FilePath,
        string Mode,
        string SourceName,
        int? EffectiveYear);

    public sealed record ApiHsCodeImportCommitRequest(string Token);

    public sealed record ApiHsCodeImportColumnMappingDto(
        string Field,
        string Header,
        int ColumnNumber,
        int Confidence);

    public sealed record ApiHsCodeImportPreviewItemDto(
        string ChangeType,
        int RowNumber,
        ApiHsCodeDto Item,
        IReadOnlyList<string> ChangedFields,
        IReadOnlyList<string> ReplacementCandidates,
        string Message);

    public sealed record ApiHsCodeImportPreviewResponse(
        string Token,
        string FileName,
        string Mode,
        string SourceName,
        int? EffectiveYear,
        string WorksheetName,
        int HeaderRowNumber,
        int Confidence,
        IReadOnlyList<ApiHsCodeImportColumnMappingDto> Columns,
        IReadOnlyList<ApiHsCodeImportPreviewItemDto> Items,
        int AddCount,
        int UpdateCount,
        int UnchangedCount,
        int SuspectedObsoleteCount,
        int ConflictCount,
        int InvalidCount,
        IReadOnlyList<string> Warnings,
        string StoragePolicy);

    public sealed record ApiHsCodeImportCommitResponse(
        bool Success,
        int AddedCount,
        int UpdatedCount,
        int UnchangedCount,
        int SuspectedObsoleteCount,
        int SkippedCount,
        string Message);

    public sealed record ApiHsCodeRemoteHealthResponse(
        string Source,
        bool Available,
        DateTimeOffset CheckedAt,
        string Message);

    public sealed record ApiHsCodeClearAllRequest(
        string Confirmation);

    public sealed record ApiHsCodeBatchDeleteRequest(
        IReadOnlyList<int> Ids);

    public sealed record ApiHsCodeImportResponse(
        bool Success,
        string FileName,
        int TotalCount,
        string Message,
        string StoragePolicy);

    public sealed record ApiHsCodeSearchResponse(
        IReadOnlyList<ApiHsCodeDto> Items,
        int Count,
        string Source,
        string StoragePolicy);

    public sealed record ApiHsCodeRemoteDetailResolutionResponse(
        IReadOnlyList<ApiHsCodeDto> Items,
        IReadOnlyList<ApiHsCodeDto> RemovedItems,
        int UpdatedCount,
        int RemovedCount,
        string Message,
        string StoragePolicy);
}
