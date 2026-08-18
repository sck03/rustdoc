using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService : IHsCodeService, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IHsCodeReadRepository _hsCodeReadRepository;
        private readonly SemaphoreSlim _detailFetchSemaphore = new SemaphoreSlim(1, 1);
        private readonly IReadOnlyList<IHsCodeRemoteProvider> _remoteProviders;
        private readonly IHsCodeImportService _importService;
        private readonly ILogger<HsCodeService> _logger;
        private readonly IBusinessClock _clock;

        public HsCodeService(IDbContextFactory<AppDbContext> dbContextFactory, IHsCodeReadRepository hsCodeReadRepository)
            : this(dbContextFactory, hsCodeReadRepository, Enumerable.Empty<IHsCodeRemoteProvider>(), null, null)
        {
        }

        public HsCodeService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IHsCodeReadRepository hsCodeReadRepository,
            IEnumerable<IHsCodeRemoteProvider> remoteProviders,
            IHsCodeImportService? importService = null,
            ILogger<HsCodeService>? logger = null,
            IBusinessClock? clock = null)
        {
            _dbContextFactory = dbContextFactory;
            _hsCodeReadRepository = hsCodeReadRepository;
            _remoteProviders = (remoteProviders ?? Enumerable.Empty<IHsCodeRemoteProvider>())
                .OrderBy(provider => provider.Priority)
                .ToList();
            _importService = importService ?? new UnsupportedHsCodeImportService();
            _logger = logger ?? NullLogger<HsCodeService>.Instance;
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public Task ImportAsync(string filePath) => _importService.ImportAsync(filePath);

        public Task<HsCodeImportPreview> PreviewImportAsync(
            string filePath,
            HsCodeImportMode mode = HsCodeImportMode.Incremental,
            string? sourceName = null,
            int? effectiveYear = null,
            CancellationToken cancellationToken = default) =>
            _importService.PreviewImportAsync(
                filePath,
                mode,
                sourceName,
                effectiveYear,
                cancellationToken);

        public Task<HsCodeImportCommitResult> CommitImportAsync(
            HsCodeImportPreview preview,
            CancellationToken cancellationToken = default) =>
            _importService.CommitImportAsync(preview, cancellationToken);

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
            // A local database failure must remain observable to the API (503),
            // otherwise an offline database is silently presented as an empty
            // catalogue. Remote providers are optional and may still degrade
            // to an empty result when an external source is unavailable.
            var localTask = SearchAsync(keyword);
            var remoteTask = SafeRemoteSearchAsync(() => SearchRemoteAsync(keyword));

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

        public async Task<HsCode?> GetByCodeAsync(string code)
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
                    _clock.UtcNow,
                    "未配置 HS 编码联网数据源 Provider。");
            }

            var results = new List<HsCodeRemoteSourceHealth>();
            foreach (var provider in _remoteProviders)
                results.Add(await provider.CheckHealthAsync(cancellationToken).ConfigureAwait(false));
            bool available = results.Any(result => result.Available);
            return new HsCodeRemoteSourceHealth(
                string.Join(", ", results.Select(result => result.Source)),
                available,
                _clock.UtcNow,
                string.Join("；", results.Select(result => result.Message)));
        }

        private async Task<List<HsCode>> SafeRemoteSearchAsync(Func<Task<List<HsCode>>> search)
        {
            try
            {
                return await search() ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "远程 HS 搜索失败，已按降级结果继续。");
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
