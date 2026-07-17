using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooTextFormatter
    {
        public static string ResolveExporterCreditCode(Invoice invoice, Exporter exporter)
        {
            return FirstNonEmpty(
                NormalizeText(invoice?.ExporterCreditCode),
                NormalizeText(exporter?.CreditCode));
        }

        public static string BuildPartyBlock(string name, string address)
        {
            string normalizedName = NormalizeText(name);
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                lines.Add(normalizedName);
            }

            lines.AddRange(SplitAddressLines(address));
            return lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        public static string DecodeXmlMultiline(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value
                .Replace("/n", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            var lines = normalized
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        public static string EncodeXmlMultiline(string value)
        {
            string normalized = DecodeXmlMultiline(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return string.Join(
                "/n",
                normalized
                    .Split([Environment.NewLine], StringSplitOptions.None)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? [])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static IReadOnlyList<string> SplitAddressLines(string address)
        {
            string normalizedAddress = DecodeXmlMultiline(address);
            if (string.IsNullOrWhiteSpace(normalizedAddress))
            {
                return Array.Empty<string>();
            }

            var explicitLines = normalizedAddress
                .Split([Environment.NewLine], StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (explicitLines.Count > 1)
            {
                return explicitLines.Take(2).ToList();
            }

            var segments = normalizedAddress
                .Split([",", "，", ";", "；"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (segments.Count > 1)
            {
                return CollapseSegmentsToAddressLines(segments);
            }

            if (normalizedAddress.Length > 48 && normalizedAddress.Contains(' ', StringComparison.Ordinal))
            {
                int midpoint = normalizedAddress.Length / 2;
                int breakIndex = normalizedAddress.LastIndexOf(' ', midpoint);
                if (breakIndex > 0)
                {
                    return
                    [
                        normalizedAddress[..breakIndex].Trim(),
                        normalizedAddress[(breakIndex + 1)..].Trim()
                    ];
                }
            }

            return [normalizedAddress];
        }

        private static IReadOnlyList<string> CollapseSegmentsToAddressLines(IReadOnlyList<string> segments)
        {
            if (segments.Count <= 2)
            {
                return segments.ToList();
            }

            int totalLength = segments.Sum(segment => segment.Length);
            int currentLength = 0;
            var firstLine = new List<string>();
            var secondLine = new List<string>();

            foreach (var segment in segments)
            {
                bool canKeepBalancing =
                    firstLine.Count == 0 ||
                    (currentLength + segment.Length <= totalLength / 2 && secondLine.Count == 0);

                if (canKeepBalancing)
                {
                    firstLine.Add(segment);
                    currentLength += segment.Length;
                }
                else
                {
                    secondLine.Add(segment);
                }
            }

            if (secondLine.Count == 0 && firstLine.Count > 1)
            {
                secondLine.Add(firstLine[^1]);
                firstLine.RemoveAt(firstLine.Count - 1);
            }

            return new List<string>
                {
                    string.Join(", ", firstLine).Trim(),
                    string.Join(", ", secondLine).Trim()
                }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
    }
}
