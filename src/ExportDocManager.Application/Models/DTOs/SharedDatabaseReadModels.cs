namespace ExportDocManager.Models.DTOs
{
    public abstract record SharedDatabasePagedQuery
    {
        public int PageNumber { get; init; } = 1;

        public int PageSize { get; init; } = 50;
    }

    public sealed record InvoiceListPageQuery : SharedDatabasePagedQuery
    {
        public string Keyword { get; init; } = string.Empty;

        public string SortColumn { get; init; } = string.Empty;

        public bool Ascending { get; init; }
    }

    public sealed record PaymentPageQuery : SharedDatabasePagedQuery
    {
        public string Keyword { get; init; } = string.Empty;
    }

    public sealed record QueryPageQuery : SharedDatabasePagedQuery
    {
        public DateTime? StartDate { get; init; }

        public DateTime? EndDate { get; init; }

        public int? CustomerId { get; init; }

        public int? ExporterId { get; init; }

        public string Keyword { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public string InvoiceType { get; init; } = string.Empty;

        public string TransportMode { get; init; } = string.Empty;

        public string StyleName { get; init; } = string.Empty;

        public string StyleNo { get; init; } = string.Empty;
    }

    public sealed record AuditLogPageQuery : SharedDatabasePagedQuery
    {
        public string InvoiceKeyword { get; init; } = string.Empty;

        public string EntityName { get; init; } = string.Empty;

        public string Action { get; init; } = string.Empty;

        public string UserId { get; init; } = string.Empty;

        public DateTime? StartTime { get; init; }

        public DateTime? EndTime { get; init; }

        public string Keyword { get; init; } = string.Empty;
    }
}
