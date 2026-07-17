using ExportDocManager.Models.DTOs;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    internal static class ExcelImportColumnValueInference
    {
        private const int MaxSampleRows = 30;
        private const int MaxColumns = 60;

        public static void InferMissingColumns(
            ExcelImportItemColumnAnalysis columns,
            int dataStartRow,
            int usedColumnCount,
            Func<int, int, string> getCellValue)
        {
            if (columns == null || dataStartRow <= 0 || getCellValue == null)
            {
                return;
            }

            int maxColumn = Math.Clamp(Math.Max(usedColumnCount, GetMaxKnownColumn(columns)), 1, MaxColumns);
            var profiles = Enumerable
                .Range(1, maxColumn)
                .Select(column => BuildProfile(column, dataStartRow, getCellValue))
                .ToList();

            InferHsCodeColumn(columns, profiles);
            InferDimensionColumn(columns, profiles);
            InferUnitAndTotalPriceColumns(columns, profiles, dataStartRow, getCellValue);
            InferVolumeColumn(columns, profiles, dataStartRow, getCellValue);
            InferWeightColumns(columns, profiles, dataStartRow, getCellValue);
        }

        private static void InferHsCodeColumn(
            ExcelImportItemColumnAnalysis columns,
            IReadOnlyList<ColumnProfile> profiles)
        {
            if (columns.HSCodeCol > 0)
            {
                return;
            }

            var best = profiles
                .Where(profile => !IsAssigned(columns, profile.Column))
                .Where(profile => profile.HsCodeCount >= Math.Max(1, Math.Min(2, profile.NonEmptyCount)))
                .OrderByDescending(profile => profile.HsCodeCount)
                .ThenBy(profile => profile.Column)
                .FirstOrDefault();

            if (best != null)
            {
                columns.HSCodeCol = best.Column;
            }
        }

        private static void InferDimensionColumn(
            ExcelImportItemColumnAnalysis columns,
            IReadOnlyList<ColumnProfile> profiles)
        {
            if (columns.DimensionCol > 0 || (columns.LengthCol > 0 && columns.WidthCol > 0 && columns.HeightCol > 0))
            {
                return;
            }

            var best = profiles
                .Where(profile => !IsAssigned(columns, profile.Column))
                .Where(profile => profile.DimensionCount >= Math.Max(1, Math.Min(2, profile.NonEmptyCount)))
                .OrderByDescending(profile => profile.DimensionCount)
                .ThenBy(profile => profile.Column)
                .FirstOrDefault();

            if (best != null)
            {
                columns.DimensionCol = best.Column;
            }
        }

        private static void InferUnitAndTotalPriceColumns(
            ExcelImportItemColumnAnalysis columns,
            IReadOnlyList<ColumnProfile> profiles,
            int dataStartRow,
            Func<int, int, string> getCellValue)
        {
            if (columns.QuantityCol <= 0 || (columns.UnitPriceCol > 0 && columns.TotalPriceCol > 0))
            {
                return;
            }

            var candidateColumns = profiles
                .Where(profile => profile.NumericCount > 0)
                .Where(profile => !IsAssigned(columns, profile.Column))
                .Select(profile => profile.Column)
                .ToList();

            if (columns.UnitPriceCol > 0 && columns.TotalPriceCol <= 0)
            {
                int totalColumn = FindRelatedPriceColumn(
                    dataStartRow,
                    columns.QuantityCol,
                    columns.UnitPriceCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsUnitPrice: true);
                if (totalColumn > 0)
                {
                    columns.TotalPriceCol = totalColumn;
                }

                return;
            }

            if (columns.TotalPriceCol > 0 && columns.UnitPriceCol <= 0)
            {
                int unitColumn = FindRelatedPriceColumn(
                    dataStartRow,
                    columns.QuantityCol,
                    columns.TotalPriceCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsUnitPrice: false);
                if (unitColumn > 0)
                {
                    columns.UnitPriceCol = unitColumn;
                }

                return;
            }

            var bestPair = FindBestPricePair(dataStartRow, columns.QuantityCol, candidateColumns, getCellValue);
            if (bestPair.UnitPriceColumn > 0 && bestPair.TotalPriceColumn > 0)
            {
                columns.UnitPriceCol = bestPair.UnitPriceColumn;
                columns.TotalPriceCol = bestPair.TotalPriceColumn;
            }
        }

        private static int FindRelatedPriceColumn(
            int dataStartRow,
            int quantityColumn,
            int knownPriceColumn,
            IReadOnlyList<int> candidateColumns,
            Func<int, int, string> getCellValue,
            bool knownColumnIsUnitPrice)
        {
            return candidateColumns
                .Select(column => new
                {
                    Column = column,
                    Score = CountPriceMatches(
                        dataStartRow,
                        quantityColumn,
                        knownColumnIsUnitPrice ? knownPriceColumn : column,
                        knownColumnIsUnitPrice ? column : knownPriceColumn,
                        getCellValue)
                })
                .Where(candidate => candidate.Score >= 2)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Column)
                .Select(candidate => candidate.Column)
                .FirstOrDefault();
        }

        private static (int UnitPriceColumn, int TotalPriceColumn, int Score) FindBestPricePair(
            int dataStartRow,
            int quantityColumn,
            IReadOnlyList<int> candidateColumns,
            Func<int, int, string> getCellValue)
        {
            var best = (UnitPriceColumn: 0, TotalPriceColumn: 0, Score: 0);

            foreach (int leftColumn in candidateColumns)
            {
                foreach (int rightColumn in candidateColumns)
                {
                    if (leftColumn == rightColumn)
                    {
                        continue;
                    }

                    int score = CountPriceMatches(dataStartRow, quantityColumn, leftColumn, rightColumn, getCellValue);
                    if (score > best.Score || (score == best.Score && IsEarlierPair(leftColumn, rightColumn, best)))
                    {
                        best = (leftColumn, rightColumn, score);
                    }
                }
            }

            return best.Score >= 2 ? best : default;
        }

        private static void InferVolumeColumn(
            ExcelImportItemColumnAnalysis columns,
            IReadOnlyList<ColumnProfile> profiles,
            int dataStartRow,
            Func<int, int, string> getCellValue)
        {
            if (columns.VolumeCol > 0
                || columns.CartonsCol <= 0
                || (columns.DimensionCol <= 0 && (columns.LengthCol <= 0 || columns.WidthCol <= 0 || columns.HeightCol <= 0)))
            {
                return;
            }

            var best = profiles
                .Where(profile => profile.NumericCount > 0)
                .Where(profile => !IsAssigned(columns, profile.Column))
                .Select(profile => new
                {
                    profile.Column,
                    Score = CountVolumeMatches(dataStartRow, columns, profile.Column, getCellValue)
                })
                .Where(candidate => candidate.Score >= 2)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Column)
                .FirstOrDefault();

            if (best != null)
            {
                columns.VolumeCol = best.Column;
            }
        }

        private static void InferWeightColumns(
            ExcelImportItemColumnAnalysis columns,
            IReadOnlyList<ColumnProfile> profiles,
            int dataStartRow,
            Func<int, int, string> getCellValue)
        {
            if (columns.CartonsCol <= 0)
            {
                return;
            }

            var candidateColumns = profiles
                .Where(profile => profile.NumericCount > 0)
                .Where(profile => !IsAssigned(columns, profile.Column))
                .Select(profile => profile.Column)
                .ToList();

            if (columns.GWPerCtnCol > 0 && columns.GWTotalCol <= 0)
            {
                columns.GWTotalCol = FindRelatedFactorColumn(
                    dataStartRow,
                    columns.CartonsCol,
                    columns.GWPerCtnCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsPerUnit: true);
            }

            if (columns.GWTotalCol > 0 && columns.GWPerCtnCol <= 0)
            {
                columns.GWPerCtnCol = FindRelatedFactorColumn(
                    dataStartRow,
                    columns.CartonsCol,
                    columns.GWTotalCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsPerUnit: false);
            }

            if (columns.NWPerCtnCol > 0 && columns.NWTotalCol <= 0)
            {
                columns.NWTotalCol = FindRelatedFactorColumn(
                    dataStartRow,
                    columns.CartonsCol,
                    columns.NWPerCtnCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsPerUnit: true);
            }

            if (columns.NWTotalCol > 0 && columns.NWPerCtnCol <= 0)
            {
                columns.NWPerCtnCol = FindRelatedFactorColumn(
                    dataStartRow,
                    columns.CartonsCol,
                    columns.NWTotalCol,
                    candidateColumns,
                    getCellValue,
                    knownColumnIsPerUnit: false);
            }

            if (columns.GWPerCtnCol > 0
                || columns.GWTotalCol > 0
                || columns.NWPerCtnCol > 0
                || columns.NWTotalCol > 0)
            {
                return;
            }

            var pairs = FindBestFactorPairs(dataStartRow, columns.CartonsCol, candidateColumns, getCellValue);
            if (pairs.Count < 2)
            {
                return;
            }

            var ordered = pairs
                .OrderByDescending(pair => pair.AverageTotal)
                .ThenBy(pair => pair.PerUnitColumn)
                .ToList();
            var gross = ordered[0];
            var net = ordered
                .Skip(1)
                .FirstOrDefault(pair => pair.PerUnitColumn != gross.PerUnitColumn && pair.TotalColumn != gross.TotalColumn);

            if (net == null)
            {
                return;
            }

            columns.GWPerCtnCol = gross.PerUnitColumn;
            columns.GWTotalCol = gross.TotalColumn;
            columns.NWPerCtnCol = net.PerUnitColumn;
            columns.NWTotalCol = net.TotalColumn;
        }

        private static bool IsEarlierPair(
            int unitPriceColumn,
            int totalPriceColumn,
            (int UnitPriceColumn, int TotalPriceColumn, int Score) current)
        {
            if (current.UnitPriceColumn == 0)
            {
                return true;
            }

            int candidateMin = Math.Min(unitPriceColumn, totalPriceColumn);
            int currentMin = Math.Min(current.UnitPriceColumn, current.TotalPriceColumn);
            return candidateMin < currentMin;
        }

        private static int CountPriceMatches(
            int dataStartRow,
            int quantityColumn,
            int unitPriceColumn,
            int totalPriceColumn,
            Func<int, int, string> getCellValue)
        {
            int matches = 0;
            for (int row = dataStartRow; row < dataStartRow + MaxSampleRows; row++)
            {
                decimal quantity = ParseExcelDecimal(getCellValue(row, quantityColumn));
                decimal unitPrice = ParseExcelDecimal(getCellValue(row, unitPriceColumn));
                decimal totalPrice = ParseExcelDecimal(getCellValue(row, totalPriceColumn));

                if (quantity <= 0 || unitPrice <= 0 || totalPrice <= 0)
                {
                    continue;
                }

                decimal expected = quantity * unitPrice;
                decimal tolerance = Math.Max(0.05m, Math.Abs(totalPrice) * 0.005m);
                if (Math.Abs(expected - totalPrice) <= tolerance)
                {
                    matches++;
                }
            }

            return matches;
        }

        private static int FindRelatedFactorColumn(
            int dataStartRow,
            int factorColumn,
            int knownColumn,
            IReadOnlyList<int> candidateColumns,
            Func<int, int, string> getCellValue,
            bool knownColumnIsPerUnit)
        {
            return candidateColumns
                .Select(column => new
                {
                    Column = column,
                    Score = CountFactorMatches(
                        dataStartRow,
                        factorColumn,
                        knownColumnIsPerUnit ? knownColumn : column,
                        knownColumnIsPerUnit ? column : knownColumn,
                        getCellValue)
                })
                .Where(candidate => candidate.Score >= 2)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Column)
                .Select(candidate => candidate.Column)
                .FirstOrDefault();
        }

        private static IReadOnlyList<FactorPair> FindBestFactorPairs(
            int dataStartRow,
            int factorColumn,
            IReadOnlyList<int> candidateColumns,
            Func<int, int, string> getCellValue)
        {
            var pairs = new List<FactorPair>();

            foreach (int perUnitColumn in candidateColumns)
            {
                foreach (int totalColumn in candidateColumns)
                {
                    if (perUnitColumn == totalColumn)
                    {
                        continue;
                    }

                    int score = CountFactorMatches(dataStartRow, factorColumn, perUnitColumn, totalColumn, getCellValue);
                    if (score < 2)
                    {
                        continue;
                    }

                    pairs.Add(new FactorPair(
                        perUnitColumn,
                        totalColumn,
                        score,
                        AveragePositiveValues(dataStartRow, totalColumn, getCellValue)));
                }
            }

            var selected = new List<FactorPair>();
            foreach (var pair in pairs
                .OrderByDescending(pair => pair.Score)
                .ThenByDescending(pair => pair.AverageTotal)
                .ThenBy(pair => pair.PerUnitColumn)
                .ThenBy(pair => pair.TotalColumn))
            {
                if (selected.Any(item =>
                    item.PerUnitColumn == pair.PerUnitColumn
                    || item.PerUnitColumn == pair.TotalColumn
                    || item.TotalColumn == pair.PerUnitColumn
                    || item.TotalColumn == pair.TotalColumn))
                {
                    continue;
                }

                selected.Add(pair);
                if (selected.Count == 2)
                {
                    break;
                }
            }

            return selected;
        }

        private static int CountFactorMatches(
            int dataStartRow,
            int factorColumn,
            int perUnitColumn,
            int totalColumn,
            Func<int, int, string> getCellValue)
        {
            int matches = 0;
            for (int row = dataStartRow; row < dataStartRow + MaxSampleRows; row++)
            {
                decimal factor = ParseExcelDecimal(getCellValue(row, factorColumn));
                decimal perUnit = ParseExcelDecimal(getCellValue(row, perUnitColumn));
                decimal total = ParseExcelDecimal(getCellValue(row, totalColumn));

                if (factor <= 0 || perUnit <= 0 || total <= 0)
                {
                    continue;
                }

                decimal expected = factor * perUnit;
                decimal tolerance = Math.Max(0.05m, Math.Abs(total) * 0.01m);
                if (Math.Abs(expected - total) <= tolerance)
                {
                    matches++;
                }
            }

            return matches;
        }

        private static int CountVolumeMatches(
            int dataStartRow,
            ExcelImportItemColumnAnalysis columns,
            int candidateColumn,
            Func<int, int, string> getCellValue)
        {
            int matches = 0;
            for (int row = dataStartRow; row < dataStartRow + MaxSampleRows; row++)
            {
                decimal cartons = ParseExcelDecimal(getCellValue(row, columns.CartonsCol));
                decimal actualVolume = ParseExcelDecimal(getCellValue(row, candidateColumn));
                if (cartons <= 0 || actualVolume <= 0)
                {
                    continue;
                }

                var dimensions = GetDimensions(row, columns, getCellValue);
                if (dimensions == null)
                {
                    continue;
                }

                decimal expected = dimensions.Value.Length * dimensions.Value.Width * dimensions.Value.Height * cartons / 1_000_000m;
                if (expected <= 0)
                {
                    continue;
                }

                decimal tolerance = Math.Max(0.01m, Math.Abs(expected) * 0.02m);
                if (Math.Abs(expected - actualVolume) <= tolerance)
                {
                    matches++;
                }
            }

            return matches;
        }

        private static (decimal Length, decimal Width, decimal Height)? GetDimensions(
            int row,
            ExcelImportItemColumnAnalysis columns,
            Func<int, int, string> getCellValue)
        {
            if (columns.LengthCol > 0 && columns.WidthCol > 0 && columns.HeightCol > 0)
            {
                decimal length = ParseExcelDecimal(getCellValue(row, columns.LengthCol));
                decimal width = ParseExcelDecimal(getCellValue(row, columns.WidthCol));
                decimal height = ParseExcelDecimal(getCellValue(row, columns.HeightCol));
                return length > 0 && width > 0 && height > 0
                    ? (length, width, height)
                    : null;
            }

            if (columns.DimensionCol > 0)
            {
                return TryParseDimensionParts(getCellValue(row, columns.DimensionCol));
            }

            return null;
        }

        private static decimal AveragePositiveValues(
            int dataStartRow,
            int column,
            Func<int, int, string> getCellValue)
        {
            var values = new List<decimal>();
            for (int row = dataStartRow; row < dataStartRow + MaxSampleRows; row++)
            {
                decimal value = ParseExcelDecimal(getCellValue(row, column));
                if (value > 0)
                {
                    values.Add(value);
                }
            }

            return values.Count == 0 ? 0 : values.Average();
        }

        private static ColumnProfile BuildProfile(
            int column,
            int dataStartRow,
            Func<int, int, string> getCellValue)
        {
            int nonEmptyCount = 0;
            int numericCount = 0;
            int hsCodeCount = 0;
            int dimensionCount = 0;

            for (int row = dataStartRow; row < dataStartRow + MaxSampleRows; row++)
            {
                string value = getCellValue(row, column)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                nonEmptyCount++;
                if (ParseExcelDecimal(value) != 0)
                {
                    numericCount++;
                }

                if (LooksLikeHsCode(value))
                {
                    hsCodeCount++;
                }

                if (LooksLikeDimension(value))
                {
                    dimensionCount++;
                }
            }

            return new ColumnProfile(column, nonEmptyCount, numericCount, hsCodeCount, dimensionCount);
        }

        private static bool LooksLikeHsCode(string value)
        {
            string digits = Regex.Replace(value ?? string.Empty, @"\D", string.Empty);
            return digits.Length is >= 8 and <= 13
                && Regex.IsMatch(value ?? string.Empty, @"^\s*[\d\.\-\s]+[A-Za-z]?\s*$");
        }

        private static bool LooksLikeDimension(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value
                .Replace('×', '*')
                .Replace('X', '*')
                .Replace('x', '*');

            return Regex.IsMatch(
                normalized,
                @"^\s*\d+(?:\.\d+)?\s*\*\s*\d+(?:\.\d+)?\s*\*\s*\d+(?:\.\d+)?\s*(?:cm|厘米|mm)?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static (decimal Length, decimal Width, decimal Height)? TryParseDimensionParts(string dimensionText)
        {
            if (string.IsNullOrWhiteSpace(dimensionText))
            {
                return null;
            }

            string digitsOnly = Regex.Replace(dimensionText, @"\D", string.Empty);
            if (digitsOnly.Length == 6 && digitsOnly == dimensionText.Trim())
            {
                decimal compactLength = ParseExcelDecimal(digitsOnly[..2]);
                decimal compactWidth = ParseExcelDecimal(digitsOnly.Substring(2, 2));
                decimal compactHeight = ParseExcelDecimal(digitsOnly.Substring(4, 2));
                return compactLength > 0 && compactWidth > 0 && compactHeight > 0
                    ? (compactLength, compactWidth, compactHeight)
                    : null;
            }

            string normalized = dimensionText
                .Replace('×', '*')
                .Replace('X', '*')
                .Replace('x', '*');

            var parts = Regex
                .Matches(normalized, @"\d+(?:\.\d+)?")
                .Select(match => ParseExcelDecimal(match.Value))
                .Where(value => value > 0)
                .Take(3)
                .ToArray();

            return parts.Length == 3 ? (parts[0], parts[1], parts[2]) : null;
        }

        private static bool IsAssigned(ExcelImportItemColumnAnalysis columns, int column)
        {
            return column > 0
                && (columns.PoNumberCol == column
                    || columns.StyleNoCol == column
                    || columns.StyleNameCol == column
                    || columns.FabricCompositionCol == column
                    || columns.StyleNameCNCol == column
                    || columns.BrandCol == column
                    || columns.HSCodeCol == column
                    || columns.OriginCol == column
                    || columns.QuantityCol == column
                    || columns.UnitENCol == column
                    || columns.UnitCNCol == column
                    || columns.CartonsCol == column
                    || columns.CtnUnitENCol == column
                    || columns.LengthCol == column
                    || columns.WidthCol == column
                    || columns.HeightCol == column
                    || columns.DimensionCol == column
                    || columns.VolumeCol == column
                    || columns.GWPerCtnCol == column
                    || columns.GWTotalCol == column
                    || columns.NWPerCtnCol == column
                    || columns.NWTotalCol == column
                    || columns.UnitPriceCol == column
                    || columns.TotalPriceCol == column);
        }

        private static int GetMaxKnownColumn(ExcelImportItemColumnAnalysis columns)
        {
            return new[]
            {
                columns.PoNumberCol,
                columns.StyleNoCol,
                columns.StyleNameCol,
                columns.FabricCompositionCol,
                columns.StyleNameCNCol,
                columns.BrandCol,
                columns.HSCodeCol,
                columns.OriginCol,
                columns.QuantityCol,
                columns.UnitENCol,
                columns.UnitCNCol,
                columns.CartonsCol,
                columns.CtnUnitENCol,
                columns.LengthCol,
                columns.WidthCol,
                columns.HeightCol,
                columns.DimensionCol,
                columns.VolumeCol,
                columns.GWPerCtnCol,
                columns.GWTotalCol,
                columns.NWPerCtnCol,
                columns.NWTotalCol,
                columns.UnitPriceCol,
                columns.TotalPriceCol
            }.Max();
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

        private sealed record ColumnProfile(
            int Column,
            int NonEmptyCount,
            int NumericCount,
            int HsCodeCount,
            int DimensionCount);

        private sealed record FactorPair(
            int PerUnitColumn,
            int TotalColumn,
            int Score,
            decimal AverageTotal);
    }
}
