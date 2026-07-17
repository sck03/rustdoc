namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiDashboardResponse
    {
        public decimal MonthlyExportAmount { get; init; }

        public decimal MonthlyProfit { get; init; }

        public decimal MonthlyTaxRefund { get; init; }

        public int PendingCount { get; init; }

        public int ShippedCount { get; init; }

        public int TotalActiveCount { get; init; }

        public string SingleWindowStatusSummary { get; init; } = string.Empty;

        public IReadOnlyList<ApiDashboardRecentInvoiceDto> RecentInvoices { get; init; } = Array.Empty<ApiDashboardRecentInvoiceDto>();

        public IReadOnlyList<ApiDashboardTodoItemDto> TodoItems { get; init; } = Array.Empty<ApiDashboardTodoItemDto>();

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ApiDashboardRecentInvoiceDto
    {
        public int Id { get; init; }

        public string InvoiceNo { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string StatusText { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public DateTime InvoiceDate { get; init; }

        public decimal TotalAmount { get; init; }

        public string CustomerNameEN { get; init; } = string.Empty;
    }

    public sealed class ApiDashboardTodoItemDto
    {
        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string ActionType { get; init; } = string.Empty;

        public string ReferenceId { get; init; } = string.Empty;
    }
}
