using ExportDocManager.Models.DTOs;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public sealed partial class BuiltInExcelImportAnalyzer
    {
        private static ExcelImportFieldAnalysis PickBetter(
            ExcelImportFieldAnalysis current,
            ExcelImportFieldAnalysis candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Value))
            {
                return current;
            }

            if (current == null)
            {
                return candidate;
            }

            if (candidate.Confidence > current.Confidence)
            {
                return candidate;
            }

            if (candidate.Confidence == current.Confidence
                && candidate.Row > 0
                && (current.Row == 0 || candidate.Row < current.Row))
            {
                return candidate;
            }

            return current;
        }

        private static ExcelImportFieldAnalysis CreateField(
            FieldDefinition definition,
            string value,
            string worksheetName,
            int row,
            int column,
            decimal confidence,
            string source)
        {
            return new ExcelImportFieldAnalysis
            {
                FieldKey = definition.Key,
                DisplayName = definition.DisplayName,
                Value = NormalizeFieldValue(value),
                WorksheetName = worksheetName,
                Row = row,
                Column = column,
                Confidence = Math.Min(1m, Math.Max(0m, confidence)),
                Source = source
            };
        }

        private static (string Value, int Row, int Column) FindNearbyValue(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            candidates.AddRange(FindSameRowValueCandidates(sheet, row, column, multiLine));
            candidates.AddRange(FindBelowValueCandidates(sheet, row, column, multiLine));
            if (ShouldProbeBelowNeighborColumn(sheet, row, column))
            {
                candidates.AddRange(FindBelowValueCandidates(sheet, row, column + 1, multiLine));
            }

            var best = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .FirstOrDefault();

            return best == null
                ? (string.Empty, 0, 0)
                : (best.Value, best.Row, best.Column);
        }

        private static (string Value, int Row, int Column) FindBestBelowValue(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var best = FindBelowValueCandidates(sheet, row, column, multiLine)
                .Concat(ShouldProbeBelowNeighborColumn(sheet, row, column)
                    ? FindBelowValueCandidates(sheet, row, column + 1, multiLine)
                    : Enumerable.Empty<NearbyValueCandidate>())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .FirstOrDefault();

            return best == null
                ? (string.Empty, 0, 0)
                : (best.Value, best.Row, best.Column);
        }

        private static bool ShouldProbeBelowNeighborColumn(SheetGrid sheet, int row, int column)
        {
            if (column + 1 > sheet.UsedColumnCount)
            {
                return false;
            }

            string neighborHeader = sheet.Get(row, column + 1);
            return string.IsNullOrWhiteSpace(neighborHeader)
                || (!IsFieldBoundaryValue(neighborHeader) && !LooksLikeSequenceHeader(neighborHeader));
        }

        private static bool LooksLikeSequenceHeader(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "序号" or "编号" or "行号" or "no" or "number" or "serialno" or "serialnumber" or "itemno";
        }

        private static IReadOnlyList<NearbyValueCandidate> FindSameRowValueCandidates(
            SheetGrid sheet,
            int row,
            int labelColumn,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            int startColumn = labelColumn + 1;
            int maxColumn = Math.Min(startColumn + (multiLine ? 8 : 2), sheet.UsedColumnCount);
            for (int column = startColumn; column <= maxColumn; column++)
            {
                string value = sheet.Get(row, column).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (IsFieldBoundaryValue(value))
                {
                    break;
                }

                if (HasFieldBoundaryBetween(sheet, row, labelColumn + 1, column - 1))
                {
                    break;
                }

                string candidateValue = multiLine
                    ? CollectVerticalBlock(sheet, row, column, value)
                    : value;

                decimal score = 100m
                    - ((column - startColumn) * 4m)
                    + ScoreValueCompleteness(candidateValue, multiLine);

                candidates.Add(new NearbyValueCandidate(candidateValue, row, column, score));

                if (!multiLine)
                {
                    continue;
                }
            }

            return candidates;
        }

        private static IReadOnlyList<NearbyValueCandidate> FindBelowValueCandidates(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            int blankRows = 0;
            for (int nextRow = row + 1; nextRow <= Math.Min(row + 8, sheet.UsedRowCount); nextRow++)
            {
                string value = sheet.Get(nextRow, column).Trim();
                int valueColumn = column;
                if (string.IsNullOrWhiteSpace(value) && column + 1 <= sheet.UsedColumnCount)
                {
                    value = sheet.Get(nextRow, column + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        valueColumn = column + 1;
                    }
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    blankRows++;
                    if (blankRows >= 2)
                    {
                        break;
                    }

                    continue;
                }

                if (IsFieldBoundaryValue(value) || HasKnownLabelBeforeColumn(sheet, nextRow, valueColumn))
                {
                    break;
                }

                string candidateValue = multiLine
                    ? CollectVerticalBlock(sheet, nextRow, valueColumn, value)
                    : value;

                decimal score = 88m
                    - ((nextRow - row - 1) * 6m)
                    - ((valueColumn - column) * 2m)
                    + ScoreValueCompleteness(candidateValue, multiLine);

                candidates.Add(new NearbyValueCandidate(candidateValue, nextRow, valueColumn, score));

                if (!multiLine)
                {
                    continue;
                }
            }

            return candidates;
        }

        private static bool HasFieldBoundaryBetween(SheetGrid sheet, int row, int startColumn, int endColumn)
        {
            for (int column = Math.Max(1, startColumn); column <= endColumn; column++)
            {
                if (IsFieldBoundaryValue(sheet.Get(row, column)))
                {
                    return true;
                }
            }

            return false;
        }

        private static decimal ScoreValueCompleteness(string value, bool multiLine)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0m;
            }

            if (!multiLine)
            {
                return value.Length >= 3 ? 2m : 0m;
            }

            int lineCount = value
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));

            return Math.Min(6m, lineCount * 1.5m);
        }

        private static string CollectVerticalBlock(SheetGrid sheet, int startRow, int column, string firstValue)
        {
            var lines = new List<string>();
            AddBlockLine(lines, firstValue);

            for (int row = startRow + 1; row <= Math.Min(startRow + 12, sheet.UsedRowCount); row++)
            {
                string value = sheet.Get(row, column).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    break;
                }

                if (IsFieldBoundaryValue(value))
                {
                    break;
                }

                AddBlockLine(lines, value);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static void AddBlockLine(List<string> lines, string value)
        {
            string normalized = NormalizeFieldValue(value);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !lines.Any(line => string.Equals(line, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(normalized);
            }
        }

        private static string ExtractInlineValue(string value, IReadOnlyList<string> labels)
        {
            foreach (string label in labels)
            {
                if (TryGetInlineTextAfterLabel(value, label, out string afterLabel))
                {
                    string extracted = afterLabel.TrimStart(' ', '\t', ':', '：', '#').Trim();
                    if (!string.IsNullOrWhiteSpace(extracted) && !LooksLikeKnownLabel(extracted))
                    {
                        return extracted;
                    }
                }
            }

            return string.Empty;
        }

        private static LabelMatch? MatchLabel(string value, IReadOnlyList<string> labels)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            foreach (string label in labels)
            {
                string normalizedLabel = NormalizeText(label);
                if (normalized == normalizedLabel)
                {
                    return new LabelMatch(0.9m, "LabelExact");
                }

                if (normalizedLabel.Length >= 4
                    && normalized.StartsWith(normalizedLabel, StringComparison.Ordinal)
                    && normalized.Length <= normalizedLabel.Length + 16)
                {
                    if (!TryGetInlineTextAfterLabel(value, label, out _))
                    {
                        continue;
                    }

                    if (LooksLikeCodeValue(value))
                    {
                        continue;
                    }

                    return new LabelMatch(0.82m, "LabelPrefix");
                }

                if (normalized.Contains(normalizedLabel, StringComparison.Ordinal)
                    && normalizedLabel.Length >= 3
                    && normalized.Length <= Math.Max(12, normalizedLabel.Length * 3))
                {
                    return new LabelMatch(0.72m, "LabelContains");
                }
            }

            return null;
        }

        private static bool TryGetInlineTextAfterLabel(string value, string label, out string afterLabel)
        {
            afterLabel = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            var pattern = Regex.Escape(label).Replace("\\ ", "\\s*");
            var match = Regex.Match(
                value,
                $@"^\s*{pattern}(?<after>.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            afterLabel = match.Groups["after"].Value;
            if (string.IsNullOrWhiteSpace(afterLabel))
            {
                return false;
            }

            char first = afterLabel[0];
            if (first is ':' or '：' or '#')
            {
                return true;
            }

            return char.IsWhiteSpace(first) && !IsSingleWordAsciiLabel(label);
        }

        private static bool IsSingleWordAsciiLabel(string label)
        {
            return label.All(c => c <= '\u007f')
                && !label.Any(char.IsWhiteSpace)
                && !label.Contains('/', StringComparison.Ordinal);
        }

        private static bool IsAddressLabelForDifferentField(string value, FieldDefinition definition)
        {
            if (definition.Key.Contains("Address", StringComparison.Ordinal))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            return normalized.Contains("address", StringComparison.Ordinal)
                || normalized.Contains("地址", StringComparison.Ordinal);
        }

        private static bool LooksLikeCodeValue(string value)
        {
            return value.Contains('-', StringComparison.Ordinal)
                && value.Any(char.IsDigit)
                && !value.Contains(':', StringComparison.Ordinal)
                && !value.Contains('：', StringComparison.Ordinal);
        }

        private static bool IsGenericPlaceholderValue(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "name" or "address" or "名称" or "地址" or "shipper" or "exporter" or "consignee" or "customer";
        }

        private static bool IsUsableFieldValue(string fieldKey, string value)
        {
            return !string.Equals(fieldKey, "DestinationCountry", StringComparison.Ordinal)
                || ExcelImportFieldValueValidator.IsDestinationCountry(value);
        }

        private static bool LooksLikeKnownLabel(string value)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return FieldDefinitions
                .SelectMany(definition => definition.Labels)
                .Any(label => normalized == NormalizeText(label) || TryGetInlineTextAfterLabel(value, label, out _));
        }

        private static bool LooksLikeItemHeader(string value)
        {
            var columns = new ExcelImportItemColumnAnalysis();
            return TrySetItemColumn(columns, NormalizeHeader(value), 1) > 0;
        }

        private static bool IsFieldBoundaryValue(string value)
        {
            return LooksLikeKnownLabel(value) || LooksLikeItemHeader(value);
        }

        private static bool HasKnownLabelBeforeColumn(SheetGrid sheet, int row, int column)
        {
            for (int previousColumn = Math.Max(1, column - 3); previousColumn < column; previousColumn++)
            {
                if (IsFieldBoundaryValue(sheet.Get(row, previousColumn)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLikelyTotalRow(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "合计" or "总计" or "total" or "subtotal";
        }

        private static int CountPresent(Dictionary<string, ExcelImportFieldAnalysis> fields, params string[] keys)
        {
            return keys.Count(key => fields.TryGetValue(key, out var field) && !string.IsNullOrWhiteSpace(field.Value));
        }

        private static void AddCompletenessIssues(ExcelImportAnalysisReport report)
        {
            if (report == null)
            {
                return;
            }

            AddMissingFieldIssue(report, "InvoiceNo", "未能高置信度识别发票号。");
            AddMissingFieldIssue(report, "CustomerNameEN", "未能高置信度识别收货人。");
            AddMissingFieldIssue(report, "ExporterNameEN", "未能高置信度识别出口商/SHIPPER。");
            AddMissingFieldIssue(report, "PortOfLoading", "未能高置信度识别起运港。");
            AddMissingFieldIssue(report, "PortOfDestination", "未能高置信度识别目的港/目的地。");

            if (report.ItemTable == null)
            {
                report.Issues.Add(new ExcelImportAnalysisIssue
                {
                    Severity = "Warning",
                    Code = "MissingItemTable",
                    Message = "未识别到商品明细表头，将回退到当前 Excel 导入方案的固定行列配置。"
                });
            }
        }

        private static void AddMissingFieldIssue(ExcelImportAnalysisReport report, string fieldKey, string message)
        {
            var field = report.Fields.FirstOrDefault(item => string.Equals(item.FieldKey, fieldKey, StringComparison.Ordinal));
            if (field != null && !string.IsNullOrWhiteSpace(field.Value) && field.Confidence >= 0.65m)
            {
                return;
            }

            report.Issues.Add(new ExcelImportAnalysisIssue
            {
                Severity = "Warning",
                Code = "LowConfidenceField",
                FieldKey = fieldKey,
                Message = message
            });
        }
    }
}
