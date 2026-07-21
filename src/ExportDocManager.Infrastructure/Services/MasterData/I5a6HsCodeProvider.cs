using ExportDocManager.Models.Entities;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Utils;
using Microsoft.Playwright;

namespace ExportDocManager.Services.MasterData
{
    public sealed class I5a6HsCodeProvider : IHsCodeRemoteProvider, IDisposable
    {
        private const string BaseUrl = "https://www.i5a6.com";
        private readonly HttpClient _httpClient;
        private readonly ManagedPlaywrightBrowserHost _browserHost;

        public I5a6HsCodeProvider(ManagedPlaywrightBrowserHost browserHost)
        {
            _httpClient = CreateHttpClient();
            _browserHost = browserHost ?? throw new ArgumentNullException(nameof(browserHost));
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.7");
            return client;
        }

        public string Name => "i5a6";
        public int Priority => 100;

        public bool CanHandleDetailUrl(string detailUrl) =>
            Uri.TryCreate(detailUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "www.i5a6.com", StringComparison.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<HsCode>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
        {
            var bundle = await SearchEvidenceAsync(keyword, cancellationToken).ConfigureAwait(false);
            return bundle.Records.Select(record => record.Item).ToList();
        }

        public async Task<HsCode> FetchDetailAsync(HsCode item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            var record = new HsCodeRemoteSearchRecord(
                item,
                HsCodeRemoteRecordKind.StandardCode,
                false,
                null,
                string.Empty,
                item.DetailUrl ?? string.Empty,
                DateTimeOffset.UtcNow);
            var bundle = await FetchDetailEvidenceAsync(record, cancellationToken).ConfigureAwait(false);
            if (bundle.IsExpired) throw new HsCodeRemoteExpiredException(bundle.RecommendedKeywords);
            return bundle.Item;
        }

        public async Task<HsCodeRemoteSearchBundle> SearchEvidenceAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            string query = (keyword ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query)) return HsCodeRemoteSearchBundle.Empty(query, Name);
            try
            {
                var bundle = await SearchStaticEvidenceCoreAsync(
                    query,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    depth: 0,
                    cancellationToken).ConfigureAwait(false);
                if (bundle.Records.Count > 0) return bundle;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
            }

