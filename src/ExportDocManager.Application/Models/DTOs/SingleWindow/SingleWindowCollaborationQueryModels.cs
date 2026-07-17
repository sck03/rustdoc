namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowCollaborationPageQuery
    {
        public string BusinessType { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Keyword { get; init; } = string.Empty;

        public int PageNumber { get; init; } = 1;

        public int PageSize { get; init; } = 50;

        public bool IncludeDisabledWorkstations { get; init; }
    }

    public sealed class SingleWindowCollaborationPageResult
    {
        public IReadOnlyList<SingleWindowOperationTicketRow> Tickets { get; init; } = [];

        public IReadOnlyList<SingleWindowWorkstationRow> Workstations { get; init; } = [];

        public int TotalTicketCount { get; init; }

        public int PageNumber { get; init; } = 1;

        public int PageSize { get; init; } = 50;
    }
}
