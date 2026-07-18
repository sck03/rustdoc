using ExportDocManager.Models;

namespace ExportDocManager.Services.Suppliers
{
    public sealed record SupplierRecord(
        int Id, string Name, string CountryRegion, string Category, string Website,
        string Status, string MainProducts, string Notes, int VersionNumber);

    public sealed record SupplierContactRecord(
        int Id, int SupplierCompanyId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary, int VersionNumber);

    public sealed record SupplierSaveRequest(
        int Id, string Name, string CountryRegion, string Category, string Website,
        string Status, string MainProducts, string Notes, int ExpectedVersion = 0);

    public sealed record SupplierContactSaveRequest(
        int Id, int SupplierCompanyId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary, int ExpectedVersion = 0);

    public sealed record SupplierProductOptionRecord(int Id, string ProductCode, string NameCN, string NameEN);

    public sealed record SupplierProductLinkRecord(
        int Id, int SupplierCompanyId, int ProductId, string ProductCode, string ProductNameCN,
        string ProductNameEN, string SupplierProductCode, decimal ReferencePrice, string Currency,
        int LeadTimeDays, string Status, int VersionNumber);

    public sealed record SupplierProductLinkSaveRequest(
        int Id, int SupplierCompanyId, int ProductId, string SupplierProductCode,
        decimal ReferencePrice, string Currency, int LeadTimeDays, string Status, int ExpectedVersion = 0);

    public sealed record SupplierImportRow(
        int RowNumber, string Name, string CountryRegion, string Category, string Website,
        string Status, string MainProducts, string Notes, string ContactName, string ContactTitle,
        string ContactEmail, string ContactPhone, bool IsDuplicate, string Error);

    public sealed record SupplierImportPreview(
        int TotalRows, int ValidRows, int DuplicateRows, IReadOnlyList<SupplierImportRow> Rows);

    public sealed record SupplierImportResult(int CreatedSuppliers, int CreatedContacts, int SkippedRows);

    public interface ISupplierDirectoryService
    {
        Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default);
        Task<PagedResult<SupplierRecord>> QueryAsync(string keyword, string status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<SupplierRecord> SaveAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<int> UpdateStatusAsync(IReadOnlyList<int> ids, string status, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SupplierContactRecord>> ListContactsAsync(int supplierCompanyId, CancellationToken cancellationToken = default);
        Task<SupplierContactRecord> SaveContactAsync(SupplierContactSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteContactAsync(int supplierCompanyId, int id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SupplierProductOptionRecord>> SearchProductsAsync(string keyword, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SupplierProductLinkRecord>> ListProductLinksAsync(int supplierCompanyId, CancellationToken cancellationToken = default);
        Task<SupplierProductLinkRecord> SaveProductLinkAsync(SupplierProductLinkSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteProductLinkAsync(int supplierCompanyId, int id, CancellationToken cancellationToken = default);
    }

    public interface ISupplierFileService
    {
        Task<SupplierImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default);
        Task<SupplierImportResult> ImportAsync(IReadOnlyList<SupplierImportRow> rows, CancellationToken cancellationToken = default);
        Task<byte[]> ExportAsync(string keyword, string status, CancellationToken cancellationToken = default);
    }
}
