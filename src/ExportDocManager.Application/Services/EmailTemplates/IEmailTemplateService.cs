namespace ExportDocManager.Services.EmailTemplates
{
    public sealed record EmailTemplateRecord(
        int Id, string Name, string Category, string Subject, string BodyHtml, bool IsActive, bool IsShared,
        int VersionNumber, bool CanEdit);

    public sealed record EmailTemplateSaveRequest(
        int Id, string Name, string Category, string Subject, string BodyHtml, bool IsActive, bool IsShared,
        int ExpectedVersion = 0);

    public sealed record EmailTemplateVariableRecord(string Key, string Token, string Label, string SampleValue);

    public sealed record EmailTemplatePreviewRequest(
        string Subject, string BodyHtml, IReadOnlyDictionary<string, string> Variables);

    public sealed record EmailTemplatePreview(string Subject, string BodyHtml, IReadOnlyList<string> UnresolvedTokens);

    public sealed record EmailTemplateVersionRecord(
        int Id, int EmailTemplateId, int VersionNumber, string ChangeType, string Name, string Category,
        string Subject, string BodyHtml, bool IsActive, bool IsShared, string ChangedBy,
        DateTimeOffset CreatedAt, bool CanRestore);

    public interface IEmailTemplateService
    {
        Task<IReadOnlyList<EmailTemplateRecord>> ListAsync(string keyword, string category, bool includeInactive, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> SaveAsync(EmailTemplateSaveRequest request, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<EmailTemplateVersionRecord>> ListVersionsAsync(int id, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> RestoreVersionAsync(int id, int versionNumber, CancellationToken cancellationToken = default);
        IReadOnlyList<EmailTemplateVariableRecord> ListVariables();
        EmailTemplatePreview Preview(EmailTemplatePreviewRequest request);
    }
}
