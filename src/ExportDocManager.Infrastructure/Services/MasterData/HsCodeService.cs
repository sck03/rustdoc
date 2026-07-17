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
        private readonly System.Net.Http.HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

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
            : this(dbContextFactory, hsCodeReadRepository, null)
        {
        }

        public HsCodeService(IDbContextFactory<AppDbContext> dbContextFactory, IHsCodeReadRepository hsCodeReadRepository, System.Net.Http.HttpClient httpClient)
        {
            _dbContextFactory = dbContextFactory;
            _hsCodeReadRepository = hsCodeReadRepository;
            _httpClient = httpClient ?? CreateHttpClient();
            _ownsHttpClient = httpClient == null;
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
            var rows = await GetReadRepository().QueryAsync(new HsCodeReadQuery
            {
                Keyword = normalizedKeyword,
                MaxCount = 100,
                ReturnAll = string.IsNullOrWhiteSpace(normalizedKeyword),
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

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static System.Net.Http.HttpClient CreateHttpClient()
        {
            // Use SocketsHttpHandler for better control over connection pooling and SSL in .NET 5+
            var handler = new System.Net.Http.SocketsHttpHandler
            {
                // Enable all supported decompression methods (GZip, Deflate, Brotli)
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                // Limit connection lifetime to handle DNS changes and prevent stale connections
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                // Configure SSL options
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    // Ignore SSL certificate errors to prevent handshake failures
                    RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                    // Do NOT strictly force TLS versions; let the OS and Server negotiate the best protocol.
                    // Forcing Tls12|Tls13 can sometimes cause handshake failures with specific server configs.
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.None
                }
            };

            var client = new System.Net.Http.HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            // Simulate navigation from within the site (consistent with Referer)
            client.DefaultRequestHeaders.Add("Referer", "https://www.i5a6.com/");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            
            // Sec-Fetch headers matching a "same-origin" navigation (since Referer is set)
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            
            // Avoid Keep-Alive issues
            client.DefaultRequestHeaders.Connection.Add("close");
            
            // Increased timeout to 60s to handle slow network/server response
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            maxRetries = Math.Max(1, maxRetries);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {attempt} failed: {ex.Message}. Retrying...");
                    await Task.Delay(1000 * attempt); // Exponential backoff: 1s, 2s, 3s
                }
            }
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
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
