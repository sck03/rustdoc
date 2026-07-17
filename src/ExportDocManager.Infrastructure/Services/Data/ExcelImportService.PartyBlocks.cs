using ClosedXML.Excel;
using ExcelDataReader;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public partial class ExcelImportService
    {
        private static (string Name, string Address)? GetUsablePartyBlock(string value)
        {
            var block = SplitPartyNameAndAddress(value);
            string name = NormalizeExcelTextBlock(block.Name);
            if (string.IsNullOrWhiteSpace(name)
                || LooksLikePartyRoleLabelOnly(name)
                || LooksLikeKnownDocumentLabel(name)
                || LooksLikeItemHeader(name))
            {
                return null;
            }

            return (name, NormalizeExcelTextBlock(block.Address));
        }

        private static (string Name, string Address)? MergePartyBlocks(
            (string Name, string Address)? primary,
            (string Name, string Address)? secondary)
        {
            if (primary == null)
            {
                return secondary;
            }

            if (secondary == null)
            {
                return primary;
            }

            string primaryAddress = NormalizeExcelTextBlock(primary.Value.Address);
            string secondaryAddress = NormalizeExcelTextBlock(secondary.Value.Address);
            if (!string.IsNullOrWhiteSpace(secondaryAddress)
                && ArePartyNamesEqual(primary.Value.Name, secondary.Value.Name)
                && (string.IsNullOrWhiteSpace(primaryAddress) || !IsUsablePartyAddressValue(primaryAddress)))
            {
                return (NormalizeExcelTextBlock(primary.Value.Name), secondaryAddress);
            }

            return primary;
        }

        private (string Name, string Address)? FindPartyBlockBelowLabel(
            IExcelImportWorksheet worksheet,
            IReadOnlyList<string> labels)
        {
            var label = FindLabelCell(worksheet, labels);
            if (label.Row <= 0 || label.Column <= 0)
            {
                return null;
            }

            (string Name, string Address)? fallback = null;
            foreach (int column in GetPartyBlockCandidateColumns(label.Column))
            {
                if (column != label.Column && HasSameRowValueForPartyColumn(worksheet, label.Row, column))
                {
                    continue;
                }

                var block = ReadPartyBlockBelowColumn(worksheet, label.Row, column);
                if (block == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(block.Value.Address))
                {
                    return block;
                }

                fallback ??= block;
            }

            return fallback;
        }

        private bool HasSameRowValueForPartyColumn(IExcelImportWorksheet worksheet, int row, int column)
        {
            string sameRowValue = NormalizeExcelTextBlock(GetCellValue(worksheet.Cell(row, column)));
            return !string.IsNullOrWhiteSpace(sameRowValue)
                && !LooksLikeKnownDocumentLabel(sameRowValue)
                && !LooksLikeItemHeader(sameRowValue);
        }

        private static IEnumerable<int> GetPartyBlockCandidateColumns(int labelColumn)
        {
            yield return labelColumn;
            if (labelColumn > 1)
            {
                yield return labelColumn - 1;
            }

            if (labelColumn < 30)
            {
                yield return labelColumn + 1;
            }
        }

        private (string Name, string Address)? ReadPartyBlockBelowColumn(
            IExcelImportWorksheet worksheet,
            int labelRow,
            int column)
        {
            var lines = new List<string>();
            int blankRows = 0;
            for (int row = labelRow + 1; row <= Math.Min(labelRow + 8, 100); row++)
            {
                string value = NormalizeExcelTextBlock(GetCellValue(worksheet.Cell(row, column)));
                if (string.IsNullOrWhiteSpace(value))
                {
                    blankRows++;
                    if (blankRows >= 2)
                    {
                        break;
                    }

                    continue;
                }

                if (LooksLikeKnownDocumentLabel(value) || LooksLikeItemHeader(value))
                {
                    break;
                }

                blankRows = 0;
                lines.Add(value);
            }

            return GetUsablePartyBlock(string.Join(Environment.NewLine, lines));
        }

        private static bool LooksLikePartyRoleLabelOnly(string value)
        {
            if (LooksLikeBusinessPartyValue(value))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(char.IsDigit))
            {
                return false;
            }

            string[] partyLabelAliases =
            [
                "发货人",
                "出口商",
                "收货人",
                "客户",
                "通知人",
                "通知方",
                "shipper",
                "exporter",
                "consignor",
                "seller",
                "consignee",
                "customer",
                "buyer",
                "notify party",
                "notify"
            ];

            return partyLabelAliases
                .Select(NormalizeText)
                .Any(label => IsSameOrNearShortLabel(normalized, label));
        }

        private static bool LooksLikeBusinessPartyValue(string value)
        {
            string normalized = NormalizeExcelTextBlock(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (LooksLikeAddressFragment(normalized))
            {
                return true;
            }

            return Regex.IsMatch(
                normalized,
                @"\b(CO\.?\s*,?\s*LTD\.?|LTD\.?|LIMITED|LLC\.?|INC\.?|CORP\.?|COMPANY|GROUP)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool LooksLikeBusinessPartyNameValue(string value)
        {
            string normalized = NormalizeExcelTextBlock(value);
            return !string.IsNullOrWhiteSpace(normalized)
                && Regex.IsMatch(
                    normalized,
                    @"\b(CO\.?\s*,?\s*LTD\.?|LTD\.?|LIMITED|LLC\.?|INC\.?|CORP\.?|COMPANY|GROUP)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsSameOrNearShortLabel(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            if (string.Equals(value, label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int lengthDelta = Math.Abs(value.Length - label.Length);
            if (label.Length < 5 || value.Length < 5 || lengthDelta > 1)
            {
                return false;
            }

            return LevenshteinDistanceAtMostOne(value, label);
        }

        private static bool LevenshteinDistanceAtMostOne(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Math.Abs(left.Length - right.Length) > 1)
            {
                return false;
            }

            int differences = 0;
            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.ToLowerInvariant(left[leftIndex]) == char.ToLowerInvariant(right[rightIndex]))
                {
                    leftIndex++;
                    rightIndex++;
                    continue;
                }

                differences++;
                if (differences > 1)
                {
                    return false;
                }

                if (left.Length > right.Length)
                {
                    leftIndex++;
                }
                else if (right.Length > left.Length)
                {
                    rightIndex++;
                }
                else
                {
                    leftIndex++;
                    rightIndex++;
                }
            }

            return true;
        }

        private static string RemoveLeadingPartyNameFromAddress(string address, string partyName)
        {
            string normalizedAddress = NormalizeExcelTextBlock(address);
            string normalizedPartyName = NormalizeExcelTextBlock(partyName);
            if (string.IsNullOrWhiteSpace(normalizedAddress) || string.IsNullOrWhiteSpace(normalizedPartyName))
            {
                return normalizedAddress;
            }

            var lines = normalizedAddress
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (lines.Count > 1 && ArePartyNamesEqual(lines[0], normalizedPartyName))
            {
                return NormalizeExcelTextBlock(string.Join(Environment.NewLine, lines.Skip(1)));
            }

            if (StartsWithPartyName(normalizedAddress, normalizedPartyName, out string rest))
            {
                return NormalizeExcelTextBlock(rest);
            }

            return normalizedAddress;
        }

        private static bool StartsWithPartyName(string value, string partyName, out string rest)
        {
            rest = string.Empty;
            string trimmed = value?.Trim() ?? string.Empty;
            string name = partyName?.Trim() ?? string.Empty;
            if (trimmed.Length <= name.Length || !trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            rest = trimmed[name.Length..].TrimStart(' ', '\t', ',', ';', '，', '；', ':', '：', '.', '。');
            return !string.IsNullOrWhiteSpace(rest);
        }

        private static bool ArePartyNamesEqual(string left, string right)
        {
            string normalizedLeft = NormalizeText(left);
            string normalizedRight = NormalizeText(right);
            return !string.IsNullOrWhiteSpace(normalizedLeft)
                && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameAsConsignee(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "sameasconsignee" or "sameasconsignees" or "sameconsignee";
        }


        private static (string Name, string Address) SplitPartyNameAndAddress(string value)
        {
            string normalized = NormalizeExcelTextBlock(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return (string.Empty, string.Empty);
            }

            var lines = normalized
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                return (string.Empty, string.Empty);
            }

            if (lines.Count > 1)
            {
                return (NormalizeExcelTextBlock(lines[0]), NormalizeExcelTextBlock(string.Join(Environment.NewLine, lines.Skip(1))));
            }

            return TrySplitSingleLineParty(lines[0]);
        }

        private static (string Name, string Address) TrySplitSingleLineParty(string value)
        {
            string line = NormalizeExcelTextBlock(value);
            if (string.IsNullOrWhiteSpace(line))
            {
                return (string.Empty, string.Empty);
            }

            string[] companySuffixPatterns =
            [
                @"\bCO\.?\s*,?\s*LTD\.?",
                @"\bLTD\.?",
                @"\bLIMITED\b",
                @"\bLLC\b",
                @"\bINC\.?",
                @"\bCORP\.?",
                @"\bCOMPANY\b"
            ];

            foreach (string pattern in companySuffixPatterns)
            {
                var matches = Regex.Matches(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (Match match in matches)
                {
                    int splitIndex = match.Index + match.Length;
                    if (splitIndex >= line.Length)
                    {
                        continue;
                    }

                    string rest = line[splitIndex..].Trim(" \t,;，；".ToCharArray());
                    if (LooksLikeAddressFragment(rest))
                    {
                        return (line[..splitIndex].Trim(), rest);
                    }
                }
            }

            return (line, string.Empty);
        }

        private static bool LooksLikeAddressFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Any(char.IsDigit)
                || normalized.Contains("road", StringComparison.Ordinal)
                || normalized.Contains("rd", StringComparison.Ordinal)
                || normalized.Contains("street", StringComparison.Ordinal)
                || normalized.Contains("st", StringComparison.Ordinal)
                || normalized.Contains("avenue", StringComparison.Ordinal)
                || normalized.Contains("ave", StringComparison.Ordinal)
                || normalized.Contains("building", StringComparison.Ordinal)
                || normalized.Contains("floor", StringComparison.Ordinal)
                || normalized.Contains("china", StringComparison.Ordinal)
                || normalized.Contains("united states", StringComparison.Ordinal)
                || normalized.Contains("tel", StringComparison.Ordinal)
                || normalized.Contains("mail", StringComparison.Ordinal)
                || normalized.Contains("路", StringComparison.Ordinal)
                || normalized.Contains("号", StringComparison.Ordinal);
        }
    }
}

