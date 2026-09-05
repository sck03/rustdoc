using System.Text;
using ClosedXML.Excel;

namespace ExportDocManager.Services.Data;

/// <summary>
/// Reads the small tabular files used by the CRM and supplier import flows.
/// The reader is deliberately bounded: it consumes one extra record so an
/// oversized file is rejected instead of silently returning a partial import.
/// </summary>
public static class TabularImportReader
{
    private const int MaximumColumns = 128;
    private const int MaximumFieldCharacters = 1_000_000;

    public static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(
        Stream input,
        string fileName,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));

        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".csv" => await ReadCsvAsync(input, maximumRows, cancellationToken),
            ".xlsx" or ".xlsm" => ReadWorkbook(input, maximumRows),
            _ => throw new InvalidDataException("只支持 .csv、.xlsx 或 .xlsm 文件。")
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadWorkbook(Stream input, int maximumRows)
    {
        ExcelWorkbookResourcePolicy.ValidateOpenXmlPackage(input);
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheets.FirstOrDefault()
                    ?? throw new InvalidDataException("Excel 工作簿没有可读取的工作表。");
        var range = sheet.RangeUsed()
                    ?? throw new InvalidDataException("Excel 工作表为空。");
        var rows = range.Rows()
            .Take(checked(maximumRows + 2))
            .Select(row => (IReadOnlyList<string>)row.Cells(1, range.ColumnCount())
                .Select(cell => cell.GetFormattedString().Trim())
                .ToArray())
            .ToArray();
        EnsureRowLimit(rows.Length, maximumRows);
        return rows;
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadCsvAsync(
        Stream input,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            input,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: true);
        var rows = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var value = new StringBuilder();
        bool quoted = false;
        bool fieldStarted = false;
        bool quoteClosed = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                if (quoted)
                {
                    if (current != '"')
                    {
                        EnsureFieldLength(value.Length + 1);
                        value.Append(current);
                        continue;
                    }

                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        EnsureFieldLength(value.Length + 1);
                        value.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        quoteClosed = true;
                    }

                    continue;
                }

                if (current == '"')
                {
                    if (fieldStarted || value.Length > 0)
                    {
                        throw new InvalidDataException("CSV 文件包含未按 RFC 4180 转义的引号。");
                    }

                    quoted = true;
                    fieldStarted = true;
                    quoteClosed = false;
                }
                else if (current == ',')
                {
                    EnsureColumnLimit(fields.Count + 1);
                    fields.Add(value.ToString().Trim());
                    value.Clear();
                    fieldStarted = false;
                    quoteClosed = false;
                }
                else
                {
                    if (quoteClosed)
                    {
                        throw new InvalidDataException("CSV 文件的引号后只能跟逗号或记录结束符。");
                    }
                    EnsureFieldLength(value.Length + 1);
                    value.Append(current);
                    fieldStarted = true;
                }
            }

            if (quoted)
            {
                // ReadLineAsync removes the physical newline; RFC 4180 keeps
                // it inside a quoted field and permits the record to continue.
                EnsureFieldLength(value.Length + 1);
                value.Append('\n');
                continue;
            }

            EnsureColumnLimit(fields.Count + 1);
            fields.Add(value.ToString().Trim());
            rows.Add(fields.ToArray());
            fields.Clear();
            value.Clear();
            fieldStarted = false;
            quoteClosed = false;
            if (rows.Count > maximumRows + 1)
            {
                throw new InvalidDataException($"导入文件超过 {maximumRows:N0} 行数据上限，系统不会静默截断。");
            }
        }

        if (quoted)
        {
            throw new InvalidDataException("CSV 文件包含未闭合的引号字段。");
        }

        EnsureRowLimit(rows.Count, maximumRows);
        return rows;
    }

    private static void EnsureRowLimit(int rowCountIncludingHeader, int maximumRows)
    {
        if (rowCountIncludingHeader > maximumRows + 1)
        {
            throw new InvalidDataException(
                $"导入文件超过 {maximumRows:N0} 行数据上限，系统不会静默截断。");
        }
    }

    private static void EnsureColumnLimit(int columnCount)
    {
        if (columnCount > MaximumColumns)
        {
            throw new InvalidDataException($"CSV 文件列数超过 {MaximumColumns:N0} 列上限。");
        }
    }

    private static void EnsureFieldLength(int length)
    {
        if (length > MaximumFieldCharacters)
        {
            throw new InvalidDataException($"CSV 文件单个字段超过 {MaximumFieldCharacters:N0} 个字符上限。");
        }
    }
}
