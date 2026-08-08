using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;

namespace ExportDocManager.Services.Data
{
    public interface IExchangeRateService
    {
        /// <summary>
        /// Gets the current buying rate for USD to CNY.
        /// 获取美元对人民币的现汇买入价。
        /// </summary>
        /// <returns>The exchange rate, or null if retrieval fails.</returns>
        Task<decimal?> GetUsdCnyBuyingRateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all major exchange rates.
        /// 获取主要货币的汇率信息。
        /// </summary>
        /// <returns>List of exchange rate info.</returns>
        Task<List<ExchangeRateInfo>> GetExchangeRatesAsync(CancellationToken cancellationToken = default);
        Task<List<string>> GetAvailableCurrenciesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the cached exchange rates.
        /// 清除汇率缓存。
        /// </summary>
        void ClearCache();
    }
}
