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
        IReadOnlyList<DashboardTodoItem> TodoItems);

    public sealed record DashboardRecentInvoice(
        int Id,
        string InvoiceNo,
        string Status,
        string Type,
        DateTime InvoiceDate,
        decimal TotalAmount,
        string CustomerNameEN);

    public sealed record DashboardTodoItem(
        string Title,
        string Description,
        string ActionType,
        string ReferenceId);
}
