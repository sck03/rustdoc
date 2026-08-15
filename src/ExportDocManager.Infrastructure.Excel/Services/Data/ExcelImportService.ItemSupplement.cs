using ExportDocManager.Models.Entities;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data;

public partial class ExcelImportService
{
    private const int SupplementaryTableScanRows = 400;
    private const int SupplementaryTableScanColumns = 30;
    private const int SupplementaryTableMaxDataRows = 200;

    private static readonly Regex AudienceDescriptionBoundary = new(
        @"(?<![a-z])(?:women['’]?s|men['’]?s|ladies['’]?|boys?['’]?|girls?['’]?|unisex|kids?['’]?|children['’]?s|baby['’]?s)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly (SupplementaryField Field, string[] Aliases)[] SupplementaryHeaders =
    [
        (SupplementaryField.CompositionAndName, ["成分及英文名", "成份及英文名", "面料成分及英文品名", "composition and description", "composition description", "fabric and description"]),
        (SupplementaryField.FabricComposition, ["面料成分", "成份", "成分", "材质", "fabric composition", "composition"]),
        (SupplementaryField.StyleName, ["英文品名", "英文名称", "英文描述", "customs description", "description"]),
        (SupplementaryField.StyleNameCn, ["报关中文名", "报关中文品名", "中文品名", "中文名称", "货物中文名称", "customs chinese description"]),
        (SupplementaryField.Brand, ["品牌", "品牌名", "商标", "brand", "label"]),
        (SupplementaryField.CustomsHsCode, ["报关hs编码", "申报hs编码", "海关hs编码", "customs hs code"]),
        (SupplementaryField.BillHsCode, ["提单hs编码", "提单hs code", "bl hs code", "shipping hs code"])
    ];

    private void EnrichItemsFromSupplementaryTable(
        IExcelImportWorksheet worksheet,
        IReadOnlyList<Item> items,
        int scanStartRow,
        CancellationToken cancellationToken)
    {
        var table = items.Count == 0
            ? null
            : FindSupplementaryItemTable(worksheet, items, Math.Max(1, scanStartRow), cancellationToken);
        if (table == null)
        {
            return;
        }

        var byPair = BuildSupplementaryItemIndex(items, item => BuildItemPairKey(item.PoNumber, item.StyleNo));
        var byStyle = items
            .Where(item => !string.IsNullOrWhiteSpace(item.StyleNo))
            .GroupBy(item => NormalizeItemLookupKey(item.StyleNo), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 && group.Select(item => NormalizeItemLookupKey(item.PoNumber)).Distinct(StringComparer.Ordinal).Count() <= 1)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        int emptyRowsAfterMatch = 0;
        bool matched = false;
        for (int row = table.HeaderRow + 1; row <= table.HeaderRow + SupplementaryTableMaxDataRows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string styleNo = GetItemCellValue(worksheet, row, table.StyleNoColumn);
            string poNumber = GetItemCellValue(worksheet, row, table.PoNumberColumn);
            Item[]? matches = byPair.GetValueOrDefault(BuildItemPairKey(poNumber, styleNo));
            if (matches == null && (table.PoNumberColumn == 0 || string.IsNullOrWhiteSpace(poNumber)))
            {
                matches = byStyle.GetValueOrDefault(NormalizeItemLookupKey(styleNo));
            }

            if (matches == null || matches.Length == 0)
            {
                if (matched && ++emptyRowsAfterMatch >= 8)
                {
                    break;
                }
                continue;
            }

            foreach (Item item in matches)
            {
                ApplySupplementaryItemValues(worksheet, row, table, item);
            }
            matched = true;
            emptyRowsAfterMatch = 0;
        }
    }

    private SupplementaryItemTable? FindSupplementaryItemTable(
        IExcelImportWorksheet worksheet,
        IReadOnlyList<Item> items,
        int scanStartRow,
        CancellationToken cancellationToken)
    {
        var styleKeys = BuildSupplementaryKeySet(items.Select(item => item.StyleNo));
        var poKeys = BuildSupplementaryKeySet(items.Select(item => item.PoNumber));
        int sampleRows = Math.Min(SupplementaryTableMaxDataRows, Math.Max(20, items.Count * 2));
        for (int row = scanStartRow; row <= scanStartRow + SupplementaryTableScanRows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = DetectSupplementaryValueColumns(worksheet, row);
            if (columns.Count < 2 || (!columns.ContainsKey(SupplementaryField.StyleNameCn) && !columns.ContainsKey(SupplementaryField.Brand)))
            {
                continue;
            }

            int styleColumn = FindBestIdentifierColumn(worksheet, row + 1, sampleRows, styleKeys, 0);
            if (styleColumn == 0)
            {
                continue;
            }
            int poColumn = FindBestIdentifierColumn(worksheet, row + 1, sampleRows, poKeys, styleColumn);
            return new SupplementaryItemTable(row, poColumn, styleColumn, columns);
        }
        return null;
    }

    private Dictionary<SupplementaryField, int> DetectSupplementaryValueColumns(IExcelImportWorksheet worksheet, int headerRow)
    {
        var columns = new Dictionary<SupplementaryField, int>();
        for (int column = 1; column <= SupplementaryTableScanColumns; column++)
        {
            string header = NormalizeHeader(GetItemCellValue(worksheet, headerRow, column));
            foreach (var definition in SupplementaryHeaders)
            {
                if (!columns.ContainsKey(definition.Field) && IsHeader(header, definition.Aliases))
                {
                    columns[definition.Field] = column;
                    break;
                }
            }
        }
        return columns;
    }

    private int FindBestIdentifierColumn(
        IExcelImportWorksheet worksheet,
        int dataStartRow,
        int sampleRowCount,
        IReadOnlySet<string> knownKeys,
        int excludedColumn)
    {
        int bestColumn = 0;
        int bestMatches = 0;
        for (int column = 1; column <= SupplementaryTableScanColumns && knownKeys.Count > 0; column++)
        {
            if (column == excludedColumn)
            {
                continue;
            }
            int matches = Enumerable.Range(dataStartRow, sampleRowCount)
                .Count(row => knownKeys.Contains(NormalizeItemLookupKey(GetItemCellValue(worksheet, row, column))));
            if (matches > bestMatches)
            {
                (bestColumn, bestMatches) = (column, matches);
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
        string Read(SupplementaryField field) => NormalizeExcelTextBlock(
            GetItemCellValue(worksheet, row, table.Column(field)));
        static string Prefer(string preferred, string fallback) =>
            string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

        var (fabric, styleName) = SplitCompositionAndEnglishName(Read(SupplementaryField.CompositionAndName));
        fabric = Prefer(Read(SupplementaryField.FabricComposition), fabric);
        styleName = Prefer(Read(SupplementaryField.StyleName), styleName);
        string styleNameCn = Read(SupplementaryField.StyleNameCn);
        string brand = Read(SupplementaryField.Brand);
        string hsCode = Prefer(Read(SupplementaryField.CustomsHsCode), Read(SupplementaryField.BillHsCode));

        if (fabric.Length > 0) item.FabricComposition = fabric;
        if (styleName.Length > 0) item.StyleName = styleName;
        if (styleNameCn.Length > 0) item.StyleNameCN = styleNameCn;
        if (brand.Length > 0) item.Brand = brand;
        if (hsCode.Length > 0) item.HSCode = hsCode;
        NormalizeItemDescriptionLanguages(item);
        NormalizeItemDescriptionAndBrand(item);
    }

    private static (string FabricComposition, string StyleName) SplitCompositionAndEnglishName(string value)
    {
        var boundary = AudienceDescriptionBoundary.Match(value);
        if (boundary.Success && value[..boundary.Index].Contains('%'))
        {
            return (value[..boundary.Index].Trim(), value[boundary.Index..].Trim());
        }
        return value.Contains('%') ? (value, string.Empty) : (string.Empty, value);
    }

    private static Dictionary<string, Item[]> BuildSupplementaryItemIndex(
        IEnumerable<Item> items,
        Func<Item, string> keySelector) =>
        items.GroupBy(keySelector, StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    private static HashSet<string> BuildSupplementaryKeySet(IEnumerable<string?> values) =>
        values.Select(NormalizeItemLookupKey).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static string BuildItemPairKey(string? poNumber, string? styleNo)
    {
        string poKey = NormalizeItemLookupKey(poNumber);
        string styleKey = NormalizeItemLookupKey(styleNo);
        return poKey.Length == 0 || styleKey.Length == 0 ? string.Empty : $"{poKey}\u001f{styleKey}";
    }

    private static string NormalizeItemLookupKey(string? value)
    {
        string normalized = string.Concat((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || IsCjk(character))
            .Select(char.ToUpperInvariant));
        return normalized == "0" ? string.Empty : normalized;
    }

    private enum SupplementaryField
    {
        CompositionAndName,
        FabricComposition,
        StyleName,
        StyleNameCn,
        Brand,
        CustomsHsCode,
        BillHsCode
    }

    private sealed record SupplementaryItemTable(
        int HeaderRow,
        int PoNumberColumn,
        int StyleNoColumn,
        IReadOnlyDictionary<SupplementaryField, int> Columns)
    {
        public int Column(SupplementaryField field) => Columns.GetValueOrDefault(field);
    }
}
