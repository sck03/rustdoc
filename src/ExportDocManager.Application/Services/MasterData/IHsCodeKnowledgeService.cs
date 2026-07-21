using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public sealed record HsCodeKnowledgeSearchResult(
        string CurrentCode,
        string RawCode,
        string Name,
        string Specification,
        string StandardName,
        string ResolutionStatus,
        int Score,
        int ExampleCount,
        int ConfirmedCount,
        IReadOnlyList<string> ReplacementCandidates,
        IReadOnlyList<string> MatchReasons,
        IReadOnlyList<string> ConflictWarnings,
        bool CanUse);

    public sealed record HsCodeKnowledgeSearchResponse(
        string Query,
        IReadOnlyList<HsCodeKnowledgeSearchResult> Items,
        int LocalExampleCount,
        string Message);

    public sealed record HsCodeExampleInput(
        int Id,
        string RawReportedHsCode,
        string ResolvedCurrentHsCode,
        string ProductName,
        string Specification,
        string Source,
        int? SourceYear,
        string ResolutionStatus,
        bool IsManuallyVerified);

    public sealed record HsCodeKnowledgeFeedbackInput(
        string QueryText,
        string ProductName,
        string Specification,
        string CandidateCode,
        bool Accepted);

    public sealed record HsCodeHistoryLearningCandidate(string Fingerprint, string RawCode, string CurrentCode,
        string ProductName, string Specification, string Source, int SourceCount, string ResolutionStatus,
        IReadOnlyList<string> ReplacementCandidates, bool CanConfirm);

    public sealed record HsCodeRemoteCandidateReviewInput(int Id, string CurrentCode, bool Confirmed);

    public sealed record HsCodeKnowledgePackagePreview(
        string FileName,
        string SchemaVersion,
        DateTimeOffset ExportedAt,
        int HsCodeCount,
        int ExampleCount,
        int ReplacementCount,
        int FeedbackCount,
        IReadOnlyList<HsCode> HsCodes,
        IReadOnlyList<HsCodeDeclarationExample> Examples,
        IReadOnlyList<HsCodeReplacementRelation> Replacements,
        IReadOnlyList<HsCodeSearchFeedback> Feedback,
        IReadOnlyList<string> Warnings);

    public sealed record HsCodeKnowledgeImportResult(
        int AddedHsCodes,
        int UpdatedHsCodes,
        int AddedExamples,
        int UpdatedExamples,
        int AddedReplacements,
        int AddedFeedback,
        string Message);

    public interface IHsCodeKnowledgeService
    {
        Task<HsCodeKnowledgeSearchResponse> SearchAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HsCodeDeclarationExample>> ListExamplesAsync(string keyword, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> CountExamplesAsync(string keyword, CancellationToken cancellationToken = default);
        Task<HsCodeDeclarationExample> SaveExampleAsync(HsCodeExampleInput input, CancellationToken cancellationToken = default);
        Task<bool> DeleteExampleAsync(int id, CancellationToken cancellationToken = default);
        Task RecordFeedbackAsync(HsCodeKnowledgeFeedbackInput input, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HsCodeHistoryLearningCandidate>> DiscoverHistoryCandidatesAsync(string keyword, int maxResults = 200, CancellationToken cancellationToken = default);
        Task<int> CaptureRemoteExamplesAsync(string query, IEnumerable<HsCode> remoteRows, CancellationToken cancellationToken = default);
        Task<int> CaptureRemoteEvidenceAsync(string query, HsCodeRemoteSearchBundle bundle, CancellationToken cancellationToken = default);
        Task<int> CaptureRemoteDetailEvidenceAsync(string query, HsCodeRemoteDetailBundle bundle, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HsCodeRemoteCandidate>> ListRemoteCandidatesAsync(string reviewStatus, int maxResults = 200, CancellationToken cancellationToken = default);
        Task<bool> ReviewRemoteCandidateAsync(HsCodeRemoteCandidateReviewInput input, CancellationToken cancellationToken = default);
        Task RefreshReplacementRelationsAsync(HsCodeImportPreview preview, CancellationToken cancellationToken = default);
        Task<byte[]> ExportPackageAsync(DateTimeOffset? since = null, CancellationToken cancellationToken = default);
        Task<HsCodeKnowledgePackagePreview> PreviewPackageAsync(string packagePath, CancellationToken cancellationToken = default);
        Task<HsCodeKnowledgeImportResult> ImportPackageAsync(HsCodeKnowledgePackagePreview preview, CancellationToken cancellationToken = default);
    }
}
