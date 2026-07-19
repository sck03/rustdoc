using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        private static readonly IReadOnlyDictionary<string, string[]> ImportHeaderAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = ["HS编码", "HSCODE", "海关编码", "商品编码", "商品税号", "税则号列", "税则号", "税号"],
                ["Name"] = ["商品名称", "货品名称", "中文品名", "税目名称", "商品品名", "名称", "品名", "NAME"],
                ["Unit1"] = ["法定第一单位", "第一法定单位", "法一单位", "计量单位1", "第一单位", "UNIT1"],
                ["Unit2"] = ["法定第二单位", "第二法定单位", "法二单位", "计量单位2", "第二单位", "UNIT2"],
                ["RebateRate"] = ["出口退税率", "出口商品退税率", "退税率", "REBATERATE", "REBATE"],
                ["SupervisionConditions"] = ["海关监管条件", "监管证件代码", "监管条件", "SUPERVISIONCONDITIONS", "SUPERVISION"],
                ["InspectionCategory"] = ["检验检疫类别", "检疫类别", "检验检疫", "CIQ类别", "INSPECTIONCATEGORY", "INSPECTION"],
                ["Elements"] = ["规范申报要素", "申报要素内容", "申报要素", "ELEMENTS", "ELEMENT"],
                ["Description"] = ["英文名称", "英文品名", "商品描述", "英文描述", "DESCRIPTION", "DESC"]
            };

        public async Task ImportAsync(string filePath)
        {
            var preview = await PreviewImportAsync(filePath);
            await CommitImportAsync(preview);
        }

        public async Task<HsCodeImportPreview> PreviewImportAsync(
            string filePath,
            HsCodeImportMode mode = HsCodeImportMode.Incremental,
            string sourceName = null,
            int? effectiveYear = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("HS编码导入文件不存在。", filePath);
            }

            using var workbook = new XLWorkbook(filePath);
            var detected = DetectBestImportLayout(workbook);
            if (detected == null || detected.Layout.CodeColumn <= 0)
            {
                throw new InvalidDataException("未能识别HS编码列，请确认文件包含商品编码或HS编码表头。商业发票Excel不会使用此导入入口。");
            }

            var parsedRows = ParseImportRows(detected, cancellationToken);
            await using var context = await CreateDbContextAsync(cancellationToken);
            var existing = await context.HsCodes.AsNoTracking().ToListAsync(cancellationToken);
            var existingMap = existing
                .Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode))
                .GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id).First(), StringComparer.OrdinalIgnoreCase);

            var items = BuildPreviewItems(parsedRows, existingMap, detected, mode);
            string normalizedSource = string.IsNullOrWhiteSpace(sourceName)
                ? Path.GetFileNameWithoutExtension(filePath)
                : sourceName.Trim();
            var warnings = new List<string>();
            if (detected.Confidence < 75)
            {
                warnings.Add("字段识别置信度偏低，请在提交前检查预览中的列映射和样例数据。");
            }
            if (mode == HsCodeImportMode.CompleteSnapshot)
            {
                warnings.Add("完整年度库模式会把本地存在但本文件缺少的编码标记为疑似作废，不会物理删除历史编码，也不会改写商业发票历史数据。");
            }
            if (items.Any(item => item.ChangeType == "Conflict"))
            {
                warnings.Add("文件中存在同编码不同内容的冲突记录；冲突项不会写入数据库。");
            }

            return new HsCodeImportPreview(
                Path.GetFileName(filePath),
                mode,
                normalizedSource,
                effectiveYear,
                detected.Worksheet.Position,
                detected.Worksheet.Name,
                detected.HeaderRowNumber,
                detected.Confidence,
                detected.Layout.Mappings,
                items,
                items.Count(item => item.ChangeType == "Add"),
                items.Count(item => item.ChangeType == "Update"),
                items.Count(item => item.ChangeType == "Unchanged"),
                items.Count(item => item.ChangeType == "SuspectedObsolete"),
                items.Count(item => item.ChangeType == "Conflict"),
                items.Count(item => item.ChangeType == "Invalid"),
                warnings);
        }

        public async Task<HsCodeImportCommitResult> CommitImportAsync(
            HsCodeImportPreview preview,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            await using var context = await CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var existing = await context.HsCodes.ToListAsync(cancellationToken);
            var existingMap = existing
                .Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode))
                .GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id).First(), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            int updated = 0;
            int unchanged = 0;
            int obsolete = 0;
            int skipped = 0;
            DateTime now = DateTime.Now;

            foreach (var previewItem in preview.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imported = previewItem.Item;
                string code = HsCodeTextHelper.NormalizeCode(imported?.Code);
                if (string.IsNullOrWhiteSpace(code) || previewItem.ChangeType is "Invalid" or "Conflict")
                {
                    skipped++;
                    continue;
                }

                if (previewItem.ChangeType == "SuspectedObsolete")
                {
                    if (existingMap.TryGetValue(code, out var obsoleteItem))
                    {
                        obsoleteItem.Status = "SuspectedObsolete";
                        obsoleteItem.ReplacedByCodes = string.Join(",", previewItem.ReplacementCandidates ?? []);
                        obsoleteItem.UpdateTime = now;
                        obsolete++;
                    }
                    continue;
                }

                if (!existingMap.TryGetValue(code, out var target))
                {
                    imported.Id = 0;
                    imported.Code = code;
                    imported.Status = "Active";
                    imported.SourceName = preview.SourceName;
                    imported.EffectiveYear = preview.EffectiveYear;
                    imported.LastVerifiedAt = now;
                    imported.UpdateTime = now;
                    await context.HsCodes.AddAsync(imported, cancellationToken);
                    existingMap[code] = imported;
                    added++;
                    continue;
                }

                if (previewItem.ChangeType == "Unchanged")
                {
                    target.Status = "Active";
                    target.SourceName = Coalesce(imported.SourceName, preview.SourceName, target.SourceName);
                    target.EffectiveYear = preview.EffectiveYear ?? target.EffectiveYear;
                    target.LastVerifiedAt = now;
                    unchanged++;
                    continue;
                }

                MergeNonEmptyHsCodeValues(imported, target);
                target.Status = "Active";
                target.SourceName = Coalesce(imported.SourceName, preview.SourceName, target.SourceName);
                target.EffectiveYear = preview.EffectiveYear ?? target.EffectiveYear;
                target.LastVerifiedAt = now;
                target.ReplacedByCodes = null;
                target.UpdateTime = now;
                updated++;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HsCodeImportCommitResult(
                added,
                updated,
                unchanged,
                obsolete,
                skipped,
                $"HS编码智能导入完成：新增 {added}，更新 {updated}，不变 {unchanged}，疑似作废 {obsolete}，跳过 {skipped}。");
        }

        private static DetectedImportLayout DetectBestImportLayout(XLWorkbook workbook)
        {
            DetectedImportLayout best = null;
            foreach (var worksheet in workbook.Worksheets)
            {
                var used = worksheet.RangeUsed();
                if (used == null)
                {
                    continue;
                }

                int firstRow = used.RangeAddress.FirstAddress.RowNumber;
                int lastScanRow = Math.Min(used.RangeAddress.LastAddress.RowNumber, firstRow + 29);
                for (int rowNumber = firstRow; rowNumber <= lastScanRow; rowNumber++)
                {
                    var layout = ResolveImportLayout(worksheet.Row(rowNumber));
                    if (layout.CodeColumn <= 0)
                    {
                        continue;
                    }

                    int contentScore = ScoreCodeColumnContent(worksheet, rowNumber, layout.CodeColumn);
                    int confidence = Math.Min(100, layout.Confidence + contentScore);
                    if (best == null || confidence > best.Confidence)
                    {
                        best = new DetectedImportLayout(worksheet, rowNumber, layout, confidence);
                    }
                }
            }

            return best;
        }

        private static ImportLayout ResolveImportLayout(IXLRow headerRow)
        {
            var matches = new Dictionary<string, (int Column, string Header, int Score)>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in headerRow.CellsUsed())
            {
                string rawHeader = cell.GetFormattedString().Trim();
                string normalizedHeader = NormalizeImportHeader(rawHeader);
                if (string.IsNullOrWhiteSpace(normalizedHeader))
                {
                    continue;
                }

                foreach (var field in ImportHeaderAliases)
                {
                    int score = ScoreHeader(normalizedHeader, field.Value);
                    if (score <= 0 || matches.TryGetValue(field.Key, out var current) && current.Score >= score)
                    {
                        continue;
                    }
                    matches[field.Key] = (cell.Address.ColumnNumber, rawHeader, score);
                }
            }

            int Column(string field) => matches.TryGetValue(field, out var match) ? match.Column : -1;
            var mappings = matches
                .Select(pair => new HsCodeImportColumnMapping(pair.Key, pair.Value.Header, pair.Value.Column, pair.Value.Score))
                .OrderBy(item => item.ColumnNumber)
                .ToList();
            int confidence = matches.TryGetValue("Code", out var code) ? code.Score / 2 : 0;
            confidence += matches.ContainsKey("Name") ? 20 : 0;
            confidence += Math.Min(30, Math.Max(0, matches.Count - 2) * 5);
            return new ImportLayout(
                Column("Code"), Column("Name"), Column("Unit1"), Column("Unit2"),
                Column("RebateRate"), Column("SupervisionConditions"), Column("InspectionCategory"),
                Column("Elements"), Column("Description"), Math.Min(90, confidence), mappings);
        }

        private static int ScoreHeader(string header, IEnumerable<string> aliases)
        {
            int best = 0;
            foreach (string alias in aliases.Select(NormalizeImportHeader))
            {
                if (string.Equals(header, alias, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 100);
                }
                else if (header.Contains(alias, StringComparison.OrdinalIgnoreCase) || alias.Contains(header, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 70);
                }
            }
            return best;
        }

        private static string NormalizeImportHeader(string value)
        {
            return new string((value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Where(character => char.IsLetterOrDigit(character) || character >= 0x4e00 && character <= 0x9fff)
                .ToArray());
        }

        private static int ScoreCodeColumnContent(IXLWorksheet worksheet, int headerRow, int codeColumn)
        {
            int valid = 0;
            int checkedCount = 0;
            int lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? headerRow, headerRow + 20);
            for (int row = headerRow + 1; row <= lastRow; row++)
            {
                string raw = ReadImportCell(worksheet.Cell(row, codeColumn));
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }
                checkedCount++;
                string code = HsCodeTextHelper.NormalizeCode(raw);
                if (code.Length is >= 6 and <= 13 && code.All(char.IsDigit))
                {
                    valid++;
                }
            }
            if (checkedCount == 0)
            {
                return 0;
            }
            return (int)Math.Round(valid * 20d / checkedCount);
        }

        private static List<ParsedImportRow> ParseImportRows(DetectedImportLayout detected, CancellationToken cancellationToken)
        {
            var result = new List<ParsedImportRow>();
            int lastRow = detected.Worksheet.LastRowUsed()?.RowNumber() ?? detected.HeaderRowNumber;
            for (int rowNumber = detected.HeaderRowNumber + 1; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = detected.Worksheet.Row(rowNumber);
                var entity = BuildImportEntity(row, detected.Layout);
                if (entity == null && row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetFormattedString())))
                {
                    result.Add(new ParsedImportRow(rowNumber, null, "无法识别有效HS编码。"));
                }
                else if (entity != null)
                {
                    result.Add(new ParsedImportRow(rowNumber, entity, null));
                }
            }
            return result;
        }

        private static IReadOnlyList<HsCodeImportPreviewItem> BuildPreviewItems(
            IReadOnlyList<ParsedImportRow> parsedRows,
            IReadOnlyDictionary<string, HsCode> existingMap,
            DetectedImportLayout detected,
            HsCodeImportMode mode)
        {
            var items = new List<HsCodeImportPreviewItem>();
            var validRows = parsedRows.Where(row => row.Item != null).ToList();
            var duplicateCodes = validRows
                .GroupBy(row => row.Item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(row => BuildComparableValue(row.Item)).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parsed in parsedRows)
            {
                if (parsed.Item == null)
                {
                    items.Add(CreatePreviewItem("Invalid", parsed.RowNumber, new HsCode(), [], [], parsed.Error, detected));
                    continue;
                }
                string code = parsed.Item.NormalizedCode;
                if (duplicateCodes.Contains(code))
                {
                    items.Add(CreatePreviewItem("Conflict", parsed.RowNumber, parsed.Item, [], [], "同一文件中该编码存在不同内容。", detected));
                    continue;
                }
                if (!seenCodes.Add(code))
                {
                    items.Add(CreatePreviewItem("Unchanged", parsed.RowNumber, parsed.Item, [], [], "文件内重复的相同编码，提交时只处理一次。", detected));
                    continue;
                }
                if (!existingMap.TryGetValue(code, out var existing))
                {
                    items.Add(CreatePreviewItem("Add", parsed.RowNumber, parsed.Item, [], [], "新增编码。", detected));
                    continue;
                }
                var changedFields = FindChangedFields(existing, parsed.Item);
                items.Add(CreatePreviewItem(
                    changedFields.Count == 0 ? "Unchanged" : "Update",
                    parsed.RowNumber,
                    parsed.Item,
                    changedFields,
                    [],
                    changedFields.Count == 0 ? "内容无变化。" : $"将更新：{string.Join("、", changedFields)}。",
                    detected));
            }

            if (mode == HsCodeImportMode.CompleteSnapshot)
            {
                var importedCodes = validRows.Select(row => row.Item.NormalizedCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var importedItems = validRows.Select(row => row.Item).ToList();
                foreach (var existing in existingMap.Values.Where(item => !importedCodes.Contains(item.NormalizedCode)))
                {
                    var replacements = FindReplacementCandidates(existing, importedItems);
                    items.Add(CreatePreviewItem(
                        "SuspectedObsolete", 0, CloneHsCode(existing), [], replacements,
                        replacements.Count == 0 ? "完整库中未出现，标记为疑似作废。" : "完整库中未出现，已生成替代候选。",
                        detected));
                }
            }

            return items;
        }

        private static HsCodeImportPreviewItem CreatePreviewItem(
            string type, int rowNumber, HsCode item, IReadOnlyList<string> fields,
            IReadOnlyList<string> replacements, string message, DetectedImportLayout detected)
        {
            return new HsCodeImportPreviewItem(
                type, detected.Worksheet.Position, detected.Worksheet.Name, rowNumber,
                item, fields, replacements, message);
        }

        private static HsCode BuildImportEntity(IXLRow row, ImportLayout layout)
        {
            string code = HsCodeTextHelper.NormalizeCode(ReadImportCell(row.Cell(layout.CodeColumn)));
            if (string.IsNullOrWhiteSpace(code) || code.Length < 6 || code.Length > 13 || !code.All(char.IsDigit))
            {
                return null;
            }
            return new HsCode
            {
                Code = code,
                Name = ReadOptionalCell(row, layout.NameColumn),
                Unit = MergeImportUnits(row, layout),
                RebateRate = ReadOptionalCell(row, layout.RebateColumn),
                SupervisionConditions = ReadOptionalCell(row, layout.SupervisionColumn),
                InspectionCategory = ReadOptionalCell(row, layout.InspectionColumn),
                Elements = ReadOptionalCell(row, layout.ElementsColumn),
                Description = ReadOptionalCell(row, layout.DescriptionColumn),
                Status = "Active",
                UpdateTime = DateTime.Now
            };
        }

        private static string ReadOptionalCell(IXLRow row, int column) => column > 0 ? ReadImportCell(row.Cell(column)) : null;

        private static string ReadImportCell(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty())
            {
                return null;
            }
            string formatted = cell.GetFormattedString()?.Trim();
            if (!string.IsNullOrWhiteSpace(formatted) && !formatted.Contains('E', StringComparison.OrdinalIgnoreCase))
            {
                return formatted;
            }
            if (cell.TryGetValue<decimal>(out var number))
            {
                return number.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
            }
            return formatted;
        }

        private static string MergeImportUnits(IXLRow row, ImportLayout layout)
        {
            string unit1 = layout.Unit1Column > 0 ? ConvertImportedUnit(ReadImportCell(row.Cell(layout.Unit1Column))) : null;
            string unit2 = layout.Unit2Column > 0 ? ConvertImportedUnit(ReadImportCell(row.Cell(layout.Unit2Column))) : null;
            if (string.IsNullOrWhiteSpace(unit1)) return unit2;
            if (string.IsNullOrWhiteSpace(unit2) || string.Equals(unit1, unit2, StringComparison.OrdinalIgnoreCase)) return unit1;
            return $"{unit1}/{unit2}";
        }

        private static string ConvertImportedUnit(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string value = raw.Trim();
            var parts = value.Split([' ', '-', '—'], StringSplitOptions.RemoveEmptyEntries);
            string nonDigitPart = parts.FirstOrDefault(part => !part.All(char.IsDigit));
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(nonDigitPart)) return nonDigitPart;
            if (CustomsUnitMap.TryGetValue(value, out string mappedValue)) return mappedValue;
            if (char.IsDigit(value[0]))
            {
                string trimmed = new string(value.SkipWhile(char.IsDigit).ToArray()).Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
            }
            return value;
        }

        private static IReadOnlyList<string> FindChangedFields(HsCode existing, HsCode imported)
        {
            var fields = new List<string>();
            Compare("商品名称", existing.Name, imported.Name);
            Compare("法定单位", existing.Unit, imported.Unit);
            Compare("退税率", existing.RebateRate, imported.RebateRate);
            Compare("监管条件", existing.SupervisionConditions, imported.SupervisionConditions);
            Compare("检验检疫类别", existing.InspectionCategory, imported.InspectionCategory);
            Compare("申报要素", existing.Elements, imported.Elements);
            Compare("描述", existing.Description, imported.Description);
            return fields;

            void Compare(string name, string oldValue, string newValue)
            {
                if (!string.IsNullOrWhiteSpace(newValue) && !string.Equals(oldValue?.Trim(), newValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    fields.Add(name);
                }
            }
        }

        private static void MergeNonEmptyHsCodeValues(HsCode source, HsCode target)
        {
            target.Name = Coalesce(source.Name, target.Name, string.Empty);
            target.Unit = Coalesce(source.Unit, target.Unit);
            target.RebateRate = Coalesce(source.RebateRate, target.RebateRate);
            target.SupervisionConditions = Coalesce(source.SupervisionConditions, target.SupervisionConditions);
            target.InspectionCategory = Coalesce(source.InspectionCategory, target.InspectionCategory);
            target.Elements = Coalesce(source.Elements, target.Elements);
            target.Description = Coalesce(source.Description, target.Description);
        }

        private static string Coalesce(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private static IReadOnlyList<string> FindReplacementCandidates(HsCode obsolete, IReadOnlyList<HsCode> imported)
        {
            string oldCode = obsolete.NormalizedCode;
            return imported
                .Where(item => !string.Equals(item.NormalizedCode, oldCode, StringComparison.OrdinalIgnoreCase))
                .Select(item => new { item.NormalizedCode, Score = ScoreReplacement(obsolete, item) })
                .Where(item => item.Score >= 45)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.NormalizedCode, StringComparer.Ordinal)
                .Take(3)
                .Select(item => item.NormalizedCode)
                .ToList();
        }

        private static int ScoreReplacement(HsCode oldItem, HsCode candidate)
        {
            int prefix = CommonPrefixLength(oldItem.NormalizedCode, candidate.NormalizedCode);
            int score = prefix >= 8 ? 65 : prefix >= 6 ? 45 : prefix >= 4 ? 20 : 0;
            if (!string.IsNullOrWhiteSpace(oldItem.Name) && !string.IsNullOrWhiteSpace(candidate.Name))
            {
                var oldTokens = BuildTextTokens(oldItem.Name);
                var newTokens = BuildTextTokens(candidate.Name);
                if (oldTokens.Count > 0)
                {
                    score += (int)Math.Round(oldTokens.Intersect(newTokens).Count() * 30d / oldTokens.Count);
                }
            }
            if (!string.IsNullOrWhiteSpace(oldItem.Unit) && string.Equals(oldItem.Unit, candidate.Unit, StringComparison.OrdinalIgnoreCase)) score += 5;
            return Math.Min(score, 100);
        }

        private static int CommonPrefixLength(string left, string right)
        {
            int length = 0;
            while (length < Math.Min(left?.Length ?? 0, right?.Length ?? 0) && left[length] == right[length]) length++;
            return length;
        }

        private static HashSet<string> BuildTextTokens(string value)
        {
            return (value ?? string.Empty)
                .ToUpperInvariant()
                .Split([' ', ',', '，', ';', '；', '/', '、', '-', '（', '）', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildComparableValue(HsCode item) => string.Join("|",
            item.Name?.Trim(), item.Unit?.Trim(), item.RebateRate?.Trim(), item.SupervisionConditions?.Trim(),
            item.InspectionCategory?.Trim(), item.Elements?.Trim(), item.Description?.Trim());

        private static HsCode CloneHsCode(HsCode item) => new()
        {
            Id = item.Id, Code = item.Code, Name = item.Name, Unit = item.Unit, RebateRate = item.RebateRate,
            SupervisionConditions = item.SupervisionConditions, InspectionCategory = item.InspectionCategory,
            Elements = item.Elements, Description = item.Description, Status = item.Status,
            SourceName = item.SourceName, EffectiveYear = item.EffectiveYear, LastVerifiedAt = item.LastVerifiedAt,
            ReplacedByCodes = item.ReplacedByCodes, UpdateTime = item.UpdateTime
        };

        private sealed record ParsedImportRow(int RowNumber, HsCode Item, string Error);
        private sealed record DetectedImportLayout(IXLWorksheet Worksheet, int HeaderRowNumber, ImportLayout Layout, int Confidence);
        private sealed record ImportLayout(
            int CodeColumn, int NameColumn, int Unit1Column, int Unit2Column, int RebateColumn,
            int SupervisionColumn, int InspectionColumn, int ElementsColumn, int DescriptionColumn,
            int Confidence, IReadOnlyList<HsCodeImportColumnMapping> Mappings);
    }
}
