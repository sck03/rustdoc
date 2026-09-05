namespace ExportDocManager.Services.EmailTemplates
{
    public sealed record EmailTemplateRecord(
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

    public sealed record EmailTemplateDraftRequest(
        int Id,
        string Name,
        string Category,
        string Subject,
        string BodyHtml,
        int ExpectedVersion = 0);

    public sealed record EmailTemplateShareRequest(string ShareScope, int ExpectedVersion);

    public sealed record EmailTemplateVariableRecord(string Key, string Token, string Label, string SampleValue);

    public sealed record EmailTemplatePreviewRequest(
        string Subject, string BodyHtml, IReadOnlyDictionary<string, string> Variables);

    public sealed record EmailTemplatePreview(string Subject, string BodyHtml, IReadOnlyList<string> UnresolvedTokens);

    public sealed record EmailTemplateVersionRecord(
        int Id, int EmailTemplateId, int VersionNumber, string ChangeType, string Name, string Category,
        string Subject, string BodyHtml, string Status, string ShareScope, string ChangedBy,
        DateTimeOffset CreatedAt, bool CanRestore);

    public interface IEmailTemplateService
    {
        Task<IReadOnlyList<EmailTemplateRecord>> ListAsync(string? keyword, string? category, bool includeArchived, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> SaveDraftAsync(EmailTemplateDraftRequest request, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> PublishAsync(int id, int expectedVersion, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> ShareAsync(int id, EmailTemplateShareRequest request, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> DisableAsync(int id, int expectedVersion, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> RestoreAsync(int id, int expectedVersion, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> ArchiveAsync(int id, int expectedVersion, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<EmailTemplateVersionRecord>> ListVersionsAsync(int id, CancellationToken cancellationToken = default);
        Task<EmailTemplateRecord> RestoreVersionAsync(int id, int versionNumber, int expectedVersion, CancellationToken cancellationToken = default);
        IReadOnlyList<EmailTemplateVariableRecord> ListVariables();
        EmailTemplatePreview Preview(EmailTemplatePreviewRequest request);
    }
}
