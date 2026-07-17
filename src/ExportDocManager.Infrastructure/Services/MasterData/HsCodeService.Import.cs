using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        public Task ImportAsync(string filePath)
        {
            return Task.Run(() => ImportCoreAsync(filePath));
        }

        private async Task ImportCoreAsync(string filePath)
        {
            try
            {
                using var workbook = new XLWorkbook(filePath);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed()?.RowsUsed().ToList() ?? [];
                if (rows.Count == 0)
                {
                    return;
                }

                var layout = ResolveImportLayout(rows[0]);
                var dataRows = layout.HeaderFound ? rows.Skip(1) : rows;

                await using var context = await CreateDbContextAsync();
                var existingMap = await BuildExistingHsCodeMapAsync(context);
                var toAdd = new List<HsCode>();
                var toUpdate = new List<HsCode>();

                foreach (var row in dataRows)
                {
                    try
                    {
                        var entity = BuildImportEntity(row, layout);
                        if (entity == null)
                        {
                            continue;
                        }

                        if (existingMap.TryGetValue(entity.NormalizedCode, out int existingId))
                        {
                            entity.Id = existingId;
                            toUpdate.Add(entity);
                        }
                        else
                        {
                            toAdd.Add(entity);
                        }
                    }
                    catch (Exception rowEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing row {row.RowNumber()}: {rowEx.Message}");
                    }
                }

                if (toAdd.Count > 0)
                {
                    await context.HsCodes.AddRangeAsync(toAdd);
                }

                if (toUpdate.Count > 0)
                {
                    context.HsCodes.UpdateRange(toUpdate);
                }

                if (toAdd.Count > 0 || toUpdate.Count > 0)
                {
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import Failed: {ex.Message}");
                throw;
            }
        }

        private static ImportLayout ResolveImportLayout(IXLRangeRow headerRow)
        {
            int colCode = 1;
            int colName = 2;
            int colRebate = 4;
            int colSupervision = 5;
            int colInspection = 6;
            int colElements = 7;
            int colDesc = 8;
            int colUnit1 = -1;
            int colUnit2 = -1;
            bool headerFound = false;

            foreach (var cell in headerRow.CellsUsed())
            {
                var value = cell.GetValue<string>().Trim().ToUpperInvariant();
                int columnIndex = cell.Address.ColumnNumber;

                if (value.Contains("HS") || value.Contains("编码") || value.Contains("CODE"))
                {
                    colCode = columnIndex;
                    headerFound = true;
                }
                else if (value.Contains("名称") || value.Contains("品名") || value.Contains("NAME"))
                {
                    colName = columnIndex;
                }
                else if (value.Contains("退税") || value.Contains("REBATE"))
                {
                    colRebate = columnIndex;
                }
                else if (value.Contains("监管") || value.Contains("SUPERVISION"))
                {
                    colSupervision = columnIndex;
                }
                else if (value.Contains("检疫") || value.Contains("INSPECTION"))
                {
                    colInspection = columnIndex;
                }
                else if (value.Contains("要素") || value.Contains("ELEMENT"))
                {
                    colElements = columnIndex;
                }
                else if (value.Contains("描述") || value.Contains("英文") || value.Contains("DESC") || value.Contains("EN"))
                {
                    colDesc = columnIndex;
                }
                else if (value.Contains("单位") || value.Contains("UNIT"))
                {
                    if (value.Contains("1") || value.Contains("一") || value.Contains("FIRST"))
                    {
                        colUnit1 = columnIndex;
                    }
                    else if (value.Contains("2") || value.Contains("二") || value.Contains("SECOND"))
                    {
                        colUnit2 = columnIndex;
                    }
                    else if (colUnit1 < 0)
                    {
                        colUnit1 = columnIndex;
                    }
                }
            }

            if (colUnit1 < 0 && colUnit2 < 0)
            {
                colUnit1 = 3;
            }

            return new ImportLayout(
                HeaderFound: headerFound,
                CodeColumn: colCode,
                NameColumn: colName,
                Unit1Column: colUnit1,
                Unit2Column: colUnit2,
                RebateColumn: colRebate,
                SupervisionColumn: colSupervision,
                InspectionColumn: colInspection,
                ElementsColumn: colElements,
                DescriptionColumn: colDesc);
        }

        private static HsCode BuildImportEntity(IXLRangeRow row, ImportLayout layout)
        {
            string code = HsCodeTextHelper.NormalizeCode(row.Cell(layout.CodeColumn).GetValue<string>());
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return new HsCode
            {
                Code = code,
                Name = row.Cell(layout.NameColumn).GetValue<string>()?.Trim(),
                Unit = MergeImportUnits(row, layout),
                RebateRate = row.Cell(layout.RebateColumn).GetValue<string>()?.Trim(),
                SupervisionConditions = row.Cell(layout.SupervisionColumn).GetValue<string>()?.Trim(),
                InspectionCategory = row.Cell(layout.InspectionColumn).GetValue<string>()?.Trim(),
                Elements = row.Cell(layout.ElementsColumn).GetValue<string>()?.Trim(),
                Description = row.Cell(layout.DescriptionColumn).GetValue<string>()?.Trim(),
                UpdateTime = DateTime.Now
            };
        }

        private static string MergeImportUnits(IXLRangeRow row, ImportLayout layout)
        {
            string unit1 = layout.Unit1Column > 0
                ? ConvertImportedUnit(row.Cell(layout.Unit1Column).GetValue<string>())
                : null;
            string unit2 = layout.Unit2Column > 0
                ? ConvertImportedUnit(row.Cell(layout.Unit2Column).GetValue<string>())
                : null;

            if (string.IsNullOrWhiteSpace(unit1))
            {
                return unit2;
            }

            if (string.IsNullOrWhiteSpace(unit2) ||
                string.Equals(unit1, unit2, StringComparison.OrdinalIgnoreCase))
            {
                return unit1;
            }

            return $"{unit1}/{unit2}";
        }

        private static string ConvertImportedUnit(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string value = raw.Trim();
            var parts = value.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                string nonDigitPart = parts.FirstOrDefault(part => !part.All(char.IsDigit));
                if (!string.IsNullOrWhiteSpace(nonDigitPart))
                {
                    return nonDigitPart;
                }
            }

            if (CustomsUnitMap.TryGetValue(value, out string mappedValue))
            {
                return mappedValue;
            }

            if (char.IsDigit(value[0]))
            {
                string trimmed = new string(value.SkipWhile(char.IsDigit).ToArray()).Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }
            }

            return value;
        }

        private static async Task<Dictionary<string, int>> BuildExistingHsCodeMapAsync(AppDbContext context)
        {
            return (await context.HsCodes
                .Select(h => new { h.Code, h.NormalizedCode, h.Id })
                .ToListAsync())
                .GroupBy(
                    entry => string.IsNullOrWhiteSpace(entry.NormalizedCode)
                        ? HsCodeTextHelper.NormalizeCode(entry.Code)
                        : entry.NormalizedCode,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(entry => entry.Id).First().Id,
                    StringComparer.OrdinalIgnoreCase);
        }

        private sealed record ImportLayout(
            bool HeaderFound,
            int CodeColumn,
            int NameColumn,
            int Unit1Column,
            int Unit2Column,
            int RebateColumn,
            int SupervisionColumn,
            int InspectionColumn,
            int ElementsColumn,
            int DescriptionColumn);
    }
}
