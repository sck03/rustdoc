using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using Serilog;

namespace ExportDocManager.Services.Data
{
    public class BocExchangeRateService : IExchangeRateService, IDisposable
    {
        private const string DefaultUrl = "https://www.boc.cn/sourcedb/whpj/";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";

        private readonly ISettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _cacheRefreshLock = new(1, 1);
        private readonly object _cacheSync = new();

        // 缓存结果以避免频繁请求
        private List<ExchangeRateInfo> _cachedRates;
        private string _cachedRatesSignature = string.Empty;
        private DateTime _lastFetchTime = DateTime.MinValue;

        public BocExchangeRateService(ISettingsService settingsService, HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(settingsService);
            ArgumentNullException.ThrowIfNull(httpClient);

            _settingsService = settingsService;
            _httpClient = httpClient;
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

        public async Task<decimal?> GetUsdCnyBuyingRateAsync()
        {
            var rates = await GetExchangeRatesAsync();
            if (rates == null) return null;
            
            var usdRate = rates.FirstOrDefault(r => r.CurrencyName == "美元");
            return usdRate?.BuyingRate;
        }

        public async Task<List<string>> GetAvailableCurrenciesAsync()
        {
            try
            {
                var rows = await LoadRowsAsync(GetExchangeRateUrl());
                if (rows != null)
                {
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

                    return currencies.OrderBy(c => c).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching available currencies");
                System.Diagnostics.Debug.WriteLine($"Error fetching available currencies: {ex.Message}");
            }

            return new List<string>();
        }

        public async Task<List<ExchangeRateInfo>> GetExchangeRatesAsync()
        {
            var cacheDuration = Math.Max(0, _settingsService.Settings.ExchangeRate.CacheDurationMinutes);
            var configuredCurrencies = GetConfiguredCurrencies();
            if (configuredCurrencies.Count == 0)
            {
                return null;
            }

            var cacheSignature = BuildCacheSignature(GetExchangeRateUrl(), configuredCurrencies);
            if (TryGetCachedRates(cacheDuration, cacheSignature, out var cachedRates))
            {
                return cachedRates;
            }

            await _cacheRefreshLock.WaitAsync();
            try
            {
                if (TryGetCachedRates(cacheDuration, cacheSignature, out cachedRates))
                {
                    return cachedRates;
                }

                var rows = await LoadRowsAsync(GetExchangeRateUrl());
                if (rows == null)
                {
                    return null;
                }

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

                    list.Add(new ExchangeRateInfo
                    {
                        CurrencyName = currencyName,
                        BuyingRate = ParseRate(cells[1].InnerText),
                        CashBuyingRate = ParseRate(cells[2].InnerText),
                        SellingRate = ParseRate(cells[3].InnerText),
                        CashSellingRate = ParseRate(cells[4].InnerText),
                        MiddleRate = ParseRate(cells[5].InnerText),
                        PublishTime = cells.Count > 6 ? cells[6].InnerText.Trim() : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                if (list.Count > 0)
                {
                    var orderedRates = list
                        .OrderBy(rate => orderMap[rate.CurrencyName])
                        .ToList();
                    UpdateCache(orderedRates, cacheSignature);
                    return CloneRates(orderedRates);
                }
            }
            catch (Exception ex)
            {
                // Log error if logging system exists
                Log.Error(ex, "Error fetching exchange rate");
                System.Diagnostics.Debug.WriteLine($"Error fetching exchange rate: {ex.Message}");
            }
            finally
            {
                _cacheRefreshLock.Release();
            }

            return null;
        }

        private string GetExchangeRateUrl()
        {
            var url = _settingsService.Settings.ExchangeRate.Url;
            return string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;
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

        private async Task<HtmlNodeCollection> LoadRowsAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.Load(stream, true);
            return doc.DocumentNode.SelectNodes("//table//tr") ?? doc.DocumentNode.SelectNodes("//tr");
        }

        private bool TryGetCachedRates(int cacheDurationMinutes, string cacheSignature, out List<ExchangeRateInfo> rates)
        {
            lock (_cacheSync)
            {
                if (_cachedRates == null ||
                    !string.Equals(_cachedRatesSignature, cacheSignature, StringComparison.Ordinal) ||
                    (DateTime.Now - _lastFetchTime).TotalMinutes >= cacheDurationMinutes)
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
                _cachedRatesSignature = cacheSignature ?? string.Empty;
                _lastFetchTime = DateTime.Now;
            }
        }

        public void Dispose()
        {
            _cacheRefreshLock.Dispose();
        }

        private static string BuildCacheSignature(string url, IEnumerable<string> currencies)
        {
            var normalizedUrl = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            var normalizedCurrencies = (currencies ?? Enumerable.Empty<string>())
                .Where(currency => !string.IsNullOrWhiteSpace(currency))
                .Select(currency => currency.Trim())
                .Distinct(StringComparer.Ordinal);

            return $"{normalizedUrl}|{string.Join("|", normalizedCurrencies)}";
        }

        private static List<ExchangeRateInfo> CloneRates(IEnumerable<ExchangeRateInfo> rates)
        {
            return (rates ?? Enumerable.Empty<ExchangeRateInfo>())
                .Select(rate => new ExchangeRateInfo
                {
                    CurrencyName = rate?.CurrencyName,
                    BuyingRate = rate?.BuyingRate,
                    CashBuyingRate = rate?.CashBuyingRate,
                    SellingRate = rate?.SellingRate,
                    CashSellingRate = rate?.CashSellingRate,
                    MiddleRate = rate?.MiddleRate,
                    PublishTime = rate?.PublishTime
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
