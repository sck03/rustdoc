namespace ExportDocManager.Services.Crm
{
    using ExportDocManager.Models;

    public sealed record CrmCustomerRecord(
        int Id, string Name, string CountryRegion, string Website, string Status,
        string Source, string Notes, int? LinkedDocumentCustomerId);

    public sealed record CrmContactRecord(
        int Id, int CrmCustomerId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary);

    public sealed record CrmFollowUpRecord(
        int Id, int CrmCustomerId, string CustomerName, int? CrmContactId,
        string ContactName, string Type, string Summary, string NextAction,
        DateTimeOffset FollowedUpAt, DateTimeOffset? NextFollowUpAt, bool IsCompleted,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public sealed record CrmCustomerSaveRequest(
        int Id, string Name, string CountryRegion, string Website, string Status,
        string Source, string Notes, int? LinkedDocumentCustomerId);

    public sealed record CrmContactSaveRequest(
        int Id, int CrmCustomerId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary);

    public sealed record CrmFollowUpSaveRequest(
        int Id, int CrmCustomerId, int? CrmContactId, string Type, string Summary,
        string NextAction, DateTimeOffset? FollowedUpAt, DateTimeOffset? NextFollowUpAt,
        bool IsCompleted);

    public sealed record CrmDashboardRecord(
        int CustomerCount,
        int ContactCount,
        int PendingFollowUpCount,
        int OverdueFollowUpCount,
        int DueNextSevenDaysCount,
        IReadOnlyList<CrmFollowUpRecord> UpcomingFollowUps);

    public sealed record CrmCustomerImportRow(
        int RowNumber,
        string Name,
        string CountryRegion,
        string Website,
        string Status,
        string Source,
        string Notes,
        string ContactName,
        string ContactTitle,
        string ContactEmail,
        string ContactPhone,
        bool IsDuplicate,
        string Error);

    public sealed record CrmCustomerImportPreview(
        int TotalRows,
        int ValidRows,
        int DuplicateRows,
        IReadOnlyList<CrmCustomerImportRow> Rows);

    public sealed record CrmCustomerImportResult(
        int CreatedCustomers,
        int CreatedContacts,
        int SkippedDuplicates);

    public sealed record CrmEmailVariableDraft(
        int CrmCustomerId, int? CrmContactId, string ToAddress, IReadOnlyDictionary<string, string> Variables);

    public interface ICrmService
    {
        Task<IReadOnlyList<CrmCustomerRecord>> ListCustomersAsync(CancellationToken cancellationToken = default);
        Task<PagedResult<CrmCustomerRecord>> QueryCustomersAsync(string keyword, string status, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<CrmCustomerRecord> SaveCustomerAsync(CrmCustomerSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteCustomerAsync(int id, CancellationToken cancellationToken = default);
        Task<int> UpdateCustomerStatusAsync(IReadOnlyList<int> ids, string status, CancellationToken cancellationToken = default);
        Task<CrmEmailVariableDraft> GetEmailVariableDraftAsync(int crmCustomerId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CrmContactRecord>> ListContactsAsync(int crmCustomerId, CancellationToken cancellationToken = default);
        Task<CrmContactRecord> SaveContactAsync(CrmContactSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteContactAsync(int crmCustomerId, int id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CrmFollowUpRecord>> ListFollowUpsAsync(int? crmCustomerId, bool includeCompleted, int limit, CancellationToken cancellationToken = default);
        Task<CrmFollowUpRecord> SaveFollowUpAsync(CrmFollowUpSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteFollowUpAsync(int id, CancellationToken cancellationToken = default);
        Task<CrmDashboardRecord> GetDashboardAsync(CancellationToken cancellationToken = default);
    }

    public interface ICrmCustomerImportService
    {
        Task<CrmCustomerImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default);
        Task<CrmCustomerImportResult> ImportAsync(IReadOnlyList<CrmCustomerImportRow> rows, CancellationToken cancellationToken = default);
    }

    public interface ICrmCustomerExportService
    {
        Task<byte[]> ExportAsync(string keyword, string status, CancellationToken cancellationToken = default);
    }
}
