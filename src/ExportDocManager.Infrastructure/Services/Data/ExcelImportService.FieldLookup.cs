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
        private string GetCellValue(IExcelImportWorksheet worksheet, string cellReference, string defaultValue = "")
        {
            try
            {
                return worksheet.GetCellValue(
                    cellReference,
                    preserveRichText: cellReference.Equals("A20", StringComparison.OrdinalIgnoreCase)).Trim();
            }
            catch
            {
                return defaultValue;
            }
        }

        private string GetCellValue(IExcelImportCell cell, string defaultValue = "")
        {
            try
            {
                return cell.GetValue().Trim();
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string GetAnalyzedValue(ExcelImportAnalysisReport analysisReport, string fieldKey)
        {
            var field = analysisReport?.Fields?
                .Where(item => string.Equals(item.FieldKey, fieldKey, StringComparison.Ordinal))
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();

            return field != null && field.Confidence >= 0.55m && !string.IsNullOrWhiteSpace(field.Value)
                ? NormalizeExcelTextBlock(field.Value)
                : null;
        }

        private static string GetAnalyzedValue(
            ExcelImportAnalysisReport analysisReport,
            string fieldKey,
            Func<string, bool> valueValidator)
        {
            string value = GetAnalyzedValue(analysisReport, fieldKey);
            return valueValidator == null || valueValidator(value) ? value : null;
        }

        private static string PickMoreCompleteMultilineValue(string analyzedValue, string fallbackValue)
        {
            analyzedValue = NormalizeExcelTextBlock(analyzedValue);
            fallbackValue = NormalizeExcelTextBlock(fallbackValue);

            if (string.IsNullOrWhiteSpace(analyzedValue))
            {
                return fallbackValue ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fallbackValue))
            {
                return analyzedValue;
            }

            int analyzedLines = CountNonEmptyLines(analyzedValue);
            int fallbackLines = CountNonEmptyLines(fallbackValue);
            if (fallbackLines > analyzedLines)
            {
                return fallbackValue;
            }

            return fallbackValue.Length > analyzedValue.Length * 2
                ? fallbackValue
                : analyzedValue;
        }

        private static int CountNonEmptyLines(string value)
        {
            return value
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static string NormalizeExcelTextBlock(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value
                .Replace('\u00a0', ' ')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            normalized = Regex.Replace(normalized, @"[ \t]{4,}", "\n");

            var lines = normalized
                .Split('\n', StringSplitOptions.None)
                .Select(line => Regex.Replace(line.Trim(), @"[ \t]{2,}", " "))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return string.Join(Environment.NewLine, lines);
        }


        private string GetMultiLineAddress(IExcelImportWorksheet worksheet, string startCellRef, int lineCount)
        {
            try
            {
                var (startRow, col) = ParseCellReference(startCellRef);
                var startCell = worksheet.Cell(startRow, col);
                string address = GetCellValue(startCell);

                for (int i = 1; i < lineCount; i++)
                {
                    string line = GetCellValue(worksheet.Cell(startRow + i, col));
                    if (string.IsNullOrWhiteSpace(line) || LooksLikeKnownDocumentLabel(line) || LooksLikeItemHeader(line))
                    {
                        break;
                    }

                    if (!string.IsNullOrEmpty(line))
                    {
                        address += "\n" + line;
                    }
                }
                return NormalizeExcelTextBlock(address);
            }
            catch
            {
                return "";
            }
        }

        private string GetValueByLabelsOrCell(
            IExcelImportWorksheet worksheet,
            string cellReference,
            IReadOnlyList<string> labels,
            string defaultValue = "",
            Func<string, bool> configuredValueValidator = null)
        {
            string labelValue = FindValueBesideLabel(worksheet, labels);
            if (!string.IsNullOrWhiteSpace(labelValue))
            {
                if (configuredValueValidator == null || configuredValueValidator(labelValue))
                {
                    return labelValue;
                }
            }

            string configuredValue = GetCellValue(worksheet, cellReference);
            if (configuredValueValidator != null && !configuredValueValidator(configuredValue))
            {
                configuredValue = string.Empty;
            }

            return string.IsNullOrWhiteSpace(configuredValue) ? defaultValue : configuredValue;
        }

        private string FindValueBesideLabel(IExcelImportWorksheet worksheet, IReadOnlyList<string> labels)
        {
            for (int row = 1; row <= 80; row++)
            {
                for (int column = 1; column <= 30; column++)
                {
                    string value = GetCellValue(worksheet.Cell(row, column));
                    if (string.IsNullOrWhiteSpace(value) || !IsAnyLabel(value, labels))
                    {
                        continue;
                    }

                    string sameRowValue = FindNextNonEmptyCellInRow(worksheet, row, column + 1);
                    if (!string.IsNullOrWhiteSpace(sameRowValue))
                    {
                        return sameRowValue;
                    }

                    string belowValue = FindNextNonEmptyCellInColumn(worksheet, row + 1, column);
                    if (!string.IsNullOrWhiteSpace(belowValue))
                    {
                        return belowValue;
                    }
                }
            }

            return string.Empty;
        }

        private string GetShippingMarks(IExcelImportWorksheet worksheet, ExcelImportSettings settings)
        {
            var label = FindLabelCell(worksheet, ["唛头", "箱唛", "shipping mark", "marks"]);
            if (label.Row > 0)
            {
                var lines = new List<string>();
                int blankRows = 0;

                for (int row = label.Row + 1; row <= Math.Min(label.Row + 18, 80); row++)
                {
                    string value = GetCellValue(worksheet.Cell(row, label.Column));
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        blankRows++;
                        if (blankRows >= 5)
                        {
                            break;
                        }

                        continue;
                    }

                    blankRows = 0;
                    if (IsAnyLabel(value, ["shipping mark", "唛头"]))
                    {
                        continue;
                    }

                    lines.Add(value);
                }

                if (lines.Count > 0)
                {
                    return NormalizeExcelTextBlock(string.Join(Environment.NewLine, lines));
                }
            }

            return NormalizeExcelTextBlock(GetCellValue(worksheet, settings.ShippingMarksCell));
        }


        private (int Row, int Column) FindLabelCell(IExcelImportWorksheet worksheet, IReadOnlyList<string> labels)
        {
            for (int row = 1; row <= 80; row++)
            {
                for (int column = 1; column <= 30; column++)
                {
                    string value = GetCellValue(worksheet.Cell(row, column));
                    if (IsAnyLabel(value, labels))
                    {
                        return (row, column);
                    }
                }
            }

            return (0, 0);
        }

        private string FindNextNonEmptyCellInRow(IExcelImportWorksheet worksheet, int row, int startColumn)
        {
            for (int column = startColumn; column <= Math.Min(startColumn + 4, 30); column++)
            {
                string value = GetCellValue(worksheet.Cell(row, column));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (HasKnownLabelBeforeColumn(worksheet, row, column))
                    {
                        return string.Empty;
                    }

                    if (LooksLikeKnownDocumentLabel(value) || LooksLikeItemHeader(value))
                    {
                        return string.Empty;
                    }

                    return value;
                }
            }

            return string.Empty;
        }

        private string FindNextNonEmptyCellInColumn(IExcelImportWorksheet worksheet, int startRow, int column)
        {
            for (int row = startRow; row <= Math.Min(startRow + 3, 80); row++)
            {
                string value = GetCellValue(worksheet.Cell(row, column));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (LooksLikeKnownDocumentLabel(value) || LooksLikeItemHeader(value))
                    {
                        return string.Empty;
                    }

                    return value;
                }
            }

            return string.Empty;
        }

        private static bool IsAnyLabel(string value, IReadOnlyList<string> labels)
        {
            string normalizedValue = NormalizeLabel(value);
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return false;
            }

            return labels.Any(label =>
            {
                string normalizedLabel = NormalizeLabel(label);
                return normalizedValue == normalizedLabel
                    || (normalizedLabel.Length >= 4
                        && !LooksLikeCodeValue(value)
                        && normalizedValue.StartsWith(normalizedLabel, StringComparison.Ordinal)
                        && normalizedValue.Length <= normalizedLabel.Length + 16);
            });
        }

        private static bool LooksLikeCodeValue(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Any(char.IsDigit)
                && (normalized.Contains('-', StringComparison.Ordinal)
                    || normalized.Contains('/', StringComparison.Ordinal)
                    || normalized.Contains('#', StringComparison.Ordinal))
                && !normalized.Contains(':', StringComparison.Ordinal)
                && !normalized.Contains('：', StringComparison.Ordinal);
        }

        private static bool LooksLikeKnownDocumentLabel(string value)
        {
            if (LooksLikeBusinessPartyNameValue(value))
            {
                return false;
            }

            string normalized = NormalizeLabel(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return DocumentLabelAliases
                .Select(NormalizeLabel)
                .Any(label => normalized == label || (label.Length >= 4 && normalized.StartsWith(label, StringComparison.Ordinal)));
        }

        private static bool LooksLikeItemHeader(string value)
        {
            var columns = new DetectedItemColumns();
            return TrySetItemColumn(columns, NormalizeHeader(value), 1) > 0;
        }

        private static bool HasKnownLabelBeforeColumn(IExcelImportWorksheet worksheet, int row, int column)
        {
            for (int previousColumn = Math.Max(1, column - 3); previousColumn < column; previousColumn++)
            {
                string value = GetCellValueSafe(worksheet.Cell(row, previousColumn));
                if (LooksLikeKnownDocumentLabel(value) || LooksLikeItemHeader(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCellValueSafe(IExcelImportCell cell)
        {
            try
            {
                return cell.GetValue().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsHeader(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value == NormalizeHeader(candidate));
        }

        private static bool ContainsDigit(string value)
        {
            return value.Any(char.IsDigit);
        }

        private static bool IsLikelyTotalRow(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "合计" or "总计" or "小计" or "subtotal" or "total" or "grandtotal";
        }

        private static string NormalizeLabel(string value)
        {
            return NormalizeText(value);
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

        private DateTime ParseDate(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime date))
                return date;

            if (dateStr.Contains("."))
            {
                var parts = dateStr.Split('.');
                if (parts.Length == 3)
                {
                    if (int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int d))
                        return new DateTime(y, m, d);
                }
            }
            return DateTime.Now;
        }
    }
}

