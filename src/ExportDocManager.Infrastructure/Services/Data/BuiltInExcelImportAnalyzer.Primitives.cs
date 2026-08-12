using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public sealed partial class BuiltInExcelImportAnalyzer
    {
        private static string GetConfiguredCellReference(string fieldKey, ExcelImportSettings settings)
        {
            return fieldKey switch
            {
                "ExporterNameCN" => settings.ExporterNameCNCell,
                "ExporterNameEN" => settings.ExporterNameCell,
                "ExporterAddressEN" => settings.ExporterAddressStartCell,
                "CustomerNameEN" => settings.CustomerNameCell,
                "CustomerAddressEN" => settings.CustomerAddressStartCell,
                "NotifyPartyName" => settings.NotifyPartyNameCell,
                "NotifyPartyAddress" => settings.NotifyPartyAddressStartCell,
                "InvoiceNo" => settings.InvoiceNoCell,
                "ContractNo" => settings.ContractNoCell,
                "InvoiceDate" => settings.InvoiceDateCell,
                "PortOfLoading" => settings.PortOfLoadingCell,
                "PortOfDestination" => settings.PortOfDestinationCell,
                "DestinationCountry" => settings.DestinationCountryCell,
                "TradeTerms" => settings.TradeTermsCell,
                "TransportMode" => settings.TransportModeCell,
                "PaymentTerms" => settings.PaymentTermsCell,
                "Currency" => settings.CurrencyCell,
                "SupervisionMode" => settings.SupervisionModeCell,
                "LetterOfCreditNo" => settings.LetterOfCreditNoCell,
                "IssuingBank" => settings.IssuingBankCell,
                "ShippingMarks" => settings.ShippingMarksCell,
                _ => string.Empty
            };
        }

        private static string GetCell(SheetGrid sheet, int row, int column)
        {
            return column <= 0 ? string.Empty : sheet.Get(row, column);
        }

        private static string NormalizeFieldValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lines = value
                .Replace('\u00a0', ' ')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None)
                .SelectMany(line => Regex.Split(line, @"[ \t]{4,}"))
                .Select(line => Regex.Replace(line.Trim(), @"[ \t]{2,}", " "))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return string.Join(Environment.NewLine, lines);
        }

        private static decimal ParseExcelDecimal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string normalized = text
                .Trim()
                .Replace('\u00a0', ' ')
                .Replace("，", ",")
                .Replace("．", ".")
                .Replace("－", "-")
                .Replace("（", "(")
                .Replace("）", ")");

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal currentCultureResult))
            {
                return currentCultureResult;
            }

            string numericText = Regex.Replace(normalized, @"[^\d\.\,\-\(\)]", string.Empty);
            if (string.IsNullOrWhiteSpace(numericText) || !numericText.Any(char.IsDigit))
            {
                return 0;
            }

            bool negative = numericText.StartsWith("(", StringComparison.Ordinal) && numericText.EndsWith(")", StringComparison.Ordinal);
            numericText = numericText.Trim('(', ')').Replace(",", string.Empty);

            if (decimal.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal invariantResult))
            {
                return negative ? -invariantResult : invariantResult;
            }

            return 0;
        }

        private static bool IsHeader(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value == NormalizeHeader(candidate));
        }

        private static string NormalizeHeader(string value)
        {
            return NormalizeText(value);
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || IsCjk(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static bool IsCjk(char value)
        {
            return value >= '\u4e00' && value <= '\u9fff';
        }

        private static decimal ToDecimalConfidence(double value)
        {
            return Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
        }

        private static (int Row, int Column) ParseCellReference(string cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                throw new ArgumentException("Cell reference cannot be empty.", nameof(cellReference));
            }

            int column = 0;
            int index = 0;
            while (index < cellReference.Length && char.IsLetter(cellReference[index]))
            {
                column = (column * 26) + (char.ToUpperInvariant(cellReference[index]) - 'A' + 1);
                index++;
            }

            string rowText = cellReference[index..];
            if (column <= 0 || !int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row <= 0)
            {
                throw new ArgumentException($"Invalid cell reference: {cellReference}", nameof(cellReference));
            }

            return (row, column);
        }

        private sealed record FieldDefinition(
            string Key,
            string DisplayName,
            string[] Labels,
            bool MultiLine = false,
            bool PreferBelow = false);

        private readonly record struct LabelMatch(decimal Score, string Kind);

        private sealed record NearbyValueCandidate(string Value, int Row, int Column, decimal Score);

        private sealed record SheetProfile(
            SheetGrid Sheet,
            Dictionary<string, ExcelImportFieldAnalysis> Fields,
            ExcelImportItemTableAnalysis? ItemTable,
            double Score,
            double Confidence)
        {
            public int SheetIndex { get; init; }
        }

        private sealed class SheetGrid
        {
            private readonly IReadOnlyList<IReadOnlyList<string>> _rows;

            public SheetGrid(string name, IReadOnlyList<List<string>> rows)
            {
                Name = name ?? string.Empty;
                _rows = rows.Select(row => (IReadOnlyList<string>)row).ToList();

                for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
                {
                    var row = _rows[rowIndex];
                    for (int columnIndex = 0; columnIndex < row.Count; columnIndex++)
                    {
                        if (string.IsNullOrWhiteSpace(row[columnIndex]))
                        {
                            continue;
                        }

                        UsedRowCount = rowIndex + 1;
                        UsedColumnCount = Math.Max(UsedColumnCount, columnIndex + 1);
                    }
                }
            }

            public string Name { get; }

            public int UsedRowCount { get; }

            public int UsedColumnCount { get; }

            public string Get(int oneBasedRow, int oneBasedColumn)
            {
                if (oneBasedRow <= 0 || oneBasedColumn <= 0 || oneBasedRow > _rows.Count)
                {
                    return string.Empty;
                }

                var row = _rows[oneBasedRow - 1];
                if (oneBasedColumn > row.Count)
                {
                    return string.Empty;
                }

                return row[oneBasedColumn - 1] ?? string.Empty;
            }
        }
    }
}