            return await _browserHost.ExecuteAsync(
                (page, ct) => SearchWithBrowserEvidenceAsync(page, query, ct),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<HsCodeRemoteDetailBundle> FetchDetailEvidenceAsync(
            HsCodeRemoteSearchRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!CanHandleDetailUrl(record.Item?.DetailUrl))
                throw new ArgumentException("i5a6 Provider 只能处理受信任的 HTTPS 详情地址。", nameof(record));
            try
            {
                string html = await GetHtmlWithRetryAsync(record.Item.DetailUrl, cancellationToken).ConfigureAwait(false);
                return I5a6PageParser.ParseDetailPage(
                    html,
                    record.Item,
                    record.InstanceCount,
                    record.EvidenceUrl,
                    DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return await _browserHost.ExecuteAsync(
                    (page, ct) => FetchDetailWithBrowserEvidenceAsync(page, record, ct),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<HsCodeRemoteSourceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            DateTimeOffset checkedAt = DateTimeOffset.Now;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/hscode/");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                return new HsCodeRemoteSourceHealth(Name, response.IsSuccessStatusCode, checkedAt,
                    response.IsSuccessStatusCode
                        ? $"i5a6 静态查询通道可访问；{_browserHost.GetAvailabilityMessage()}"
                        : $"i5a6 返回 HTTP {(int)response.StatusCode}；{_browserHost.GetAvailabilityMessage()}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new HsCodeRemoteSourceHealth(Name, false, checkedAt, $"i5a6 当前不可用：{ex.Message}");
            }
        }

        private async Task<HsCodeRemoteSearchBundle> SearchStaticEvidenceCoreAsync(
            string keyword,
            HashSet<string> visitedKeywords,
            int depth,
            CancellationToken cancellationToken)
        {
            if (depth > 3 || !visitedKeywords.Add(keyword)) return HsCodeRemoteSearchBundle.Empty(keyword, Name);
            string url = $"{BaseUrl}/hscode/key/{System.Net.WebUtility.UrlEncode(keyword)}";
            string html = await GetHtmlWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
            var current = I5a6PageParser.ParseSearchPage(html, keyword, Name, DateTimeOffset.UtcNow);
            var records = current.Records.ToList();
            var replacements = current.ReplacementEvidence.ToList();
            foreach (string recommendedKeyword in current.ReplacementEvidence
                         .SelectMany(item => item.RecommendedKeywords)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (visitedKeywords.Contains(recommendedKeyword)) continue;
                var nested = await SearchStaticEvidenceCoreAsync(
                    recommendedKeyword,
                    visitedKeywords,
                    depth + 1,
                    cancellationToken).ConfigureAwait(false);
                records.AddRange(nested.Records);
                replacements.AddRange(nested.ReplacementEvidence);
            }
            return MergeBundle(keyword, records, replacements);
        }

        private static async Task<HsCodeRemoteSearchBundle> SearchWithBrowserEvidenceAsync(
            IPage page,
            string keyword,
            CancellationToken cancellationToken)
        {
            string url = $"{BaseUrl}/hscode/key/{System.Net.WebUtility.UrlEncode(keyword)}";
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForSemanticContentAsync(page, detailPage: false).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string html = await page.ContentAsync().ConfigureAwait(false);
            return I5a6PageParser.ParseSearchPage(html, keyword, "i5a6", DateTimeOffset.UtcNow);
        }

        internal static async Task<IReadOnlyList<HsCode>> ParseSearchPageAsync(IPage page)
        {
            string html = await page.ContentAsync().ConfigureAwait(false);
            const string source = "i5a6（浏览器降级）";
            var bundle = I5a6PageParser.ParseSearchPage(html, string.Empty, source);
            var records = bundle.Records.Count > 0
                ? bundle.Records
                : I5a6PageParser.ParseLegacySimpleTable(html, source);
            return records
                .Where(record => !record.IsExpired)
                .Select(record => record.Item)
                .ToList();
        }

        private static async Task<HsCodeRemoteDetailBundle> FetchDetailWithBrowserEvidenceAsync(
            IPage page,
            HsCodeRemoteSearchRecord record,
            CancellationToken cancellationToken)
        {
            await page.GotoAsync(record.Item.DetailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForSemanticContentAsync(page, detailPage: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string html = await page.ContentAsync().ConfigureAwait(false);
            return I5a6PageParser.ParseDetailPage(html, record.Item, record.InstanceCount, record.EvidenceUrl, DateTimeOffset.UtcNow);
        }

        private async Task<string> GetHtmlWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 2) await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                }
            }
            throw new HttpRequestException("i5a6 静态页面连续两次读取失败。", lastError);
        }

        private static async Task WaitForSemanticContentAsync(IPage page, bool detailPage)
        {
            string expression = detailPage
                ? "() => document.body && (/商品编码|申报要素|已作废/.test(document.body.innerText || ''))"
                : "() => document.body && (document.querySelectorAll('table tr').length > 1 || document.querySelectorAll('a[href*=\"/hscode/detail/\"]').length > 0 || /(?:0\\s*条|未找到|无相关)/.test(document.body.innerText || ''))";
            try
            {
                await page.WaitForFunctionAsync(expression, null, new PageWaitForFunctionOptions { Timeout = 4_000 }).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The parser still receives the final DOM and applies its own quality gate.
            }
        }

        private static HsCodeRemoteSearchBundle MergeBundle(
            string query,
            IEnumerable<HsCodeRemoteSearchRecord> records,
            IEnumerable<HsCodeRemoteReplacementEvidence> replacements)
        {
            var deduplicatedRecords = (records ?? Enumerable.Empty<HsCodeRemoteSearchRecord>())
                .Where(record => record?.Item != null && !string.IsNullOrWhiteSpace(record.Item.Code))
                .GroupBy(record => string.Join("|", record.Kind, HsCodeTextHelper.NormalizeCode(record.Item.Code), record.Item.Name?.Trim(), record.Item.Description?.Trim()), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var deduplicatedReplacements = (replacements ?? Enumerable.Empty<HsCodeRemoteReplacementEvidence>())
                .Where(item => !string.IsNullOrWhiteSpace(item.OldCode))
                .GroupBy(item => item.OldCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            return new HsCodeRemoteSearchBundle(query, "i5a6", deduplicatedRecords, deduplicatedReplacements);
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
