namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiUserReportTemplateDto(
        int Id,
        string ReportType,
        string Name,
        string ContentHtml,
        string Status,
        string ShareScope,
        int VersionNumber,
        bool CanEdit,
        bool CanPublish,
        bool CanShare,
        bool CanDisable,
        bool CanRestore,
        bool CanArchive,
        int? OwnerUserId);

    public sealed record ApiUserReportTemplateVersionDto(
        int Id,
        int UserReportTemplateId,
        int VersionNumber,
        string ChangeType,
        string Name,
        string ContentHtml,
        string Status,
        string ShareScope,
        string ChangedBy,
        DateTimeOffset CreatedAt,
        bool CanRestore);

    public sealed record ApiUserReportTemplateCreateRequest(
        string ReportType = "",
        string Name = "",
        string ContentHtml = "");

    public sealed record ApiUserReportTemplateDraftRequest(
        string ReportType = "",
        string Name = "",
        string ContentHtml = "",
        int ExpectedVersion = 0);

    public sealed record ApiUserReportTemplateCloneRequest(
        string ReportType = "",
        string Name = "",
        string SourceTemplatePath = "");

    public sealed record ApiUserReportTemplateLifecycleRequest(int ExpectedVersion);

    public sealed record ApiUserReportTemplateShareRequest(string ShareScope, int ExpectedVersion);
}
