using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Reporting
{
    public sealed record UserReportTemplateRecord(
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

    public sealed record UserReportTemplateDraftRequest(
        int Id,
        string ReportType,
        string Name,
        string ContentHtml,
        int ExpectedVersion = 0);

    /// <summary>
    /// The API composition root resolves built-in file content before calling
    /// this command. Database template sources are resolved again by ID inside
    /// the service so their visibility cannot be forged by a client payload.
    /// Exactly one source must be supplied.
    /// </summary>
    public sealed record UserReportTemplateCloneRequest(
        string ReportType,
        string Name,
        int SourceUserTemplateId = 0,
        string ServerResolvedContentHtml = "");

    public sealed record UserReportTemplateShareRequest(string ShareScope, int ExpectedVersion);

    public sealed record UserReportTemplateVersionRecord(
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

    public sealed class UserReportTemplateConcurrencyException : ServiceConcurrencyException
    {
        public UserReportTemplateConcurrencyException(string message) : base(message) { }
        public UserReportTemplateConcurrencyException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public interface IUserReportTemplateService
    {
        Task<IReadOnlyList<UserReportTemplateRecord>> ListAsync(
            ReportDocumentType reportType,
            bool includeArchived = false,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> SaveDraftAsync(
            UserReportTemplateDraftRequest request,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> CloneAsync(
            UserReportTemplateCloneRequest request,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> PublishAsync(
            int id, int expectedVersion, CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> ShareAsync(
            int id, UserReportTemplateShareRequest request, CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> DisableAsync(
            int id, int expectedVersion, CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> RestoreAsync(
            int id, int expectedVersion, CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> ArchiveAsync(
            int id, int expectedVersion, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserReportTemplateVersionRecord>> ListVersionsAsync(
            int id,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> RestoreVersionAsync(
            int id,
            int versionNumber,
            int expectedVersion,
            CancellationToken cancellationToken = default);
    }
}
