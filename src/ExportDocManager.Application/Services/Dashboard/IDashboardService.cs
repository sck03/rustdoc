namespace ExportDocManager.Services.Dashboard
{
    public interface IDashboardService
    {
        Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    }

    public sealed record DashboardSnapshot(
        decimal MonthlyExportAmount,
        decimal MonthlyProfit,
        decimal MonthlyTaxRefund,
        int PendingCount,
        int ShippedCount,
        int TotalActiveCount,
        string SingleWindowStatusSummary,
        IReadOnlyList<DashboardRecentInvoice> RecentInvoices,
        IReadOnlyList<DashboardTodoItem> TodoItems,
        string PeriodLabel,
        decimal PreviousMonthlyExportAmount,
        decimal PreviousMonthlyProfit,
        decimal PreviousMonthlyTaxRefund,
        int MonthlyInvoiceCount,
        int DraftCount,
        int VerifiedCount,
        int CompletedCount);

    public sealed record DashboardRecentInvoice(
        int Id,
        string InvoiceNo,
        string Status,
        string Type,
        DateOnly InvoiceDate,
        decimal TotalAmount,
        string CustomerNameEN);

    public sealed record DashboardTodoItem(
        string Title,
        string Description,
        string ActionType,
        string ReferenceId);
}
