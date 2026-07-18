namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSalesOpportunityDto(
        int Id, int CrmCustomerId, string CustomerName, int? ProductId, string ProductCode,
        string ProductName, string Title, string Stage, string QuotationNo, decimal EstimatedAmount,
        string Currency, int ProbabilityPercent, DateTimeOffset? ExpectedCloseAt, string NextAction, string Notes,
        int VersionNumber);

    public sealed record ApiSalesOpportunitySaveRequest(
        int Id, int CrmCustomerId, int? ProductId, string Title, string Stage, string QuotationNo,
        decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateTimeOffset? ExpectedCloseAt, string NextAction, string Notes, string ChangeNote,
        int ExpectedVersion = 0);

    public sealed record ApiSalesOpportunityHistoryDto(
        int Id, int SalesOpportunityId, int VersionNumber, string ChangeType, string Stage,
        string QuotationNo, decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateTimeOffset? ExpectedCloseAt, string ChangeNote, string ChangedBy, DateTimeOffset CreatedAt);
}
