using System.Text;
using ClosedXML.Excel;

namespace ExportDocManager.Services.Data
{
    public static class TabularImportReader
    {
        public static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
            Stream input, string fileName, int maximumRows, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
            {
                ".csv" => await ReadCsvAsync(input, maximumRows, cancellationToken),
                ".xlsx" or ".xlsm" => ReadWorkbook(input, maximumRows),
                _ => throw new InvalidDataException("只支持 .csv、.xlsx 或 .xlsm 文件。")
            };
        }

        private static IReadOnlyList<IReadOnlyList<string>> ReadWorkbook(Stream input, int maximumRows)
        {
            using var workbook = new XLWorkbook(input);
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Excel 工作簿没有可读取的工作表。");
            var range = sheet.RangeUsed() ?? throw new InvalidDataException("Excel 工作表为空。");
            return range.Rows().Take(maximumRows + 1)
                .Select(row => (IReadOnlyList<string>)row.Cells(1, range.ColumnCount())
                    .Select(cell => cell.GetFormattedString().Trim()).ToArray()).ToArray();
        }

        private static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadCsvAsync(
            Stream input, int maximumRows, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(input, new UTF8Encoding(false), true, leaveOpen: true);
            var rows = new List<IReadOnlyList<string>>();
            while (!reader.EndOfStream && rows.Count <= maximumRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(ParseCsvLine(await reader.ReadLineAsync(cancellationToken) ?? string.Empty));
            }
            return rows;
        }

        private static IReadOnlyList<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                if (current == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                    else quoted = !quoted;
                }
                else if (current == ',' && !quoted) { values.Add(value.ToString().Trim()); value.Clear(); }
                else value.Append(current);
            }
            values.Add(value.ToString().Trim());
            return values;
        }
    }
}
