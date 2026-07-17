using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowReferenceCatalogExcelImportService
    {
        Task<SingleWindowReferenceCatalogExcelImportPreview> PreviewImportAsync(
            Stream workbookStream,
            SingleWindowReferenceCatalogExcelImportOptions options,
            CancellationToken cancellationToken = default);
    }

    public sealed record SingleWindowReferenceCatalogExcelImportOptions(
        string CatalogKey,
        string SheetName,
        int HeaderRowNumber,
        int DataStartRowNumber,
        IReadOnlyDictionary<string, int> ColumnMap);

    public sealed record SingleWindowReferenceCatalogExcelImportPreview(
        string CatalogKey,
        string SheetName,
        IReadOnlyList<string> SheetNames,
        int HeaderRowNumber,
        int DataStartRowNumber,
        IReadOnlyList<SingleWindowReferenceCatalogExcelColumnMapping> ColumnMappings,
        SingleWindowReferenceCatalogModel Catalog,
        int RowCount);

    public sealed record SingleWindowReferenceCatalogExcelColumnMapping(
        string FieldKey,
        string Label,
        int ColumnNumber,
        bool Required);
}
