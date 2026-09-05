namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSalesOpportunityDto(
        int Id, int CrmCustomerId, string CustomerName, int? ProductId, string ProductCode,
        string ProductName, string Title, string Stage, string QuotationNo, decimal EstimatedAmount,
        string Currency, int ProbabilityPercent, DateOnly? ExpectedCloseDate, string NextAction, string Notes,
        int VersionNumber, IReadOnlyList<string> AllowedNextStages);

    public sealed record ApiSalesOpportunitySaveRequest(
        int Id, int CrmCustomerId, int? ProductId = null, string Title = "", string QuotationNo = "",
        decimal EstimatedAmount = 0, string Currency = "USD", int ProbabilityPercent = 0,
        DateOnly? ExpectedCloseDate = null, string NextAction = "", string Notes = "", string ChangeNote = "",
        int ExpectedVersion = 0);

    public sealed record ApiSalesOpportunityTransitionRequest(
        string NextStage,
        string ChangeNote,
        int ExpectedVersion);

    public sealed record ApiSalesOpportunityLifecycleRequest(int ExpectedVersion);

    public sealed record ApiSalesOpportunityHistoryDto(
        int Id, int SalesOpportunityId, int VersionNumber, string ChangeType, string Stage,
        string QuotationNo, decimal EstimatedAmount, string Currency, int ProbabilityPercent,
        DateOnly? ExpectedCloseDate, string ChangeNote, string ChangedBy, DateTimeOffset CreatedAt);
}
