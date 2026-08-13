using ClosedXML.Excel;
using ExcelDataReader;
using System.Globalization;
using System.Text;

namespace ExportDocManager.Services.Data
{
    public sealed partial class BuiltInExcelImportAnalyzer
    {
        private static IReadOnlyList<SheetGrid> ReadWorkbook(string filePath, CancellationToken cancellationToken)
        {
            return string.Equals(Path.GetExtension(filePath), ".xls", StringComparison.OrdinalIgnoreCase)
                ? ReadBinaryWorkbook(filePath, cancellationToken)
                : ReadOpenXmlWorkbook(filePath, cancellationToken);
        }

        private static IReadOnlyList<SheetGrid> ReadOpenXmlWorkbook(string filePath, CancellationToken cancellationToken)
        {
            using var workbook = new XLWorkbook(filePath);
            var sheets = new List<SheetGrid>();

            foreach (var worksheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rows = new List<List<string>>();
                for (int row = 1; row <= MaxProfileRows; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var values = new List<string>();
                    for (int column = 1; column <= MaxProfileColumns; column++)
                    {
                        values.Add(worksheet.Cell(row, column).Value.ToString());
                    }

                    rows.Add(values);
                }

                sheets.Add(new SheetGrid(worksheet.Name, rows));
            }

            return sheets;
        }

        private static IReadOnlyList<SheetGrid> ReadBinaryWorkbook(string filePath, CancellationToken cancellationToken)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sheets = new List<SheetGrid>();

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rows = new List<List<string>>();
                int rowCount = 0;
                while (reader.Read() && rowCount < MaxProfileRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var values = new List<string>();
                    int columnCount = Math.Min(reader.FieldCount, MaxProfileColumns);
                    for (int column = 0; column < columnCount; column++)
                    {
                        values.Add(CellValueToString(reader.GetValue(column)));
                    }

                    rows.Add(values);
                    rowCount++;
                }

                sheets.Add(new SheetGrid(reader.Name, rows));
            }
            while (reader.NextResult());

            return sheets;
        }

        private static string CellValueToString(object value)
        {
            return value switch
            {
                null => string.Empty,
                DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }
    }
}
