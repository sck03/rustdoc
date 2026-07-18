namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiCrmCustomerDto(
        int Id, string Name, string CountryRegion, string Website, string Status,
        string Source, string Notes, int? LinkedDocumentCustomerId, int VersionNumber);

    public sealed record ApiCrmContactDto(
        int Id, int CrmCustomerId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary, int VersionNumber);

    public sealed record ApiCrmFollowUpDto(
        int Id, int CrmCustomerId, string CustomerName, int? CrmContactId,
        string ContactName, string Type, string Summary, string NextAction,
        DateTimeOffset FollowedUpAt, DateTimeOffset? NextFollowUpAt, bool IsCompleted,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int VersionNumber);

    public sealed record ApiCrmCustomerSaveRequest(
        int Id, string Name, string CountryRegion, string Website, string Status,
        string Source, string Notes, int? LinkedDocumentCustomerId, int ExpectedVersion = 0);

    public sealed record ApiCrmContactSaveRequest(
        int Id, int CrmCustomerId, string Name, string Title, string Email,
        string Phone, string InstantMessaging, bool IsPrimary, int ExpectedVersion = 0);

    public sealed record ApiCrmFollowUpSaveRequest(
        int Id, int CrmCustomerId, int? CrmContactId, string Type, string Summary,
        string NextAction, DateTimeOffset? FollowedUpAt, DateTimeOffset? NextFollowUpAt,
        bool IsCompleted, int ExpectedVersion = 0);

    public sealed record ApiCrmDashboardDto(
        int CustomerCount,
        int ContactCount,
        int PendingFollowUpCount,
        int OverdueFollowUpCount,
        int DueNextSevenDaysCount,
        IReadOnlyList<ApiCrmFollowUpDto> UpcomingFollowUps,
        IReadOnlyList<ApiSalesOpportunityStageSummaryDto> OpportunityStages,
        IReadOnlyList<ApiSalesOpportunityCurrencySummaryDto> OpportunityCurrencies,
        IReadOnlyList<ApiSalesOpportunityDto> UpcomingOpportunityClosings);

    public sealed record ApiSalesOpportunityStageSummaryDto(string Stage, int Count);
    public sealed record ApiSalesOpportunityCurrencySummaryDto(
        string Currency, int Count, decimal EstimatedAmount, decimal WeightedAmount);

    public sealed record ApiCrmCustomerImportRowDto(
        int RowNumber, string Name, string CountryRegion, string Website, string Status,
        string Source, string Notes, string ContactName, string ContactTitle,
        string ContactEmail, string ContactPhone, bool IsDuplicate, string Error);

    public sealed record ApiCrmCustomerImportPreviewDto(
        int TotalRows, int ValidRows, int DuplicateRows, IReadOnlyList<ApiCrmCustomerImportRowDto> Rows);

    public sealed record ApiCrmCustomerImportRequest(IReadOnlyList<ApiCrmCustomerImportRowDto> Rows);

    public sealed record ApiCrmCustomerImportResultDto(
        int CreatedCustomers, int CreatedContacts, int SkippedDuplicates);

    public sealed record ApiCrmCustomerBatchStatusRequest(IReadOnlyList<int> Ids, string Status);
    public sealed record ApiCrmCustomerBatchStatusResult(int AffectedCount, string Status);
    public sealed record ApiCrmEmailVariableDraftDto(
        int CrmCustomerId, int? CrmContactId, string ToAddress, IReadOnlyDictionary<string, string> Variables);
}
