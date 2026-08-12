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

    public sealed record ApiHsCodeKnowledgeExamplePage(
        IReadOnlyList<ExportDocManager.Models.Entities.HsCodeDeclarationExample> Items,
        int TotalCount,
        int PageNumber,
        int PageSize);

    public sealed record ApiHsCodeKnowledgeImportResponse(
        string FileName,
        int HsCodeCount,
        int ExampleCount,
        int ReplacementCount,
        int FeedbackCount,
        IReadOnlyList<string> Warnings,
        ExportDocManager.Services.MasterData.HsCodeKnowledgeImportResult Result);
}
