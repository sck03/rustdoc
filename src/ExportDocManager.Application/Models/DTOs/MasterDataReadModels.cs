namespace ExportDocManager.Models.DTOs
{
    public abstract record SharedKeywordReadQuery
    {
        public string Keyword { get; init; } = string.Empty;
    }

    public sealed record CustomerReadQuery : SharedKeywordReadQuery;

    public sealed record ExporterReadQuery : SharedKeywordReadQuery;

    public sealed record PayeeReadQuery : SharedKeywordReadQuery;

    public sealed record ProductReadQuery : SharedDatabasePagedQuery
    {
        public string Keyword { get; init; } = string.Empty;

        /// <summary>用于下拉/辅助查询的轻量上限；分页列表使用 PageSize。</summary>
        public int MaxCount { get; init; } = 200;

        public bool ReturnAll { get; init; }
    }

    public sealed record PortReadQuery : SharedKeywordReadQuery;

    public sealed record UnitReadQuery : SharedKeywordReadQuery;

    public sealed record HsCodeReadQuery : SharedDatabasePagedQuery
    {
        public string Keyword { get; init; } = string.Empty;

        public int MaxCount { get; init; } = 100;

        public bool ReturnAll { get; init; }
    }
}
