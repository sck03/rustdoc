using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Data
{
    public class BocExchangeRateService : IExchangeRateService, IDisposable
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
        private const long MaximumResponseBytes = 5L * 1024L * 1024L;
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        private readonly ISettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _cacheRefreshLock = new(1, 1);
        private readonly Lock _cacheSync = new();

        // 缓存结果以避免频繁请求
        private List<ExchangeRateInfo>? _cachedRates;
        private string _cachedRatesSignature = string.Empty;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveHostAsync;
        private readonly TimeSpan _requestTimeout;

        public BocExchangeRateService(
            ISettingsService settingsService,
            HttpClient httpClient,
            Func<string, CancellationToken, Task<IPAddress[]>>? resolveHostAsync = null,
            TimeSpan? requestTimeout = null)
        {
            ArgumentNullException.ThrowIfNull(settingsService);
            ArgumentNullException.ThrowIfNull(httpClient);

            _settingsService = settingsService;
            _httpClient = httpClient;
            _resolveHostAsync = resolveHostAsync ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
            _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
            if (_requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            }
        }

        public void ClearCache()
        {
            lock (_cacheSync)
            {
                _cachedRates = null;
                _cachedRatesSignature = string.Empty;
                _lastFetchTime = DateTime.MinValue;
            }
        }

        public async Task<decimal?> GetUsdCnyBuyingRateAsync(CancellationToken cancellationToken = default)
        {
            var rates = await GetExchangeRatesAsync(cancellationToken);
            var usdRate = rates.FirstOrDefault(r => r.CurrencyName == "美元");
            return usdRate?.BuyingRate;
        }

        public async Task<List<string>> GetAvailableCurrenciesAsync(CancellationToken cancellationToken = default)
        {
            var rows = await LoadRowsAsync(GetExchangeRateUrl(), cancellationToken);

            var currencies = new HashSet<string>();
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td");
                if (cells != null && cells.Count >= 6)
                {
                    var currencyName = cells[0].InnerText.Trim();
                    if (!string.IsNullOrEmpty(currencyName) && currencyName != "货币名称")
                    {
                        currencies.Add(currencyName);
                    }
                }
            }

            if (currencies.Count == 0)
            {
                throw CreateProtocolFailure();
            }

            return currencies.OrderBy(c => c).ToList();
        }

        public async Task<List<ExchangeRateInfo>> GetExchangeRatesAsync(CancellationToken cancellationToken = default)
        {
            var cacheDuration = Math.Max(0, _settingsService.Settings.ExchangeRate.CacheDurationMinutes);
            var configuredCurrencies = GetConfiguredCurrencies();
            if (configuredCurrencies.Count == 0)
            {
                return [];
            }

            var cacheSignature = BuildCacheSignature(GetExchangeRateUrl(), configuredCurrencies);
            if (TryGetCachedRates(cacheDuration, cacheSignature, out var cachedRates))
            {
                return cachedRates;
            }

            await _cacheRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryGetCachedRates(cacheDuration, cacheSignature, out cachedRates))
                {
                    return cachedRates;
                }

                var rows = await LoadRowsAsync(GetExchangeRateUrl(), cancellationToken);

                var orderMap = configuredCurrencies
                    .Select((currency, index) => new { currency, index })
                    .ToDictionary(x => x.currency, x => x.index, StringComparer.Ordinal);

                var list = new List<ExchangeRateInfo>();
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null || cells.Count < 6)
                    {
                        continue;
                    }

                    var currencyName = cells[0].InnerText.Trim();
                    if (!orderMap.ContainsKey(currencyName))
                    {
                        continue;
                    }

                    var rate = new ExchangeRateInfo
                    {
                        CurrencyName = currencyName,
                        BuyingRate = ParseRate(cells[1].InnerText),
                        CashBuyingRate = ParseRate(cells[2].InnerText),
                        SellingRate = ParseRate(cells[3].InnerText),
                        CashSellingRate = ParseRate(cells[4].InnerText),
                        MiddleRate = ParseRate(cells[5].InnerText),
                        PublishTime = cells.Count > 6 ? cells[6].InnerText.Trim() : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    if (rate.BuyingRate.HasValue ||
                        rate.CashBuyingRate.HasValue ||
                        rate.SellingRate.HasValue ||
                        rate.CashSellingRate.HasValue ||
                        rate.MiddleRate.HasValue)
                    {
                        list.Add(rate);
                    }
                }

                if (list.Count > 0)
                {
                    var orderedRates = list
                        .OrderBy(rate => orderMap[rate.CurrencyName])
                        .ToList();
                    UpdateCache(orderedRates, cacheSignature);
                    return CloneRates(orderedRates);
                }

                throw CreateProtocolFailure();
            }
            finally
            {
                _cacheRefreshLock.Release();
            }
        }

        private string GetExchangeRateUrl()
        {
            var url = _settingsService.Settings.ExchangeRate.Url;
            return ExchangeRateEndpointPolicy.Normalize(url).ToString();
        }

        private List<string> GetConfiguredCurrencies()
        {
            var settings = _settingsService.Settings.ExchangeRate;
            var currencies = settings.SelectedCurrencies?.Where(currency => !string.IsNullOrWhiteSpace(currency)).ToList();
            if (currencies == null || currencies.Count == 0)
            {
                currencies = settings.AllSupportedCurrencies?.Where(currency => !string.IsNullOrWhiteSpace(currency)).ToList();
            }

            return currencies?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        }

        private async Task<HtmlNodeCollection> LoadRowsAsync(string url, CancellationToken cancellationToken)
        {
            using var timeoutSource = new CancellationTokenSource(_requestTimeout);
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            try
            {
                Uri endpoint = ExchangeRateEndpointPolicy.Normalize(url);
                for (int redirect = 0; redirect <= ExchangeRateEndpointPolicy.MaximumRedirects; redirect++)
                {
                    await ExchangeRateEndpointPolicy.ValidatePublicHostAsync(
                            endpoint,
                            _resolveHostAsync,
                            operationCancellation.Token)
                        .ConfigureAwait(false);
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    request.Headers.UserAgent.ParseAdd(UserAgent);
                    using var response = await _httpClient.SendAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead,
                            operationCancellation.Token)
                        .ConfigureAwait(false);
                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        if (redirect == ExchangeRateEndpointPolicy.MaximumRedirects ||
                            response.Headers.Location == null)
                        {
                            throw new InfrastructureServiceException("汇率源重定向次数超过安全上限或缺少目标地址。");
                        }

                        endpoint = ExchangeRateEndpointPolicy.Normalize(
                            new Uri(endpoint, response.Headers.Location).ToString());
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumResponseBytes)
                    {
                        throw new PayloadLimitExceededException(MaximumResponseBytes);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(operationCancellation.Token);
                    await using var buffer = new MemoryStream();
                    await BoundedStreamHelper.CopyToAsync(
                        stream,
                        buffer,
                        MaximumResponseBytes,
                        operationCancellation.Token);
                    buffer.Position = 0;
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.Load(buffer, true);
                    var rows = doc.DocumentNode.SelectNodes("//table//tr") ??
                        doc.DocumentNode.SelectNodes("//tr");
                    if (rows == null || !rows.Any(HasExpectedRateColumns))
                    {
                        throw CreateProtocolFailure();
                    }
                    return rows;
                }

                throw new InfrastructureServiceException("汇率源重定向次数超过安全上限。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
            {
                throw new ServiceTimeoutException(
                    $"汇率源在 {_requestTimeout.TotalSeconds:0.#} 秒内未完成响应。",
                    ex);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (PayloadLimitExceededException ex)
            {
                throw new InfrastructureServiceException("汇率源返回内容超过 5 MiB 安全上限。", ex);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or SocketException or InvalidOperationException)
            {
                throw new InfrastructureServiceException("无法读取汇率源，请检查网络、证书或远端服务状态。", ex);
            }
        }

        private static bool HasExpectedRateColumns(HtmlNode row)
        {
            var cells = row?.SelectNodes("td");
            if (cells == null || cells.Count < 6)
            {
                return false;
            }

            string currencyName = WebUtility.HtmlDecode(cells[0].InnerText).Trim();
            return !string.IsNullOrWhiteSpace(currencyName) &&
                !string.Equals(currencyName, "货币名称", StringComparison.Ordinal);
        }

        private static InfrastructureServiceException CreateProtocolFailure() =>
            new("汇率源返回的页面结构或数据格式已变化，暂时无法可靠解析汇率。");

        private bool TryGetCachedRates(
            int cacheDurationMinutes,
            string cacheSignature,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<ExchangeRateInfo>? rates)
        {
            lock (_cacheSync)
            {
                if (_cachedRates == null ||
                    !string.Equals(_cachedRatesSignature, cacheSignature, StringComparison.Ordinal) ||
                    (DateTime.UtcNow - _lastFetchTime).TotalMinutes >= cacheDurationMinutes)
                {
                    rates = null;
                    return false;
                }

                rates = CloneRates(_cachedRates);
                return true;
            }
        }

        private void UpdateCache(List<ExchangeRateInfo> rates, string cacheSignature)
        {
            lock (_cacheSync)
            {
                _cachedRates = CloneRates(rates);
                _cachedRatesSignature = cacheSignature;
                _lastFetchTime = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            _cacheRefreshLock.Dispose();
        }

        private static string BuildCacheSignature(string url, IEnumerable<string> currencies)
        {
            var normalizedUrl = ExchangeRateEndpointPolicy.Normalize(url).ToString();
            var normalizedCurrencies = currencies
                .Where(currency => !string.IsNullOrWhiteSpace(currency))
                .Select(currency => currency.Trim())
                .Distinct(StringComparer.Ordinal);

            return $"{normalizedUrl}|{string.Join("|", normalizedCurrencies)}";
        }

        private static List<ExchangeRateInfo> CloneRates(IEnumerable<ExchangeRateInfo> rates)
        {
            return rates
                .Select(rate => new ExchangeRateInfo
                {
                    CurrencyName = rate.CurrencyName,
                    BuyingRate = rate.BuyingRate,
                    CashBuyingRate = rate.CashBuyingRate,
                    SellingRate = rate.SellingRate,
                    CashSellingRate = rate.CashSellingRate,
                    MiddleRate = rate.MiddleRate,
                    PublishTime = rate.PublishTime
                })
                .ToList();
        }

        private decimal? ParseRate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (decimal.TryParse(text.Trim(), out decimal rate))
            {
                // 汇率通常是 /100 的值 (例如 720.5 -> 7.205)
                // 日元可能是例外? 不, 中行也是每100日元
                return rate / 100m;
            }

            return null;
        }
    }
}
