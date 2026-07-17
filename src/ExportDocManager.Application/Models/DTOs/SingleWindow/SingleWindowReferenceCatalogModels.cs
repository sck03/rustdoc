namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowReferenceCatalogModel
    {
        public IReadOnlyList<SingleWindowReferenceCountryEntry> Countries { get; init; } = [];

        public IReadOnlyList<SingleWindowReferenceAcdCountryEntry> AcdCountries { get; init; } = [];

        public IReadOnlyList<SingleWindowReferenceCurrencyEntry> Currencies { get; init; } = [];

        public IReadOnlyList<SingleWindowReferenceAcdTradeModeEntry> AcdTradeModes { get; init; } = [];

        public IReadOnlyList<SingleWindowReferenceTransportModeEntry> TransportModes { get; init; } = [];

        public IReadOnlyList<SingleWindowReferencePortEntry> Ports { get; init; } = [];
    }

    public sealed class SingleWindowReferenceCountryEntry
    {
        public string Code { get; init; } = string.Empty;

        public string EnglishName { get; init; } = string.Empty;

        public string ChineseName { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }

    public sealed class SingleWindowReferenceAcdCountryEntry
    {
        public string Code { get; init; } = string.Empty;

        public string ChineseName { get; init; } = string.Empty;

        public string EnglishName { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }

    public sealed class SingleWindowReferenceCurrencyEntry
    {
        public string Code { get; init; } = string.Empty;

        public string AcdCode { get; init; } = string.Empty;

        public string AlphaCode { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }

    public sealed class SingleWindowReferenceAcdTradeModeEntry
    {
        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }

    public sealed class SingleWindowReferenceTransportModeEntry
    {
        public string Value { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }

    public sealed class SingleWindowReferencePortEntry
    {
        public string Value { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];
    }
}
