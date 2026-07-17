namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiExchangeRateDto
    {
        public string CurrencyName { get; set; } = string.Empty;

        public decimal? BuyingRate { get; set; }

        public decimal? CashBuyingRate { get; set; }

        public decimal? SellingRate { get; set; }

        public decimal? CashSellingRate { get; set; }

        public decimal? MiddleRate { get; set; }

        public string PublishTime { get; set; } = string.Empty;
    }

    public sealed class ApiExchangeRateListResponse
    {
        public IReadOnlyList<ApiExchangeRateDto> Rates { get; set; } = Array.Empty<ApiExchangeRateDto>();

        public string SourceUrl { get; set; } = string.Empty;

        public IReadOnlyList<string> SelectedCurrencies { get; set; } = Array.Empty<string>();

        public int CacheDurationMinutes { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        public string StatusText { get; set; } = string.Empty;

        public string StoragePolicy { get; set; } = string.Empty;
    }

    public sealed class ApiExchangeRateAvailableCurrenciesResponse
    {
        public IReadOnlyList<string> Currencies { get; set; } = Array.Empty<string>();

        public string SourceUrl { get; set; } = string.Empty;

        public DateTimeOffset FetchedAt { get; set; }

        public string StoragePolicy { get; set; } = string.Empty;
    }
}
