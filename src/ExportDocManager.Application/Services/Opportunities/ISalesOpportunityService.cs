using ExportDocManager.Models;

namespace ExportDocManager.Services.Opportunities
{
    public sealed record SalesOpportunityRecord(
        int Id, int CrmCustomerId, string CustomerName, int? ProductId, string ProductCode,
        string ProductName, string Title, string Stage, string QuotationNo, decimal EstimatedAmount,
        string Currency, int ProbabilityPercent, DateOnly? ExpectedCloseDate, string NextAction, string Notes,
        int VersionNumber);

    public sealed record SalesOpportunitySaveRequest(
        int Id, int CrmCustomerId, int? ProductId, string Title, string QuotationNo,
        decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateOnly? ExpectedCloseDate, string NextAction, string Notes, string ChangeNote,
        int ExpectedVersion = 0);

    public sealed record SalesOpportunityTransitionRequest(
        string NextStage,
        string ChangeNote,
        int ExpectedVersion);

    public sealed record SalesOpportunityHistoryRecord(
        int Id, int SalesOpportunityId, int VersionNumber, string ChangeType, string Stage,
        string QuotationNo, decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateOnly? ExpectedCloseDate, string ChangeNote, string ChangedBy, DateTimeOffset CreatedAt);

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
            string? keyword, string? stage, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<SalesOpportunityRecord> SaveAsync(SalesOpportunitySaveRequest request, CancellationToken cancellationToken = default);
        Task<SalesOpportunityRecord> TransitionAsync(
            int id,
            SalesOpportunityTransitionRequest request,
            CancellationToken cancellationToken = default);
        Task<bool> ArchiveAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0);
        Task<IReadOnlyList<SalesOpportunityHistoryRecord>> ListHistoryAsync(int opportunityId, CancellationToken cancellationToken = default);
        Task<SalesOpportunityDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    }
}
