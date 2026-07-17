namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiUserReportTemplateDto(
        int Id,
        string ReportType,
        string Name,
        string ContentHtml,
        bool IsActive,
        bool IsShared,
        string ShareScope,
        int VersionNumber,
        bool CanEdit,
        int? OwnerUserId);

    public sealed record ApiUserReportTemplateVersionDto(
        int Id,
        int UserReportTemplateId,
        int VersionNumber,
        string ChangeType,
        string Name,
        string ContentHtml,
        bool IsActive,
        bool IsShared,
        string ShareScope,
        string ChangedBy,
        DateTimeOffset CreatedAt,
        bool CanRestore);

    public sealed record ApiUserReportTemplateSaveRequest(
        int Id,
        string ReportType,
        string Name,
        string ContentHtml,
        bool IsActive,
        bool IsShared,
        string ShareScope,
        int ExpectedVersion = 0,
        string SourceTemplatePath = "");
}
