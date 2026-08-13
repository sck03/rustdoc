using System.Text.RegularExpressions;
using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed record CustomsCooIssuingAuthorityEntry(
        string Code,
        string Name,
        string ApplicationAddress,
        string AbbreviationName,
        params string[] Aliases);

    public sealed class CustomsCooIssuingAuthorityCatalog
    {
        private readonly IReadOnlyDictionary<string, CustomsCooIssuingAuthorityEntry> _lookup;
        private readonly IReadOnlyList<SelectionOption<string>> _options;

        public CustomsCooIssuingAuthorityCatalog(
            IEnumerable<CustomsCooIssuingAuthorityEntry>? entries = null)
        {
            var normalizedEntries = NormalizeEntries(entries ?? []);
            _lookup = BuildLookup(normalizedEntries);
            _options = BuildOptions(normalizedEntries);
        }

        public IReadOnlyList<SelectionOption<string>> GetOptions() => _options;

        public string ParseCode(string value)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var codeMatch = Regex.Match(normalized, @"(?<!\d)(\d{4})(?!\d)");
            if (codeMatch.Success)
            {
                return codeMatch.Groups[1].Value;
            }

            return TryResolve(normalized, out var entry)
                ? entry.Code
                : normalized;
        }

        public string GetDisplayText(string value)
        {
            string code = ParseCode(value);
            return TryResolve(code, out var entry)
                ? BuildDisplayText(entry)
                : code;
        }

        public string ResolveApplicationAddress(string value)
        {
            string code = ParseCode(value);
            return TryResolve(code, out var entry)
                ? entry.ApplicationAddress
                : string.Empty;
        }

        private bool TryResolve(
            string? value,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CustomsCooIssuingAuthorityEntry? entry)
        {
            return _lookup.TryGetValue(NormalizeLookupKey(value), out entry);
        }

        private static Dictionary<string, CustomsCooIssuingAuthorityEntry> BuildLookup(
            IEnumerable<CustomsCooIssuingAuthorityEntry> entries)
        {
            var lookup = new Dictionary<string, CustomsCooIssuingAuthorityEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Array.Empty<CustomsCooIssuingAuthorityEntry>())
            {
                AddLookup(lookup, entry.Code, entry);
                AddLookup(lookup, entry.Name, entry);
                AddLookup(lookup, entry.ApplicationAddress, entry);
                AddLookup(lookup, entry.AbbreviationName, entry);

                foreach (var alias in entry.Aliases)
                {
                    AddLookup(lookup, alias, entry);
                }
            }

            return lookup;
        }

        private static IReadOnlyList<SelectionOption<string>> BuildOptions(
            IEnumerable<CustomsCooIssuingAuthorityEntry> entries)
        {
            return (entries ?? Array.Empty<CustomsCooIssuingAuthorityEntry>())
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .Select(entry => new SelectionOption<string>(entry.Code, BuildDisplayText(entry)))
                .ToList();
        }

        private static IReadOnlyList<CustomsCooIssuingAuthorityEntry> NormalizeEntries(
            IEnumerable<CustomsCooIssuingAuthorityEntry> entries)
        {
            var merged = new Dictionary<string, CustomsCooIssuingAuthorityEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Array.Empty<CustomsCooIssuingAuthorityEntry>())
            {
                var normalized = NormalizeEntry(entry);
                if (normalized == null)
                {
                    continue;
                }

                merged[normalized.Code] = normalized;
            }

            if (merged.Count == 0)
            {
                foreach (var fallback in GetFallbackEntries())
                {
                    merged[fallback.Code] = fallback;
                }
            }

            return merged.Values
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .ToList();
        }

        private static CustomsCooIssuingAuthorityEntry? NormalizeEntry(CustomsCooIssuingAuthorityEntry? entry)
        {
            if (entry == null)
            {
                return null;
            }

            string normalizedCode = NormalizeCode(entry.Code);
            string normalizedName = NormalizeText(entry.Name);
            string normalizedAddress = NormalizeText(entry.ApplicationAddress);
            string normalizedAbbreviationName = NormalizeText(entry.AbbreviationName);
            if (string.IsNullOrWhiteSpace(normalizedCode) ||
                string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            return new CustomsCooIssuingAuthorityEntry(
                normalizedCode,
                normalizedName,
                normalizedAddress,
                normalizedAbbreviationName,
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                ?? Array.Empty<string>());
        }

        private static IEnumerable<CustomsCooIssuingAuthorityEntry> GetFallbackEntries()
        {
            return
            [
                new CustomsCooIssuingAuthorityEntry("3100", "宁波关区", "NINGBO, CHINA", "", "宁波海关关区"),
                new CustomsCooIssuingAuthorityEntry("3101", "宁波海关", "NINGBO, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("2200", "上海海关", "SHANGHAI, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("2201", "浦江海关", "SHANGHAI, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("2300", "南京海关", "NANJING, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("2900", "杭州关区", "HANGZHOU, CHINA", "", "杭州海关关区"),
                new CustomsCooIssuingAuthorityEntry("2901", "杭州海关", "HANGZHOU, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("3500", "福州关区", "FUZHOU, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("3700", "厦门关区", "XIAMEN, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("4200", "青岛海关", "QINGDAO, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("5100", "广州海关", "GUANGZHOU, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("5200", "黄埔关区", "GUANGZHOU, CHINA", ""),
                new CustomsCooIssuingAuthorityEntry("5300", "深圳海关", "SHENZHEN, CHINA", "")
            ];
        }

        private static string NormalizeCode(string value)
        {
            string normalized = NormalizeText(value);
            if (normalized.Length == 4 && normalized.All(char.IsDigit))
            {
                return normalized;
            }

            var match = Regex.Match(normalized, @"(?<!\d)(\d{4})(?!\d)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static void AddLookup(
            IDictionary<string, CustomsCooIssuingAuthorityEntry> lookup,
            string key,
            CustomsCooIssuingAuthorityEntry entry)
        {
            string normalizedKey = NormalizeLookupKey(key);
            if (!string.IsNullOrWhiteSpace(normalizedKey))
            {
                lookup[normalizedKey] = entry;
            }
        }

        private static string NormalizeLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Trim()
                .ToUpperInvariant()
                .Where(ch => char.IsLetterOrDigit(ch) || ch > 127)
                .ToArray());
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string BuildDisplayText(CustomsCooIssuingAuthorityEntry entry)
        {
            string officialName = NormalizeText(entry.Name);
            return string.IsNullOrWhiteSpace(officialName)
                ? entry.Code
                : $"{entry.Code}：{officialName}";
        }
    }
}
