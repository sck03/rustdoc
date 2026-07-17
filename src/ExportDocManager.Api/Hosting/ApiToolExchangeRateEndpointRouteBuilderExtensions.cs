using ExportDocManager.Models;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string ExchangeRateStoragePolicy =
            "汇率工具只读取程序根 appsettings.json 中的汇率配置，并通过用户配置的汇率源远程获取数据；查询结果只保存在服务内存缓存和页面状态中，不写入默认导出目录、数据库表或系统 C 盘。";

        private static void MapExchangeRateToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/tools/exchange-rates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService,
                IExchangeRateService exchangeRateService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                bool forceRefresh = bool.TryParse(
                    context.Request.Query["forceRefresh"].FirstOrDefault(),
                    out bool parsedForceRefresh) && parsedForceRefresh;

                try
                {
                    await settingsService.LoadAsync();
                    if (forceRefresh)
                    {
                        exchangeRateService.ClearCache();
                    }

                    var rates = await exchangeRateService.GetExchangeRatesAsync();
                    cancellationToken.ThrowIfCancellationRequested();

                    var exchangeRateSettings = EnsureExchangeRateSettings(settingsService.Settings);
                    var selectedCurrencies = GetConfiguredCurrencies(exchangeRateSettings);
                    var rateDtos = (rates ?? [])
                        .Where(rate => rate != null)
                        .Select(ToApiDto)
                        .ToList();
                    var fetchedAt = DateTimeOffset.Now;

                    return Results.Ok(new ApiExchangeRateListResponse
                    {
                        Rates = rateDtos,
                        SourceUrl = ResolveExchangeRateSourceUrl(exchangeRateSettings),
                        SelectedCurrencies = selectedCurrencies,
                        CacheDurationMinutes = Math.Max(0, exchangeRateSettings.CacheDurationMinutes),
                        FetchedAt = fetchedAt,
                        StatusText = BuildExchangeRateStatusText(rateDtos.Count, fetchedAt),
                        StoragePolicy = ExchangeRateStoragePolicy
                    });
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(
                        new ApiErrorResponse("汇率查询已取消。"),
                        statusCode: StatusCodes.Status499ClientClosedRequest);
                }
                catch (Exception ex)
                {
                    return WriteConflict($"获取汇率失败：{ex.Message}");
                }
            })
            .WithName("ListExchangeRates");

            endpoints.MapGet("/api/tools/exchange-rates/available-currencies", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService,
                IExchangeRateService exchangeRateService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    await settingsService.LoadAsync();
                    var currencies = await exchangeRateService.GetAvailableCurrenciesAsync();
                    cancellationToken.ThrowIfCancellationRequested();

                    return Results.Ok(new ApiExchangeRateAvailableCurrenciesResponse
                    {
                        Currencies = (currencies ?? [])
                            .Where(currency => !string.IsNullOrWhiteSpace(currency))
                            .Select(currency => currency.Trim())
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(currency => currency, StringComparer.CurrentCulture)
                            .ToList(),
                        SourceUrl = ResolveExchangeRateSourceUrl(EnsureExchangeRateSettings(settingsService.Settings)),
                        FetchedAt = DateTimeOffset.Now,
                        StoragePolicy = ExchangeRateStoragePolicy
                    });
                }
                catch (OperationCanceledException)
                {
                    return Results.Json(
                        new ApiErrorResponse("货币列表查询已取消。"),
                        statusCode: StatusCodes.Status499ClientClosedRequest);
                }
                catch (Exception ex)
                {
                    return WriteConflict($"获取货币列表失败：{ex.Message}");
                }
            })
            .WithName("ListAvailableExchangeRateCurrencies");
        }

        private static ExchangeRateSettings EnsureExchangeRateSettings(AppSettings settings)
        {
            settings.ExchangeRate ??= new ExchangeRateSettings();
            settings.ExchangeRate.SelectedCurrencies ??= [];
            settings.ExchangeRate.AllSupportedCurrencies ??= [];
            return settings.ExchangeRate;
        }

        private static List<string> GetConfiguredCurrencies(ExchangeRateSettings settings)
        {
            var currencies = settings.SelectedCurrencies?
                .Where(currency => !string.IsNullOrWhiteSpace(currency))
                .Select(currency => currency.Trim())
                .ToList();
            if (currencies == null || currencies.Count == 0)
            {
                currencies = settings.AllSupportedCurrencies?
                    .Where(currency => !string.IsNullOrWhiteSpace(currency))
                    .Select(currency => currency.Trim())
                    .ToList();
            }

            return currencies?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        }

        private static string ResolveExchangeRateSourceUrl(ExchangeRateSettings settings)
        {
            return string.IsNullOrWhiteSpace(settings.Url)
                ? "https://www.boc.cn/sourcedb/whpj/"
                : settings.Url.Trim();
        }

        private static string BuildExchangeRateStatusText(int rateCount, DateTimeOffset fetchedAt)
        {
            return rateCount > 0
                ? $"获取成功! 共 {rateCount} 种货币。数据来源: 中国银行。更新时间: {fetchedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
                : "获取汇率失败，请检查网络连接或系统设置中的汇率源网址。";
        }

        private static ApiExchangeRateDto ToApiDto(ExchangeRateInfo rate)
        {
            return new ApiExchangeRateDto
            {
                CurrencyName = rate.CurrencyName ?? string.Empty,
                BuyingRate = rate.BuyingRate,
                CashBuyingRate = rate.CashBuyingRate,
                SellingRate = rate.SellingRate,
                CashSellingRate = rate.CashSellingRate,
                MiddleRate = rate.MiddleRate,
                PublishTime = rate.PublishTime ?? string.Empty
            };
        }
    }
}
