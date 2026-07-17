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
        
        /// <summary>
        /// Searches for HS codes from online source (i5a6.com).
        /// </summary>
        Task<List<HsCode>> SearchRemoteAsync(string keyword);

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
        Task<HsCode> FetchDetailAsync(HsCode hsCode);

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
}
