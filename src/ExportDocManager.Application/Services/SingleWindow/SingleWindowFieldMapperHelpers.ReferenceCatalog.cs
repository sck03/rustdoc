using System.Threading;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class SingleWindowFieldMapperHelpers
    {
        private static Func<SingleWindowReferenceCatalogModel> _referenceCatalogSnapshotLoader;
        private static ReferenceCatalogState _referenceCatalogState = ReferenceCatalogState.Empty;

        public static void ConfigureReferenceCatalogSnapshotLoader(Func<SingleWindowReferenceCatalogModel> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            Volatile.Write(ref _referenceCatalogSnapshotLoader, loader);
            ReloadReferenceCatalog();
        }

        public static void ReloadReferenceCatalog()
        {
            var payload = LoadReferenceCatalog();
            Volatile.Write(ref _referenceCatalogState, BuildReferenceCatalogState(payload));
        }

        private static ReferenceCatalogState CurrentReferenceCatalogState => Volatile.Read(ref _referenceCatalogState);

        private static ReferenceCatalogState BuildReferenceCatalogState(ReferenceCatalogPayload payload)
        {
            return new ReferenceCatalogState(
                BuildCountryLookup(payload),
                BuildAcdCountryLookup(payload),
                BuildCurrencyTextLookup(payload),
                BuildCurrencyAcdCodeLookup(payload),
                BuildAcdTradeModeLookup(payload),
                BuildTransportModeLookup(payload),
                BuildPortLookup(payload));
        }

        private static Dictionary<string, CountryCatalogEntry> BuildCountryLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, CountryCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.Countries)
            {
                AddLookup(lookup, entry.Code, entry);
                AddLookup(lookup, entry.EnglishName, entry);
                AddLookup(lookup, entry.ChineseName, entry);

                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry);
                }
            }

            return lookup;
        }

        private static Dictionary<string, CurrencyCatalogEntry> BuildCurrencyTextLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, CurrencyCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.Currencies)
            {
                AddLookup(lookup, entry.AlphaCode, entry);
                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry);
                }
            }

            return lookup;
        }

        private static Dictionary<string, AcdCountryCatalogEntry> BuildAcdCountryLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, AcdCountryCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.AcdCountries)
            {
                AddLookup(lookup, entry.Code, entry);
                AddLookup(lookup, entry.ChineseName, entry);
                AddLookup(lookup, entry.EnglishName, entry);

                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry);
                }
            }

            return lookup;
        }

        private static Dictionary<string, AcdTradeModeCatalogEntry> BuildAcdTradeModeLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, AcdTradeModeCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.AcdTradeModes)
            {
                AddLookup(lookup, entry.Code, entry);
                AddLookup(lookup, entry.Name, entry);
                AddLookup(lookup, entry.Description, entry);

                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry);
                }
            }

            return lookup;
        }

        private static Dictionary<string, CurrencyCatalogEntry> BuildCurrencyAcdCodeLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, CurrencyCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.Currencies)
            {
                AddLookup(lookup, entry.AcdCode, entry);
            }

            return lookup;
        }

        private static Dictionary<string, string> BuildTransportModeLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.TransportModes)
            {
                AddLookup(lookup, entry.Value, entry.Value);
                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry.Value);
                }
            }

            return lookup;
        }

        private static Dictionary<string, string> BuildPortLookup(ReferenceCatalogPayload payload)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.Ports)
            {
                AddLookup(lookup, entry.Value, entry.Value);
                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry.Value);
                }
            }

            return lookup;
        }

        private static ReferenceCatalogPayload LoadReferenceCatalog()
        {
            try
            {
                var loader = Volatile.Read(ref _referenceCatalogSnapshotLoader);
                var catalog = loader?.Invoke();
                if (catalog == null)
                {
                    return BuildFallbackReferenceCatalog();
                }

                var countries = (catalog.Countries ?? [])
                    .Select(MapCountryEntry)
                    .Where(entry => entry != null)
                    .Cast<CountryCatalogEntry>()
                    .ToList();
                var acdCountries = (catalog.AcdCountries ?? [])
                    .Select(MapAcdCountryEntry)
                    .Where(entry => entry != null)
                    .Cast<AcdCountryCatalogEntry>()
                    .ToList();
                var currencies = (catalog.Currencies ?? [])
                    .Select(MapCurrencyEntry)
                    .Where(entry => entry != null)
                    .Cast<CurrencyCatalogEntry>()
                    .ToList();
                var acdTradeModes = (catalog.AcdTradeModes ?? [])
                    .Select(MapAcdTradeModeEntry)
                    .Where(entry => entry != null)
                    .Cast<AcdTradeModeCatalogEntry>()
                    .ToList();
                var transportModes = (catalog.TransportModes ?? [])
                    .Select(MapTransportModeEntry)
                    .Where(entry => entry != null)
                    .Cast<TransportModeCatalogEntry>()
                    .ToList();
                var ports = (catalog.Ports ?? [])
                    .Select(MapPortEntry)
                    .Where(entry => entry != null)
                    .Cast<PortCatalogEntry>()
                    .ToList();

                if (countries.Count == 0 &&
                    acdCountries.Count == 0 &&
                    currencies.Count == 0 &&
                    acdTradeModes.Count == 0 &&
                    transportModes.Count == 0 &&
                    ports.Count == 0)
                {
                    return BuildFallbackReferenceCatalog();
                }

                return new ReferenceCatalogPayload(countries, acdCountries, currencies, acdTradeModes, transportModes, ports);
            }
            catch
            {
                return BuildFallbackReferenceCatalog();
            }
        }

        private static CountryCatalogEntry MapCountryEntry(SingleWindowReferenceCountryEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string code = NormalizeText(entry.Code);
            string englishName = NormalizeUpperText(entry.EnglishName);
            string chineseName = NormalizeText(entry.ChineseName);
            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(englishName) ||
                string.IsNullOrWhiteSpace(chineseName))
            {
                return null;
            }

            return new CountryCatalogEntry(
                code,
                englishName,
                chineseName,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static CurrencyCatalogEntry MapCurrencyEntry(SingleWindowReferenceCurrencyEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string code = NormalizeText(entry.Code);
            string alphaCode = NormalizeUpperText(entry.AlphaCode);
            string acdCode = NormalizeText(entry.AcdCode);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(alphaCode))
            {
                return null;
            }

            return new CurrencyCatalogEntry(
                code,
                acdCode,
                alphaCode,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static AcdCountryCatalogEntry MapAcdCountryEntry(SingleWindowReferenceAcdCountryEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string code = NormalizeText(entry.Code);
            string chineseName = NormalizeText(entry.ChineseName);
            string englishName = NormalizeText(entry.EnglishName);
            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(chineseName) ||
                string.IsNullOrWhiteSpace(englishName))
            {
                return null;
            }

            return new AcdCountryCatalogEntry(
                code,
                chineseName,
                englishName,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static AcdTradeModeCatalogEntry MapAcdTradeModeEntry(SingleWindowReferenceAcdTradeModeEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string code = NormalizeText(entry.Code);
            string name = NormalizeText(entry.Name);
            string description = NormalizeText(entry.Description);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new AcdTradeModeCatalogEntry(
                code,
                name,
                description,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static TransportModeCatalogEntry MapTransportModeEntry(SingleWindowReferenceTransportModeEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string value = NormalizeUpperText(entry.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new TransportModeCatalogEntry(
                value,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static PortCatalogEntry MapPortEntry(SingleWindowReferencePortEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string value = NormalizeUpperText(entry.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new PortCatalogEntry(
                value,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }

        private static ReferenceCatalogPayload BuildFallbackReferenceCatalog()
        {
            return new ReferenceCatalogPayload(
                FallbackCountryCatalog.ToList(),
                FallbackAcdCountryCatalog.ToList(),
                FallbackCurrencyCatalog.ToList(),
                FallbackAcdTradeModeCatalog.ToList(),
                FallbackTransportModeCatalog.ToList(),
                FallbackPortCatalog.ToList());
        }

        private static void AddLookup<TValue>(IDictionary<string, TValue> lookup, string key, TValue value)
        {
            string normalized = NormalizeLookupKey(key);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                lookup[normalized] = value;
            }
        }

        private sealed record ReferenceCatalogPayload(
            IReadOnlyList<CountryCatalogEntry> Countries,
            IReadOnlyList<AcdCountryCatalogEntry> AcdCountries,
            IReadOnlyList<CurrencyCatalogEntry> Currencies,
            IReadOnlyList<AcdTradeModeCatalogEntry> AcdTradeModes,
            IReadOnlyList<TransportModeCatalogEntry> TransportModes,
            IReadOnlyList<PortCatalogEntry> Ports);

        private sealed record ReferenceCatalogState(
            IReadOnlyDictionary<string, CountryCatalogEntry> CountryLookup,
            IReadOnlyDictionary<string, AcdCountryCatalogEntry> AcdCountryLookup,
            IReadOnlyDictionary<string, CurrencyCatalogEntry> CurrencyTextLookup,
            IReadOnlyDictionary<string, CurrencyCatalogEntry> CurrencyAcdCodeLookup,
            IReadOnlyDictionary<string, AcdTradeModeCatalogEntry> AcdTradeModeLookup,
            IReadOnlyDictionary<string, string> TransportModeLookup,
            IReadOnlyDictionary<string, string> PortLookup)
        {
            public static ReferenceCatalogState Empty { get; } = new(
                CreateEmptyLookup<CountryCatalogEntry>(),
                CreateEmptyLookup<AcdCountryCatalogEntry>(),
                CreateEmptyLookup<CurrencyCatalogEntry>(),
                CreateEmptyLookup<CurrencyCatalogEntry>(),
                CreateEmptyLookup<AcdTradeModeCatalogEntry>(),
                CreateEmptyLookup<string>(),
                CreateEmptyLookup<string>());
        }

        private static Dictionary<string, TValue> CreateEmptyLookup<TValue>()
        {
            return new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
