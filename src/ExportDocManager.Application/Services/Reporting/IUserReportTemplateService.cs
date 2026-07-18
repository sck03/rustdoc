namespace ExportDocManager.Services.Reporting
{
    public sealed record UserReportTemplateRecord(
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

    public sealed record UserReportTemplateSaveRequest(
        int Id,
        string ReportType,
        string Name,
        string ContentHtml,
        bool IsActive,
        bool IsShared,
        string ShareScope = UserReportTemplateShareScope.Private,
        int ExpectedVersion = 0);

    public sealed record UserReportTemplateVersionRecord(
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

    public sealed class UserReportTemplateConcurrencyException : InvalidOperationException
    {
        public UserReportTemplateConcurrencyException(string message) : base(message) { }
        public UserReportTemplateConcurrencyException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public interface IUserReportTemplateService
    {
        Task<IReadOnlyList<UserReportTemplateRecord>> ListAsync(
            ReportDocumentType reportType,
            bool includeInactive = false,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> SaveAsync(
            UserReportTemplateSaveRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserReportTemplateVersionRecord>> ListVersionsAsync(
            int id,
            CancellationToken cancellationToken = default);

        Task<UserReportTemplateRecord> RestoreVersionAsync(
            int id,
            int versionNumber,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
