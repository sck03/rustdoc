using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSingleWindowReferenceCatalogResponse(
        SingleWindowReferenceCatalogModel Catalog,
        string StoragePolicy);

    public sealed record ApiSingleWindowReferenceCatalogSaveRequest(
        SingleWindowReferenceCatalogModel Catalog);

    public sealed record ApiSingleWindowReferenceCatalogSaveResponse(
        bool Success,
        SingleWindowReferenceCatalogModel Catalog,
        string Message,
        string StoragePolicy);

    public sealed record ApiSingleWindowReferenceCatalogExcelColumnMappingDto(
        string FieldKey,
        string Label,
        int ColumnNumber,
        bool Required);

    public sealed record ApiSingleWindowReferenceCatalogExcelImportPreviewResponse(
        bool Success,
        string CatalogKey,
        string SheetName,
        IReadOnlyList<string> SheetNames,
        int HeaderRowNumber,
        int DataStartRowNumber,
        IReadOnlyList<ApiSingleWindowReferenceCatalogExcelColumnMappingDto> ColumnMappings,
        SingleWindowReferenceCatalogModel Catalog,
        int RowCount,
        string Message,
        string StoragePolicy);

    public sealed record ApiSingleWindowIssuingAuthorityOptionDto(
        string Code,
        string Label,
        string ApplicationAddress);

    public sealed record ApiSingleWindowIssuingAuthorityCatalogResponse(
        IReadOnlyList<ApiSingleWindowIssuingAuthorityOptionDto> Options,
        string StoragePolicy);

    public sealed record ApiCustomsCooOptionDto(
        string Value,
        string Label);

    public sealed record ApiCustomsCooOriginCriteriaOptionSetDto(
        string CertType,
        string OriginCriteria,
        IReadOnlyList<ApiCustomsCooOptionDto> Options);

    public sealed record ApiCustomsCooEditorOptionsResponse(
        IReadOnlyList<ApiCustomsCooOptionDto> ApplyTypeOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> CertStatusOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> CertTypeOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> ProducerSecretOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> ExhibitFlagOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> ThirdPartyInvoiceOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> PredictFlagOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> PromiseOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> CurrencyOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> CooTradeModeOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> GoodsItemFlagOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> PackTypeOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> GoodsTaxRateOptions,
        IReadOnlyList<ApiCustomsCooOptionDto> PackUnitOptions,
        IReadOnlyList<ApiCustomsCooOriginCriteriaOptionSetDto> OriginCriteriaOptionSets,
        IReadOnlyList<ApiCustomsCooOriginCriteriaOptionSetDto> OriginCriteriaSubOptionSets,
        string StoragePolicy);

    public sealed record ApiSingleWindowRepairGroupsRequest(
        IReadOnlyList<string> GroupKeys);

    public sealed record ApiSingleWindowRepairGroupsResponse(
        bool Success,
        int RepairedGroupCount,
        SingleWindowExportReview Review,
        string Message);

    public sealed record ApiSingleWindowSubmitPackageRequest(
        string PackagePath);

    public sealed record ApiSingleWindowImportPackageRequest(
        string PackagePath,
        string WorkingDirectory,
        bool KeepWorkingDirectory);

    public sealed record ApiSingleWindowReceiptPackageExportRequest(
        string BusinessType,
        string BatchReference,
        string InvoiceNo,
        IReadOnlyList<string> ReceiptFiles,
        string PackagePath);

    public sealed record ApiSingleWindowClientProfileDto(
        int Id,
        string ProfileName,
        string MachineName,
        string ImportRootPath,
        string ReceiptRootPath,
        string BusinessDirectoryOverridesJson,
        bool CanSubmitCustomsCoo,
        bool CanSubmitAgentConsignment,
        bool IsEnabled,
        DateTime UpdatedAt);

    public sealed record ApiSingleWindowClientProfileResponse(
        ApiSingleWindowClientProfileDto Profile,
        string StoragePolicy);

    public sealed record ApiSingleWindowClientProfileSaveRequest(
        string ImportRootPath,
        string ReceiptRootPath,
        string BusinessType);

    public sealed record ApiSingleWindowClientProfileSaveResponse(
        bool Success,
        int Id,
        ApiSingleWindowClientProfileDto Profile,
        string StoragePolicy,
        string Message);

    public sealed record ApiSingleWindowClientDispatchRequest(
        int BatchId,
        string ImportRootPath,
        string ProfileName);

    public sealed record ApiSingleWindowReceiptCollectionRequest(
        int BatchId,
        string ReceiptRootPath);

    public sealed record ApiSingleWindowHandoffPackageResponse(
        bool Success,
        string PackagePath,
        SingleWindowPackageManifest Manifest,
        int? TrackingBatchId,
        string StoragePolicy,
        string Message);

    public sealed record ApiSingleWindowImportedPackageResponse(
        bool Success,
        string PackagePath,
        string WorkingDirectory,
        bool WorkingDirectoryKept,
        SingleWindowPackageManifest Manifest,
        IReadOnlyList<SingleWindowReceiptParseResult> ParsedReceipts,
        int? TrackingBatchId,
        string TrackingStatus,
        int PersistedReceiptCount,
        string StoragePolicy,
        string Message);
}
