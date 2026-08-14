using ExportDocManager.Models.Entities;
using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public partial class ExcelImportService
    {
        private const int SupplementaryTableScanRows = 400;
        private const int SupplementaryTableScanColumns = 30;
        private const int SupplementaryTableMaxDataRows = 200;

        private static readonly Regex AudienceDescriptionBoundary = new(
            @"(?<![a-z])(?:women['’]?s|men['’]?s|ladies['’]?|boys?['’]?|girls?['’]?|unisex|kids?['’]?|children['’]?s|baby['’]?s)(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private void EnrichItemsFromSupplementaryTable(
            IExcelImportWorksheet worksheet,
            IReadOnlyList<Item> items,
            int scanStartRow,
            CancellationToken cancellationToken)
        {
            if (items.Count == 0)
            {
                return;
            }

            var table = FindSupplementaryItemTable(
                worksheet,
                items,
                Math.Max(1, scanStartRow),
                cancellationToken);
            if (table == null)
            {
                return;
            }

            var itemsByPair = items
                .Where(item => !string.IsNullOrWhiteSpace(item.StyleNo))
                .GroupBy(item => BuildItemPairKey(item.PoNumber, item.StyleNo), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrEmpty(group.Key))
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var itemsByStyle = items
                .Where(item => !string.IsNullOrWhiteSpace(item.StyleNo))
                .GroupBy(item => NormalizeItemLookupKey(item.StyleNo), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrEmpty(group.Key)
                    && group
                        .Select(item => NormalizeItemLookupKey(item.PoNumber))
                        .Distinct(StringComparer.Ordinal)
                        .Count() <= 1)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            int unmatchedRowCount = 0;
            bool matchedAnyRow = false;
            int lastRow = table.HeaderRow + SupplementaryTableMaxDataRows;
            for (int row = table.HeaderRow + 1; row <= lastRow; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string styleNo = GetItemCellValue(worksheet, row, table.StyleNoCol);
                string styleKey = NormalizeItemLookupKey(styleNo);
                if (string.IsNullOrEmpty(styleKey))
                {
                    if (matchedAnyRow && ++unmatchedRowCount >= 8)
                    {
                        break;
                    }

                    continue;
                }

                string poNumber = GetItemCellValue(worksheet, row, table.PoNumberCol);
                string pairKey = BuildItemPairKey(poNumber, styleNo);
                Item[]? matchingItems = null;
                if (!string.IsNullOrEmpty(pairKey))
                {
                    itemsByPair.TryGetValue(pairKey, out matchingItems);
                }

                if (matchingItems == null
                    && (table.PoNumberCol == 0 || string.IsNullOrWhiteSpace(poNumber)))
                {
                    itemsByStyle.TryGetValue(styleKey, out matchingItems);
                }

                if (matchingItems == null || matchingItems.Length == 0)
                {
                    if (matchedAnyRow && ++unmatchedRowCount >= 8)
                    {
                        break;
                    }

                    continue;
                }

                foreach (var item in matchingItems)
                {
                    ApplySupplementaryItemValues(worksheet, row, table, item);
                }

                matchedAnyRow = true;
                unmatchedRowCount = 0;
            }
        }

        private SupplementaryItemTable? FindSupplementaryItemTable(
            IExcelImportWorksheet worksheet,
            IReadOnlyList<Item> items,
            int scanStartRow,
            CancellationToken cancellationToken)
        {
            int lastHeaderRow = scanStartRow + SupplementaryTableScanRows;
            for (int row = scanStartRow; row <= lastHeaderRow; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var columns = DetectSupplementaryValueColumns(worksheet, row);
                if (columns.AttributeCount < 2 || (columns.StyleNameCNCol == 0 && columns.BrandCol == 0))
                {
                    continue;
                }

                int sampleRowCount = Math.Min(
                    SupplementaryTableMaxDataRows,
                    Math.Max(20, items.Count * 2));
                var styleKeys = items
                    .Select(item => NormalizeItemLookupKey(item.StyleNo))
                    .Where(key => !string.IsNullOrEmpty(key))
                    .ToHashSet(StringComparer.Ordinal);
                int styleNoCol = FindBestIdentifierColumn(
                    worksheet,
                    row + 1,
                    sampleRowCount,
                    styleKeys,
                    []);
                if (styleNoCol == 0)
                {
                    continue;
                }

                var poKeys = items
                    .Select(item => NormalizeItemLookupKey(item.PoNumber))
                    .Where(key => !string.IsNullOrEmpty(key))
                    .ToHashSet(StringComparer.Ordinal);
                int poNumberCol = FindBestIdentifierColumn(
                    worksheet,
                    row + 1,
                    sampleRowCount,
                    poKeys,
                    [styleNoCol]);

                columns.HeaderRow = row;
                columns.StyleNoCol = styleNoCol;
                columns.PoNumberCol = poNumberCol;
                return columns;
            }

            return null;
        }

        private SupplementaryItemTable DetectSupplementaryValueColumns(
            IExcelImportWorksheet worksheet,
            int headerRow)
        {
            var table = new SupplementaryItemTable();
            for (int column = 1; column <= SupplementaryTableScanColumns; column++)
            {
                string header = NormalizeHeader(GetItemCellValue(worksheet, headerRow, column));
                if (string.IsNullOrEmpty(header))
                {
                    continue;
                }

                if (IsHeader(header,
                    "成分及英文名", "成份及英文名", "面料成分及英文品名",
                    "composition and description", "composition description", "fabric and description"))
                {
                    table.CompositionAndNameCol = column;
                }
                else if (IsHeader(header, "面料成分", "成份", "成分", "材质", "fabric composition", "composition"))
                {
                    table.FabricCompositionCol = column;
                }
                else if (IsHeader(header, "英文品名", "英文名称", "英文描述", "customs description", "description"))
                {
                    table.StyleNameCol = column;
                }

                if (IsHeader(header,
                    "报关中文名", "报关中文品名", "中文品名", "中文名称",
                    "货物中文名称", "customs chinese description"))
                {
                    table.StyleNameCNCol = column;
                }

                if (IsHeader(header, "品牌", "品牌名", "商标", "brand", "label"))
                {
                    table.BrandCol = column;
                }

                if (IsHeader(header, "报关hs编码", "申报hs编码", "海关hs编码", "customs hs code"))
                {
                    table.CustomsHsCodeCol = column;
                }
                else if (IsHeader(header, "提单hs编码", "提单hs code", "bl hs code", "shipping hs code"))
                {
                    table.BillHsCodeCol = column;
                }
            }

            return table;
        }

        private int FindBestIdentifierColumn(
            IExcelImportWorksheet worksheet,
            int dataStartRow,
            int sampleRowCount,
            IReadOnlySet<string> knownKeys,
            IReadOnlyCollection<int> excludedColumns)
        {
            if (knownKeys.Count == 0)
            {
                return 0;
            }

            int bestColumn = 0;
            int bestMatchCount = 0;
            for (int column = 1; column <= SupplementaryTableScanColumns; column++)
            {
                if (excludedColumns.Contains(column))
                {
                    continue;
                }

                int matchCount = 0;
                for (int row = dataStartRow; row < dataStartRow + sampleRowCount; row++)
                {
                    string key = NormalizeItemLookupKey(GetItemCellValue(worksheet, row, column));
                    if (knownKeys.Contains(key))
                    {
                        matchCount++;
                    }
                }

                if (matchCount > bestMatchCount)
                {
                    bestColumn = column;
                    bestMatchCount = matchCount;
                }
            }

            return bestColumn;
        }

        private void ApplySupplementaryItemValues(
            IExcelImportWorksheet worksheet,
            int row,
            SupplementaryItemTable table,
            Item item)
        {
            string combined = GetItemCellValue(worksheet, row, table.CompositionAndNameCol);
            var (fabricComposition, styleName) = SplitCompositionAndEnglishName(combined);

            string explicitFabric = GetItemCellValue(worksheet, row, table.FabricCompositionCol);
            if (!string.IsNullOrWhiteSpace(explicitFabric))
            {
                fabricComposition = explicitFabric;
            }

            string explicitStyleName = GetItemCellValue(worksheet, row, table.StyleNameCol);
            if (!string.IsNullOrWhiteSpace(explicitStyleName))
            {
                styleName = explicitStyleName;
            }

            if (!string.IsNullOrWhiteSpace(fabricComposition))
            {
                item.FabricComposition = NormalizeExcelTextBlock(fabricComposition);
            }

            if (!string.IsNullOrWhiteSpace(styleName))
            {
                item.StyleName = NormalizeExcelTextBlock(styleName);
            }

            string chineseName = GetItemCellValue(worksheet, row, table.StyleNameCNCol);
            if (!string.IsNullOrWhiteSpace(chineseName))
            {
                item.StyleNameCN = NormalizeExcelTextBlock(chineseName);
            }

            string brand = GetItemCellValue(worksheet, row, table.BrandCol);
            if (!string.IsNullOrWhiteSpace(brand))
            {
                item.Brand = NormalizeExcelTextBlock(brand);
            }

            string hsCode = GetItemCellValue(worksheet, row, table.CustomsHsCodeCol);
            if (string.IsNullOrWhiteSpace(hsCode))
            {
                hsCode = GetItemCellValue(worksheet, row, table.BillHsCodeCol);
            }

            if (!string.IsNullOrWhiteSpace(hsCode))
            {
                item.HSCode = NormalizeExcelTextBlock(hsCode);
            }

            NormalizeItemDescriptionLanguages(item);
            NormalizeItemDescriptionAndBrand(item);
        }

        private static (string FabricComposition, string StyleName) SplitCompositionAndEnglishName(string value)
        {
            string normalized = NormalizeExcelTextBlock(value);
            if (string.IsNullOrEmpty(normalized))
            {
                return (string.Empty, string.Empty);
            }

            var boundary = AudienceDescriptionBoundary.Match(normalized);
            if (boundary.Success)
            {
                string composition = normalized[..boundary.Index].Trim();
                if (composition.Contains('%'))
                {
                    return (composition, normalized[boundary.Index..].Trim());
                }
            }

            return normalized.Contains('%')
                ? (normalized, string.Empty)
                : (string.Empty, normalized);
        }

        private static string BuildItemPairKey(string? poNumber, string? styleNo)
        {
            string poKey = NormalizeItemLookupKey(poNumber);
            string styleKey = NormalizeItemLookupKey(styleNo);
            return string.IsNullOrEmpty(poKey) || string.IsNullOrEmpty(styleKey)
                ? string.Empty
                : $"{poKey}\u001f{styleKey}";
        }

        private static string NormalizeItemLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character) || IsCjk(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }

            string normalized = builder.ToString();
            return normalized == "0" ? string.Empty : normalized;
        }

        private sealed class SupplementaryItemTable
        {
            public int HeaderRow { get; set; }

            public int PoNumberCol { get; set; }

            public int StyleNoCol { get; set; }

            public int CompositionAndNameCol { get; set; }

            public int FabricCompositionCol { get; set; }

            public int StyleNameCol { get; set; }

            public int StyleNameCNCol { get; set; }

            public int BrandCol { get; set; }

            public int CustomsHsCodeCol { get; set; }

            public int BillHsCodeCol { get; set; }

            public int AttributeCount =>
                (CompositionAndNameCol > 0 ? 1 : 0)
                + (FabricCompositionCol > 0 ? 1 : 0)
                + (StyleNameCol > 0 ? 1 : 0)
                + (StyleNameCNCol > 0 ? 1 : 0)
                + (BrandCol > 0 ? 1 : 0)
                + (CustomsHsCodeCol > 0 ? 1 : 0)
                + (BillHsCodeCol > 0 ? 1 : 0);
        }
    }
}
