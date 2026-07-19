using ExportDocManager.Models.Entities;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Utils;
using Microsoft.Playwright;

namespace ExportDocManager.Services.MasterData
{
    public sealed class I5a6HsCodeProvider : IHsCodeRemoteProvider, IDisposable
    {
        private const string BaseUrl = "https://www.i5a6.com";
        private readonly HsCodeService _staticScraper;
        private readonly HttpClient _httpClient;
        private readonly ManagedPlaywrightBrowserHost _browserHost;

        public I5a6HsCodeProvider(ManagedPlaywrightBrowserHost browserHost)
        {
            _httpClient = CreateHttpClient();
            _staticScraper = new HsCodeService(null, null, _httpClient);
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AppleWebKit/537.36 Chrome/151.0 ExportDocManager-HsCode");
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
            string query = (keyword ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query)) return [];
            var staticRows = await _staticScraper.SearchI5a6DirectAsync(query).ConfigureAwait(false);
            if (staticRows.Count > 0) return staticRows;
            return await _browserHost.ExecuteAsync(
                (page, ct) => SearchWithBrowserAsync(page, query, ct),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<HsCode> FetchDetailAsync(HsCode item, CancellationToken cancellationToken = default)
        {
            if (!CanHandleDetailUrl(item?.DetailUrl))
                throw new ArgumentException("i5a6 Provider 只能处理受信任的 HTTPS 详情地址。", nameof(item));
            try
            {
                return await _staticScraper.FetchI5a6DetailDirectAsync(item).ConfigureAwait(false);
            }
            catch (HsCodeRemoteExpiredException)
            {
                throw;
            }
            catch (HsCodeService.DetailFetchFailedException)
            {
                return await _browserHost.ExecuteAsync(
                    (page, ct) => FetchDetailWithBrowserAsync(page, item, ct),
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

        private static async Task<IReadOnlyList<HsCode>> SearchWithBrowserAsync(IPage page, string keyword, CancellationToken cancellationToken)
        {
            string url = $"{BaseUrl}/hscode/key/{System.Net.WebUtility.UrlEncode(keyword)}";
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await page.WaitForTimeoutAsync(500).ConfigureAwait(false);
            return await ParseSearchPageAsync(page).ConfigureAwait(false);
        }

        internal static async Task<IReadOnlyList<HsCode>> ParseSearchPageAsync(IPage page)
        {
            var rowsJson = await page.EvaluateAsync<System.Text.Json.JsonElement>(
                """
                () => {
                  const tables = Array.from(document.querySelectorAll('table'));
                  const table = tables.find(x => /HS编码|商品编码/.test(x.textContent || ''));
                  if (!table) return [];
                  const allRows = Array.from(table.querySelectorAll('tr'));
                  if (!allRows.length) return [];
                  const headers = Array.from(allRows[0].querySelectorAll('th,td')).map(x => (x.textContent || '').trim());
                  let codeIndex = headers.findIndex(x => /HS编码|商品编码|Code/i.test(x));
                  let nameIndex = headers.findIndex(x => /商品名称|品名|Name/i.test(x));
                  if (codeIndex < 0) codeIndex = 0;
                  if (nameIndex < 0) nameIndex = 1;
                  return allRows.slice(1).map(row => {
                    const cells = Array.from(row.querySelectorAll('td'));
                    const codeCell = cells[codeIndex];
                    const link = codeCell?.querySelector('a');
                    return {
                      code: (codeCell?.textContent || '').trim(),
                      name: (cells[nameIndex]?.textContent || '').trim(),
                      description: (cells[nameIndex]?.getAttribute('title') || '').trim(),
                      detailUrl: link ? link.href : ''
                    };
                  }).filter(x => x.code && !/作废/.test(x.code + x.name)).slice(0, 50);
                }
                """).ConfigureAwait(false);
            var rows = System.Text.Json.JsonSerializer.Deserialize<List<BrowserSearchRow>>(
                rowsJson.GetRawText(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            return rows.Select(row => new HsCode
                {
                    Code = HsCodeTextHelper.NormalizeCode(row.Code),
                    Name = row.Name ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    DetailUrl = NormalizeDetailUrl(row.DetailUrl),
                    Status = "Active",
                    SourceName = "i5a6（浏览器降级）",
                    LastVerifiedAt = DateTime.Now,
                    UpdateTime = DateTime.Now
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .DistinctBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<HsCode> FetchDetailWithBrowserAsync(
            IPage page,
            HsCode item,
            CancellationToken cancellationToken)
        {
            await page.GotoAsync(item.DetailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await page.WaitForTimeoutAsync(500).ConfigureAwait(false);
            string bodyText = await page.Locator("body").InnerTextAsync().ConfigureAwait(false);
            if (HsCodeTextHelper.IsExpiredText(bodyText))
            {
                string html = await page.ContentAsync().ConfigureAwait(false);
                throw new HsCodeRemoteExpiredException(HsCodeService.ExtractRecommendedSearchKeywords(html));
            }

            var fieldsJson = await page.EvaluateAsync<System.Text.Json.JsonElement>(
                """
                () => {
                  const result = {};
                  for (const row of Array.from(document.querySelectorAll('tr'))) {
                    const cells = Array.from(row.querySelectorAll('th,td'));
                    if (cells.length < 2) continue;
                    const label = (cells[0].textContent || '').replace(/[：:]/g, '').trim();
                    const value = (cells[1].textContent || '').trim();
                    if (label && value && !result[label]) result[label] = value;
                  }
                  return result;
                }
                """).ConfigureAwait(false);
            var fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fieldsJson.GetRawText())
                ?? new Dictionary<string, string>();
            string Get(params string[] labels) => labels.Select(label => fields.GetValueOrDefault(label)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            string code = Get("商品编码", "HS编码");
            string name = Get("商品名称", "品名");
            string elements = Get("申报要素", "规范申报要素");
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(elements))
                throw new InvalidOperationException("受控浏览器已打开详情页，但未解析到有效HS字段。网页可能已改版。");

            item.Code = string.IsNullOrWhiteSpace(code) ? item.Code : HsCodeTextHelper.NormalizeCode(code);
            item.Name = string.IsNullOrWhiteSpace(name) ? item.Name : name;
            item.Elements = elements;
            item.Unit = Get("法定第一单位", "第一法定单位");
            item.RebateRate = Get("出口退税率", "退税率");
            item.SupervisionConditions = Get("海关监管条件", "监管条件");
            item.InspectionCategory = Get("检验检疫类别", "检验检疫");
            item.Description = Get("英文名称", "英文品名") ?? item.Description;
            item.Status = "Active";
            item.SourceName = "i5a6（浏览器降级）";
            item.LastVerifiedAt = DateTime.Now;
            item.UpdateTime = DateTime.Now;
            return item;
        }

        private static string NormalizeDetailUrl(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return string.Empty;
            if (href.StartsWith("//", StringComparison.Ordinal)) return "https:" + href;
            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)) return absolute.ToString();
            return BaseUrl + (href.StartsWith('/') ? href : "/" + href);
        }

        private sealed class BrowserSearchRow
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string DetailUrl { get; set; }
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
