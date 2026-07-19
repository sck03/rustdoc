using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IHsCodeService
    {
        Task<List<HsCode>> SearchAsync(string keyword);
        Task<HsCode> GetByCodeAsync(string code);
        Task ImportAsync(string filePath);

        Task<HsCodeImportPreview> PreviewImportAsync(
            string filePath,
            HsCodeImportMode mode = HsCodeImportMode.Incremental,
            string sourceName = null,
            int? effectiveYear = null,
            CancellationToken cancellationToken = default);

        Task<HsCodeImportCommitResult> CommitImportAsync(
            HsCodeImportPreview preview,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Searches for HS codes from online source (i5a6.com).
        /// </summary>
        Task<List<HsCode>> SearchRemoteAsync(string keyword, CancellationToken cancellationToken = default);

        Task<HsCodeRemoteSourceHealth> GetRemoteSourceHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 智能搜索：优先本地，无结果则联网搜索并自动获取详情
        /// </summary>
        Task<List<HsCode>> SearchSmartAsync(string keyword);

        /// <summary>
        /// 后台逐步处理剩余条目的详情
        /// </summary>
        Task ProcessRemainingDetailsAsync(List<HsCode> items, Action<HsCode> onItemUpdated, Action<HsCode> onItemRemoved, Action<List<HsCode>> onItemsAdded = null, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches detailed information for a specific HS code from the remote source.
        /// </summary>
        Task<HsCode> FetchDetailAsync(HsCode hsCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves or updates an HS code to the local database.
        /// </summary>
        Task SaveAsync(HsCode hsCode);

        /// <summary>
        /// Deletes an HS code from the local database.
        /// </summary>
        Task DeleteAsync(int id);

        /// <summary>
        /// Deletes multiple HS codes from the local database in one batch.
        /// </summary>
        Task DeleteAsync(IEnumerable<int> ids);

        /// <summary>
        /// Gets all HS codes from the local database.
        /// </summary>
        Task<List<HsCode>> GetAllLocalAsync();

        /// <summary>
        /// Gets a paged list of HS codes from the local database.
        /// </summary>
        Task<PagedResult<HsCode>> GetPagedLocalAsync(int pageNumber, int pageSize, string keyword = null);
        
        /// <summary>
        /// Clears all HS codes from the local database.
        /// </summary>
        Task ClearAllLocalAsync();
    }

    public enum HsCodeImportMode
    {
        Incremental = 0,
        CompleteSnapshot = 1
    }

    public sealed record HsCodeImportColumnMapping(
        string Field,
        string Header,
        int ColumnNumber,
        int Confidence);

    public sealed record HsCodeImportPreviewItem(
        string ChangeType,
        int WorksheetNumber,
        string WorksheetName,
        int RowNumber,
        HsCode Item,
        IReadOnlyList<string> ChangedFields,
        IReadOnlyList<string> ReplacementCandidates,
        string Message);

    public sealed record HsCodeImportPreview(
        string FileName,
        HsCodeImportMode Mode,
        string SourceName,
        int? EffectiveYear,
        int WorksheetNumber,
        string WorksheetName,
        int HeaderRowNumber,
        int Confidence,
        IReadOnlyList<HsCodeImportColumnMapping> Columns,
        IReadOnlyList<HsCodeImportPreviewItem> Items,
        int AddCount,
        int UpdateCount,
        int UnchangedCount,
        int SuspectedObsoleteCount,
        int ConflictCount,
        int InvalidCount,
        IReadOnlyList<string> Warnings);

    public sealed record HsCodeImportCommitResult(
        int AddedCount,
        int UpdatedCount,
        int UnchangedCount,
        int SuspectedObsoleteCount,
        int SkippedCount,
        string Message);

    public sealed record HsCodeRemoteSourceHealth(
        string Source,
        bool Available,
        DateTimeOffset CheckedAt,
        string Message);

    public interface IHsCodeRemoteProvider
    {
        string Name { get; }
        int Priority { get; }
        bool CanHandleDetailUrl(string detailUrl);
        Task<IReadOnlyList<HsCode>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
        Task<HsCode> FetchDetailAsync(HsCode item, CancellationToken cancellationToken = default);
        Task<HsCodeRemoteSourceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
    }

    public sealed class HsCodeRemoteExpiredException : InvalidOperationException
    {
        public HsCodeRemoteExpiredException(IEnumerable<string> recommendedKeywords = null)
            : base("该 HS 编码已作废")
        {
            RecommendedKeywords = (recommendedKeywords ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<string> RecommendedKeywords { get; }
    }
}
