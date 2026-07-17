using ClosedXML.Excel;
using ExcelDataReader;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public partial class ExcelImportService
    {
        private static IExcelImportWorkbook OpenWorkbook(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return string.Equals(Path.GetExtension(filePath), ".xls", StringComparison.OrdinalIgnoreCase)
                ? new BinaryExcelImportWorkbook(filePath, cancellationToken)
                : new ClosedXmlExcelImportWorkbook(filePath);
        }

        private static (int Row, int Column) ParseCellReference(string cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                throw new ArgumentException("Cell reference cannot be empty.", nameof(cellReference));
            }

            int column = 0;
            int index = 0;
            while (index < cellReference.Length && char.IsLetter(cellReference[index]))
            {
                column = (column * 26) + (char.ToUpperInvariant(cellReference[index]) - 'A' + 1);
                index++;
            }

            string rowText = cellReference[index..];
            if (column <= 0 || !int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row <= 0)
            {
                throw new ArgumentException($"Invalid cell reference: {cellReference}", nameof(cellReference));
            }

            return (row, column);
        }


        private interface IExcelImportWorkbook : IDisposable
        {
            IReadOnlyList<IExcelImportWorksheet> Worksheets { get; }

            IExcelImportWorksheet Worksheet(int oneBasedIndex);
        }

        private interface IExcelImportWorksheet
        {
            string Name { get; }

            IExcelImportCell Cell(string cellReference);

            IExcelImportCell Cell(int row, int column);

            string GetCellValue(string cellReference, bool preserveRichText = false);
        }

        private interface IExcelImportCell
        {
            string GetValue();
        }

        private sealed class ClosedXmlExcelImportWorkbook : IExcelImportWorkbook
        {
            private readonly XLWorkbook _workbook;

            public ClosedXmlExcelImportWorkbook(string filePath)
            {
                _workbook = new XLWorkbook(filePath);
                Worksheets = _workbook.Worksheets.Select(sheet => new ClosedXmlExcelImportWorksheet(sheet)).ToList();
            }

            public IReadOnlyList<IExcelImportWorksheet> Worksheets { get; }

            public IExcelImportWorksheet Worksheet(int oneBasedIndex)
            {
                return new ClosedXmlExcelImportWorksheet(_workbook.Worksheet(oneBasedIndex));
            }

            public void Dispose()
            {
                _workbook.Dispose();
            }
        }

        private sealed class ClosedXmlExcelImportWorksheet : IExcelImportWorksheet
        {
            private readonly IXLWorksheet _worksheet;

            public ClosedXmlExcelImportWorksheet(IXLWorksheet worksheet)
            {
                _worksheet = worksheet;
            }

            public string Name => _worksheet.Name;

            public IExcelImportCell Cell(string cellReference)
            {
                return new ClosedXmlExcelImportCell(_worksheet.Cell(cellReference));
            }

            public IExcelImportCell Cell(int row, int column)
            {
                return new ClosedXmlExcelImportCell(_worksheet.Cell(row, column));
            }

            public string GetCellValue(string cellReference, bool preserveRichText = false)
            {
                var cell = _worksheet.Cell(cellReference);
                return preserveRichText && cell.HasRichText
                    ? cell.GetRichText().Text
                    : cell.Value.ToString();
            }
        }

        private sealed class ClosedXmlExcelImportCell : IExcelImportCell
        {
            private readonly IXLCell _cell;

            public ClosedXmlExcelImportCell(IXLCell cell)
            {
                _cell = cell;
            }

            public string GetValue()
            {
                return _cell.Value.ToString();
            }
        }

        private sealed class BinaryExcelImportWorkbook : IExcelImportWorkbook
        {
            public BinaryExcelImportWorkbook(string filePath, CancellationToken cancellationToken)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var worksheets = new List<IExcelImportWorksheet>();
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rows = new List<object[]>();
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var values = new object[reader.FieldCount];
                        for (int index = 0; index < reader.FieldCount; index++)
                        {
                            values[index] = reader.GetValue(index);
                        }

                        rows.Add(values);
                    }

                    worksheets.Add(new BinaryExcelImportWorksheet(reader.Name, rows));
                }
                while (reader.NextResult());

                Worksheets = worksheets;
            }

            public IReadOnlyList<IExcelImportWorksheet> Worksheets { get; }

            public IExcelImportWorksheet Worksheet(int oneBasedIndex)
            {
                return Worksheets[oneBasedIndex - 1];
            }

            public void Dispose()
            {
            }
        }

        private sealed class BinaryExcelImportWorksheet : IExcelImportWorksheet
        {
            private readonly IReadOnlyList<object[]> _rows;

            public BinaryExcelImportWorksheet(string name, IReadOnlyList<object[]> rows)
            {
                Name = name ?? string.Empty;
                _rows = rows;
            }

            public string Name { get; }

            public IExcelImportCell Cell(string cellReference)
            {
                var (row, column) = ParseCellReference(cellReference);
                return Cell(row, column);
            }

            public IExcelImportCell Cell(int row, int column)
            {
                if (row <= 0 || column <= 0 || row > _rows.Count)
                {
                    return BinaryExcelImportCell.Empty;
                }

                var values = _rows[row - 1];
                if (column > values.Length)
                {
                    return BinaryExcelImportCell.Empty;
                }

                return new BinaryExcelImportCell(values[column - 1]);
            }

            public string GetCellValue(string cellReference, bool preserveRichText = false)
            {
                return Cell(cellReference).GetValue();
            }
        }

        private sealed class BinaryExcelImportCell : IExcelImportCell
        {
            public static readonly BinaryExcelImportCell Empty = new(null);

            private readonly object _value;

            public BinaryExcelImportCell(object value)
            {
                _value = value;
            }

            public string GetValue()
            {
                return _value switch
                {
                    null => string.Empty,
                    DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => _value.ToString() ?? string.Empty,
                };
            }
        }    }
}

