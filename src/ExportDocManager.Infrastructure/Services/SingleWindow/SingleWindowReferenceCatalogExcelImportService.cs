using ClosedXML.Excel;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowReferenceCatalogExcelImportService : ISingleWindowReferenceCatalogExcelImportService
    {
        private static readonly IReadOnlyDictionary<string, PageDefinition> Pages =
            new Dictionary<string, PageDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["countries"] = new PageDefinition(
                    "countries",
                    [
                        Field("code", "代码", required: true, "Code", "编码"),
                        Field("englishName", "英文名", required: true, "EnglishName", "English Name", "英文名称"),
                        Field("chineseName", "中文名", required: true, "ChineseName", "Chinese Name", "中文名称"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ]),
                ["acdCountries"] = new PageDefinition(
                    "acdCountries",
                    [
                        Field("code", "代码", required: true, "Code", "编码"),
                        Field("chineseName", "中文简称", required: true, "ChineseName", "Chinese Name", "中文名", "中文名称"),
                        Field("englishName", "英文名", required: true, "EnglishName", "English Name", "英文名称"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ]),
                ["currencies"] = new PageDefinition(
                    "currencies",
                    [
                        Field("code", "标准数字代码", required: true, "Code", "数字代码", "币制代码"),
                        Field("acdCode", "ACD海关币制码", required: false, "AcdCode", "海关币制码", "ACD币制码"),
                        Field("alphaCode", "字母代码", required: true, "AlphaCode", "ISO代码", "币制字母代码"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ]),
                ["acdTradeModes"] = new PageDefinition(
                    "acdTradeModes",
                    [
                        Field("code", "代码", required: true, "Code", "编码"),
                        Field("name", "简称", required: true, "Name", "名称"),
                        Field("description", "说明", required: false, "Description", "描述", "备注"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ]),
                ["transportModes"] = new PageDefinition(
                    "transportModes",
                    [
                        Field("value", "标准值", required: true, "Value", "运输方式", "运输方式标准值"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ]),
                ["ports"] = new PageDefinition(
                    "ports",
                    [
                        Field("value", "标准值", required: true, "Value", "港口", "港口标准值"),
                        Field("aliases", "别名", required: false, "Aliases", "AliasesText", "别称")
                    ])
            };

        public Task<SingleWindowReferenceCatalogExcelImportPreview> PreviewImportAsync(
            Stream workbookStream,
            SingleWindowReferenceCatalogExcelImportOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbookStream);
            ArgumentNullException.ThrowIfNull(options);
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(options.CatalogKey) ||
                !Pages.TryGetValue(options.CatalogKey.Trim(), out var page))
            {
                throw new ArgumentException("参考词典分类无效。", nameof(options));
            }

            using var workbook = new XLWorkbook(workbookStream);
            var sheetNames = workbook.Worksheets
                .Select(sheet => sheet.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            if (sheetNames.Count == 0)
            {
                throw new InvalidDataException("Excel 文件没有可用工作表。");
            }

            string requestedSheetName = options.SheetName?.Trim() ?? string.Empty;
            var worksheet = string.IsNullOrWhiteSpace(requestedSheetName)
                ? workbook.Worksheets.First()
                : workbook.Worksheets.FirstOrDefault(sheet =>
                    string.Equals(sheet.Name, requestedSheetName, StringComparison.OrdinalIgnoreCase));
            if (worksheet == null)
            {
                throw new ArgumentException($"找不到工作表：{requestedSheetName}");
            }

            int dataStartRowNumber = options.DataStartRowNumber > 0
                ? options.DataStartRowNumber
                : 2;
            int headerRowNumber = options.HeaderRowNumber > 0
                ? options.HeaderRowNumber
                : Math.Max(1, dataStartRowNumber - 1);
            if (dataStartRowNumber <= headerRowNumber)
            {
                throw new ArgumentException("数据起始行必须大于表头行。", nameof(options));
            }

            var columnMap = ResolveColumnMap(worksheet, headerRowNumber, page, options.ColumnMap);
            var catalog = ImportRows(page, worksheet, dataStartRowNumber, columnMap);
            int rowCount = CountRows(catalog, page.Key);
            var mappings = page.Fields
                .Select(field => new SingleWindowReferenceCatalogExcelColumnMapping(
                    field.Key,
                    field.Label,
                    columnMap.TryGetValue(field.Key, out int columnNumber) ? columnNumber : 0,
                    field.Required))
                .ToList();

            return Task.FromResult(new SingleWindowReferenceCatalogExcelImportPreview(
                page.Key,
                worksheet.Name,
                sheetNames,
                headerRowNumber,
                dataStartRowNumber,
                mappings,
                catalog,
                rowCount));
        }

        private static FieldDefinition Field(
            string key,
            string label,
            bool required,
            params string[] aliases)
        {
            return new FieldDefinition(
                key,
                label,
                required,
                [key, label, .. aliases]);
        }

        private static IReadOnlyDictionary<string, int> ResolveColumnMap(
            IXLWorksheet worksheet,
            int headerRowNumber,
            PageDefinition page,
            IReadOnlyDictionary<string, int> requestedColumnMap)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var detectedMap = DetectColumnMapFromHeader(worksheet, headerRowNumber, page);
            for (int index = 0; index < page.Fields.Count; index++)
            {
                var field = page.Fields[index];
                int columnNumber = 0;
                if (requestedColumnMap != null &&
                    requestedColumnMap.TryGetValue(field.Key, out int requestedColumnNumber) &&
                    requestedColumnNumber > 0)
                {
                    columnNumber = requestedColumnNumber;
                }
                else if (detectedMap.TryGetValue(field.Key, out int detectedColumnNumber) &&
                    detectedColumnNumber > 0)
                {
                    columnNumber = detectedColumnNumber;
                }
                else
                {
                    columnNumber = index + 1;
                }

                map[field.Key] = columnNumber;
            }

            return map;
        }

        private static IReadOnlyDictionary<string, int> DetectColumnMapFromHeader(
            IXLWorksheet worksheet,
            int headerRowNumber,
            PageDefinition page)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var normalizedFields = page.Fields
                .Select(field => new
                {
                    Field = field,
                    Headers = field.HeaderCandidates
                        .Select(NormalizeHeader)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                })
                .ToList();

            foreach (var cell in worksheet.Row(headerRowNumber).CellsUsed())
            {
                string header = NormalizeHeader(cell.GetString());
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var match = normalizedFields.FirstOrDefault(item =>
                    item.Headers.Contains(header) &&
                    !map.ContainsKey(item.Field.Key));
                if (match != null)
                {
                    map[match.Field.Key] = cell.Address.ColumnNumber;
                }
            }

            return map;
        }

        private static string NormalizeHeader(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static SingleWindowReferenceCatalogModel ImportRows(
            PageDefinition page,
            IXLWorksheet worksheet,
            int dataStartRowNumber,
            IReadOnlyDictionary<string, int> columnMap)
        {
            int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            return page.Key switch
            {
                "countries" => new SingleWindowReferenceCatalogModel
                {
                    Countries = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferenceCountryEntry
                        {
                            Code = ReadCell(row, columnMap, "code"),
                            EnglishName = ReadCell(row, columnMap, "englishName"),
                            ChineseName = ReadCell(row, columnMap, "chineseName"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Code, item.EnglishName, item.ChineseName, item.Aliases))
                },
                "acdCountries" => new SingleWindowReferenceCatalogModel
                {
                    AcdCountries = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferenceAcdCountryEntry
                        {
                            Code = ReadCell(row, columnMap, "code"),
                            ChineseName = ReadCell(row, columnMap, "chineseName"),
                            EnglishName = ReadCell(row, columnMap, "englishName"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Code, item.ChineseName, item.EnglishName, item.Aliases))
                },
                "currencies" => new SingleWindowReferenceCatalogModel
                {
                    Currencies = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferenceCurrencyEntry
                        {
                            Code = ReadCell(row, columnMap, "code"),
                            AcdCode = ReadCell(row, columnMap, "acdCode"),
                            AlphaCode = ReadCell(row, columnMap, "alphaCode"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Code, item.AcdCode, item.AlphaCode, item.Aliases))
                },
                "acdTradeModes" => new SingleWindowReferenceCatalogModel
                {
                    AcdTradeModes = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferenceAcdTradeModeEntry
                        {
                            Code = ReadCell(row, columnMap, "code"),
                            Name = ReadCell(row, columnMap, "name"),
                            Description = ReadCell(row, columnMap, "description"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Code, item.Name, item.Description, item.Aliases))
                },
                "transportModes" => new SingleWindowReferenceCatalogModel
                {
                    TransportModes = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferenceTransportModeEntry
                        {
                            Value = ReadCell(row, columnMap, "value"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Value, item.Aliases))
                },
                "ports" => new SingleWindowReferenceCatalogModel
                {
                    Ports = ImportRows(worksheet, dataStartRowNumber, lastRowNumber, columnMap, row =>
                        new SingleWindowReferencePortEntry
                        {
                            Value = ReadCell(row, columnMap, "value"),
                            Aliases = ParseAliases(ReadCell(row, columnMap, "aliases"))
                        },
                        item => HasAnyValue(item.Value, item.Aliases))
                },
                _ => new SingleWindowReferenceCatalogModel()
            };
        }

        private static IReadOnlyList<T> ImportRows<T>(
            IXLWorksheet worksheet,
            int dataStartRowNumber,
            int lastRowNumber,
            IReadOnlyDictionary<string, int> columnMap,
            Func<IXLRow, T> factory,
            Func<T, bool> hasAnyValue)
        {
            var rows = new List<T>();
            for (int rowNumber = dataStartRowNumber; rowNumber <= lastRowNumber; rowNumber++)
            {
                var row = factory(worksheet.Row(rowNumber));
                if (hasAnyValue(row))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static string ReadCell(IXLRow row, IReadOnlyDictionary<string, int> columnMap, string fieldKey)
        {
            if (row == null ||
                columnMap == null ||
                !columnMap.TryGetValue(fieldKey, out int columnNumber) ||
                columnNumber <= 0)
            {
                return string.Empty;
            }

            return row.Cell(columnNumber).GetString()?.Trim() ?? string.Empty;
        }

        private static IReadOnlyList<string> ParseAliases(string value)
        {
            return (value ?? string.Empty)
                .Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool HasAnyValue(params object[] values)
        {
            foreach (var value in values ?? [])
            {
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return true;
                }

                if (value is IEnumerable<string> aliases && aliases.Any(alias => !string.IsNullOrWhiteSpace(alias)))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountRows(SingleWindowReferenceCatalogModel catalog, string key)
        {
            return key switch
            {
                "countries" => catalog.Countries?.Count ?? 0,
                "acdCountries" => catalog.AcdCountries?.Count ?? 0,
                "currencies" => catalog.Currencies?.Count ?? 0,
                "acdTradeModes" => catalog.AcdTradeModes?.Count ?? 0,
                "transportModes" => catalog.TransportModes?.Count ?? 0,
                "ports" => catalog.Ports?.Count ?? 0,
                _ => 0
            };
        }

        private sealed record PageDefinition(
            string Key,
            IReadOnlyList<FieldDefinition> Fields);

        private sealed record FieldDefinition(
            string Key,
            string Label,
            bool Required,
            IReadOnlyList<string> HeaderCandidates);
    }
}
