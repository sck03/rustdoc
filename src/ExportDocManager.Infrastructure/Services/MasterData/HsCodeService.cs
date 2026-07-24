using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using Serilog;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService : IHsCodeService, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IHsCodeReadRepository _hsCodeReadRepository;
        private readonly SemaphoreSlim _detailFetchSemaphore = new SemaphoreSlim(1, 1);
        private readonly IReadOnlyList<IHsCodeRemoteProvider> _remoteProviders;

        // 海关标准计量单位代码映射表 (Customs Unit Code Map)
        private static readonly Dictionary<string, string> CustomsUnitMap = new Dictionary<string, string>
        {
            { "001", "台" }, { "002", "座" }, { "003", "辆" }, { "004", "艘" }, { "005", "架" },
            { "006", "套" }, { "007", "个" }, { "008", "只" }, { "009", "头" }, { "010", "张" },
            { "011", "件" }, { "012", "支" }, { "013", "根" }, { "014", "条" }, { "015", "把" },
            { "016", "块" }, { "017", "卷" }, { "018", "副" }, { "019", "枚" }, { "020", "吊" },
            { "021", "双" }, { "022", "对" }, { "023", "箱" }, { "025", "桶" }, { "026", "扎" },
            { "027", "包" }, { "028", "筐" }, { "029", "罗" }, { "030", "匹" }, { "031", "册" },
            { "032", "本" }, { "033", "格" }, { "034", "筒" }, { "035", "千克" }, { "036", "克" },
            { "037", "毫克" }, { "038", "吨" }, { "039", "公担" }, { "044", "厘米" }, { "045", "毫米" },
            { "046", "米" }, { "047", "千米" }, { "048", "英尺" }, { "049", "英寸" }, { "050", "码" },
            { "063", "千瓦时" }, { "070", "升" }, { "071", "毫升" }, { "072", "微升" }, { "095", "升" },
            { "096", "毫升" }, { "097", "微升" }, { "110", "平方米" }, { "111", "平方英尺" }, { "112", "平方码" },
            { "115", "立方米" }, { "116", "立方英尺" }, { "120", "立方厘米" }, { "121", "立方毫米" }, { "126", "立方米" },
            { "132", "升" }, { "133", "毫升" }, { "134", "微升" }, { "135", "升" }, { "136", "毫升" },
            { "137", "微升" }, { "138", "升" }, { "139", "毫升" }, { "140", "微升" }, { "141", "升" },
            { "142", "毫升" }, { "143", "微升" }, { "144", "升" }, { "145", "毫升" }, { "146", "微升" },
            { "147", "升" }, { "148", "毫升" }, { "149", "微升" }, { "163", "克拉" }
        };

        public HsCodeService(IDbContextFactory<AppDbContext> dbContextFactory, IHsCodeReadRepository hsCodeReadRepository)
            : this(dbContextFactory, hsCodeReadRepository, Enumerable.Empty<IHsCodeRemoteProvider>())
        {
        }

        public HsCodeService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IHsCodeReadRepository hsCodeReadRepository,
            IEnumerable<IHsCodeRemoteProvider> remoteProviders)
        {
            _dbContextFactory = dbContextFactory;
            _hsCodeReadRepository = hsCodeReadRepository;
            _remoteProviders = (remoteProviders ?? Enumerable.Empty<IHsCodeRemoteProvider>())
                .OrderBy(provider => provider.Priority)
                .ToList();
        }

        private IHsCodeReadRepository GetReadRepository()
        {
            return _hsCodeReadRepository ?? throw new InvalidOperationException("HS 编码读仓未配置。");
        }

        private async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            if (_dbContextFactory == null)
            {
                throw new InvalidOperationException("HS 编码数据库上下文未配置。");
            }

            return await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        }

        public async Task<List<HsCode>> SearchSmartAsync(string keyword)
        {
            var localTask = SafeSearchAsync(() => SearchAsync(keyword), "本地 HS 搜索");
            var remoteTask = SafeSearchAsync(() => SearchRemoteAsync(keyword), "远程 HS 搜索");

            await Task.WhenAll(localTask, remoteTask);

            return MergeSearchResults(
                await localTask,
                await remoteTask);
        }

        public async Task<List<HsCode>> SearchAsync(string keyword)
        {
            var normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                // An empty smart-search query must never materialize the entire shared
                // HS catalogue. The paged list endpoint is the explicit browse path.
                return [];
            }
            var rows = await GetReadRepository().QueryAsync(new HsCodeReadQuery
            {
                Keyword = normalizedKeyword,
                MaxCount = 100,
                ReturnAll = false,
                PageSize = 100
            });
            return DeduplicateByCode(rows).ToList();
        }

        public async Task<HsCode> GetByCodeAsync(string code)
        {
            var normalizedCode = HsCodeTextHelper.NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return null;
            }

            return await GetReadRepository().GetByCodeAsync(normalizedCode);
        }

        public async Task<HsCodeRemoteSourceHealth> GetRemoteSourceHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_remoteProviders.Count == 0)
            {
                return new HsCodeRemoteSourceHealth(
                    "未配置",
                    false,
                    DateTimeOffset.Now,
                    "未配置 HS 编码联网数据源 Provider。");
            }

            var results = new List<HsCodeRemoteSourceHealth>();
            foreach (var provider in _remoteProviders)
                results.Add(await provider.CheckHealthAsync(cancellationToken).ConfigureAwait(false));
            bool available = results.Any(result => result.Available);
            return new HsCodeRemoteSourceHealth(
                string.Join(", ", results.Select(result => result.Source)),
                available,
                DateTimeOffset.Now,
                string.Join("；", results.Select(result => result.Message)));
        }

        private async Task<List<HsCode>> SafeSearchAsync(Func<Task<List<HsCode>>> search, string searchName)
        {
            try
            {
                return await search() ?? [];
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{SearchName} 失败，已按降级结果继续。", searchName);
                return [];
            }
        }

        private static List<HsCode> MergeSearchResults(
            IEnumerable<HsCode> localResults,
            IEnumerable<HsCode> remoteResults)
        {
            var mergedResults = new List<HsCode>();
            AppendSearchResults(mergedResults, localResults);
            AppendSearchResults(mergedResults, remoteResults);
            return mergedResults;
        }

        private static void AppendSearchResults(
            List<HsCode> target,
            IEnumerable<HsCode> source)
        {
            ArgumentNullException.ThrowIfNull(target);

            var existingCodes = new HashSet<string>(
                target
                    .Select(item => HsCodeTextHelper.NormalizeCode(item?.Code))
                    .Where(code => !string.IsNullOrWhiteSpace(code)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in source ?? Enumerable.Empty<HsCode>())
            {
                var normalizedCode = HsCodeTextHelper.NormalizeCode(item?.Code);
                if (item == null ||
                    string.IsNullOrWhiteSpace(normalizedCode) ||
                    HsCodeTextHelper.IsExpired(item) ||
                    !existingCodes.Add(normalizedCode))
                {
                    continue;
                }

                item.Code = normalizedCode;
                target.Add(item);
            }
        }

        private static List<HsCode> FilterReplacementResults(HsCode originalItem, IEnumerable<HsCode> candidateResults)
        {
            var originalCode = HsCodeTextHelper.NormalizeCode(originalItem?.Code);
            var filteredResults = new List<HsCode>();

            foreach (var candidate in candidateResults ?? Enumerable.Empty<HsCode>())
            {
                var normalizedCode = HsCodeTextHelper.NormalizeCode(candidate?.Code);
                if (candidate == null ||
                    string.IsNullOrWhiteSpace(normalizedCode) ||
                    HsCodeTextHelper.IsExpired(candidate) ||
                    string.Equals(normalizedCode, originalCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (filteredResults.Any(item => string.Equals(HsCodeTextHelper.NormalizeCode(item.Code), normalizedCode, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                filteredResults.Add(candidate);
            }

            return filteredResults;
        }

        private static List<HsCode> DeduplicateByCode(IEnumerable<HsCode> items)
        {
            var deduplicatedItems = (items ?? Enumerable.Empty<HsCode>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !HsCodeTextHelper.IsExpired(item))
                .DistinctBy(item => HsCodeTextHelper.NormalizeCode(item.Code), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in deduplicatedItems)
            {
                item.Code = HsCodeTextHelper.NormalizeCode(item.Code);
            }

            return deduplicatedItems;
        }

        public void Dispose()
        {
            _detailFetchSemaphore.Dispose();
        }
    }
}
