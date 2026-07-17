namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiEmailTemplateDto(
        int Id, string Name, string Category, string Subject, string BodyHtml, bool IsActive, bool IsShared,
        int VersionNumber, bool CanEdit);

    public sealed record ApiEmailTemplateSaveRequest(
        int Id, string Name, string Category, string Subject, string BodyHtml, bool IsActive, bool IsShared);

    public sealed record ApiEmailTemplateVariableDto(string Key, string Token, string Label, string SampleValue);

    public sealed record ApiEmailTemplatePreviewRequest(
        string Subject, string BodyHtml, IReadOnlyDictionary<string, string> Variables);

    public sealed record ApiEmailTemplatePreviewDto(
        string Subject, string BodyHtml, IReadOnlyList<string> UnresolvedTokens);

    public sealed record ApiEmailTemplateVersionDto(
        int Id, int EmailTemplateId, int VersionNumber, string ChangeType, string Name, string Category,
        string Subject, string BodyHtml, bool IsActive, bool IsShared, string ChangedBy,
        DateTimeOffset CreatedAt, bool CanRestore);
}
