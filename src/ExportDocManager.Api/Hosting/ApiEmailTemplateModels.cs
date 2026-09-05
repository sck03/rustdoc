namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiEmailTemplateDto(
        int Id,
        string Name,
        string Category,
        string Subject,
        string BodyHtml,
        string Status,
        string ShareScope,
        int VersionNumber,
        int? OwnerUserId,
        bool CanEdit,
        bool CanPublish,
        bool CanShare,
        bool CanDisable,
        bool CanRestore,
        bool CanArchive);

    public sealed record ApiEmailTemplateDraftRequest(
        string Name = "",
        string Category = "",
        string Subject = "",
        string BodyHtml = "",
        int ExpectedVersion = 0);

    public sealed record ApiEmailTemplateLifecycleRequest(int ExpectedVersion);

    public sealed record ApiEmailTemplateShareRequest(string ShareScope, int ExpectedVersion);

    public sealed record ApiEmailTemplateVariableDto(string Key, string Token, string Label, string SampleValue);

    public sealed record ApiEmailTemplatePreviewRequest(
        string Subject, string BodyHtml, IReadOnlyDictionary<string, string> Variables);

    public sealed record ApiEmailTemplatePreviewDto(
        string Subject, string BodyHtml, IReadOnlyList<string> UnresolvedTokens);

    public sealed record ApiEmailTemplateVersionDto(
        int Id,
        int EmailTemplateId,
        int VersionNumber,
        string ChangeType,
        string Name,
        string Category,
        string Subject,
        string BodyHtml,
        string Status,
        string ShareScope,
        string ChangedBy,
        DateTimeOffset CreatedAt,
        bool CanRestore);
}
