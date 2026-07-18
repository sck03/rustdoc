using ExportDocManager.Models;

namespace ExportDocManager.Services.Opportunities
{
    public sealed record SalesOpportunityRecord(
        int Id, int CrmCustomerId, string CustomerName, int? ProductId, string ProductCode,
        string ProductName, string Title, string Stage, string QuotationNo, decimal EstimatedAmount,
        string Currency, int ProbabilityPercent, DateTimeOffset? ExpectedCloseAt, string NextAction, string Notes,
        int VersionNumber);

    public sealed record SalesOpportunitySaveRequest(
        int Id, int CrmCustomerId, int? ProductId, string Title, string Stage, string QuotationNo,
        decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateTimeOffset? ExpectedCloseAt, string NextAction, string Notes, string ChangeNote,
        int ExpectedVersion = 0);

    public sealed record SalesOpportunityHistoryRecord(
        int Id, int SalesOpportunityId, int VersionNumber, string ChangeType, string Stage,
        string QuotationNo, decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateTimeOffset? ExpectedCloseAt, string ChangeNote, string ChangedBy, DateTimeOffset CreatedAt);

    public sealed record SalesOpportunityStageSummary(string Stage, int Count);
    public sealed record SalesOpportunityCurrencySummary(
        string Currency, int Count, decimal EstimatedAmount, decimal WeightedAmount);
    public sealed record SalesOpportunityDashboard(
        IReadOnlyList<SalesOpportunityStageSummary> Stages,
        IReadOnlyList<SalesOpportunityCurrencySummary> Currencies,
        IReadOnlyList<SalesOpportunityRecord> UpcomingClosings);

    public interface ISalesOpportunityService
    {
        Task<PagedResult<SalesOpportunityRecord>> QueryAsync(
            string keyword, string stage, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<SalesOpportunityRecord> SaveAsync(SalesOpportunitySaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SalesOpportunityHistoryRecord>> ListHistoryAsync(int opportunityId, CancellationToken cancellationToken = default);
        Task<SalesOpportunityDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    }
}
