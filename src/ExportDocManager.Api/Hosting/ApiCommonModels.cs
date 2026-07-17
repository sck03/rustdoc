namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiPagedResponse<T>(
        IReadOnlyList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    public sealed record ApiCommandResponse(bool Success, string Message);
}
