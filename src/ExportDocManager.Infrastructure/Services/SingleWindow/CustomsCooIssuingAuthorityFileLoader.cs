using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooIssuingAuthorityFileLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static IReadOnlyList<CustomsCooIssuingAuthorityEntry> LoadEntriesFromFiles(params string[] filePaths)
        {
            var merged = new Dictionary<string, CustomsCooIssuingAuthorityEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in filePaths ?? Array.Empty<string>())
            {
                MergeEntries(merged, LoadEntriesFromFile(filePath));
            }

            return merged.Values
                .OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<CustomsCooIssuingAuthorityEntry> LoadEntriesFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return Array.Empty<CustomsCooIssuingAuthorityEntry>();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var payload = JsonSerializer.Deserialize<CustomsCooIssuingAuthorityDataFile>(json, JsonOptions);
                if (payload?.Entries == null || payload.Entries.Count == 0)
                {
                    return Array.Empty<CustomsCooIssuingAuthorityEntry>();
                }

                return payload.Entries
                    .Select(MapEntry)
                    .Where(entry => entry != null)
                    .Cast<CustomsCooIssuingAuthorityEntry>()
                    .ToList();
            }
            catch
            {
                return Array.Empty<CustomsCooIssuingAuthorityEntry>();
            }
        }

        private static CustomsCooIssuingAuthorityEntry MapEntry(CustomsCooIssuingAuthorityDataEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new CustomsCooIssuingAuthorityEntry(
                NormalizeCode(entry.Code),
                NormalizeText(entry.Name),
                NormalizeText(entry.ApplicationAddress),
                NormalizeText(entry.AbbreviationName),
                entry.Aliases?
                    .Select(NormalizeText)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                ?? Array.Empty<string>());
        }

        private static void MergeEntries(
            IDictionary<string, CustomsCooIssuingAuthorityEntry> target,
            IEnumerable<CustomsCooIssuingAuthorityEntry> entries)
        {
            foreach (var entry in entries ?? Array.Empty<CustomsCooIssuingAuthorityEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Code) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                target[entry.Code] = entry;
            }
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

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private sealed class CustomsCooIssuingAuthorityDataFile
        {
            public List<CustomsCooIssuingAuthorityDataEntry> Entries { get; set; } = [];
        }

        private sealed class CustomsCooIssuingAuthorityDataEntry
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string ApplicationAddress { get; set; } = string.Empty;
            public string AbbreviationName { get; set; } = string.Empty;
            public string[] Aliases { get; set; } = Array.Empty<string>();
        }
    }
}
