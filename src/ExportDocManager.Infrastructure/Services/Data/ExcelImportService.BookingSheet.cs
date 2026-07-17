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
        private bool TryParseBookingSheetGoodsTable(
            IExcelImportWorksheet worksheet,
            Invoice invoice,
            ExcelImportAnalysisReport analysisReport)
        {
            var table = FindBookingSheetGoodsTable(worksheet);
            if (table == null)
            {
                return false;
            }

            string shippingMarks = GetBookingSheetShippingMarks(worksheet, table);
            if (!string.IsNullOrWhiteSpace(shippingMarks))
            {
                invoice.ShippingMarks = PickMoreCompleteMultilineValue(invoice.ShippingMarks, shippingMarks);
            }

            bool added = false;
            foreach (int row in FindBookingSheetDataRows(worksheet, table))
            {
                var descriptionLines = CollectBookingSheetDescriptionLines(worksheet, table, row);
                var item = BuildBookingSheetItem(worksheet, table, row, descriptionLines);
                if (item == null)
                {
                    continue;
                }

                item.InvoiceId = invoice.Id;
                invoice.Items.Add(item);
                added = true;
            }

            if (added)
            {
                ApplyBookingSheetLayoutToAnalysisReport(analysisReport, worksheet.Name, table);
            }

            return added;
        }

        private BookingSheetGoodsTable FindBookingSheetGoodsTable(IExcelImportWorksheet worksheet)
        {
            for (int row = 1; row <= 80; row++)
            {
                int marksCol = 0;
                int cartonsCol = 0;
                int descriptionCol = 0;
                int grossWeightCol = 0;
                int measurementCol = 0;

                for (int column = 1; column <= 30; column++)
                {
                    string header = GetCellValue(worksheet.Cell(row, column));
                    if (string.IsNullOrWhiteSpace(header))
                    {
                        continue;
                    }

                    if (HeaderContains(header, "marks and numbers", "shipping marks", "shipping mark", "marks", "唛头", "箱唛"))
                    {
                        marksCol = column;
                    }
                    else if (HeaderContains(header, "quantity & type", "quantity and type", "quantity type", "数量", "包装件数"))
                    {
                        cartonsCol = column;
                    }
                    else if (HeaderContains(header, "description of goods", "description goods", "goods description", "品名", "货描", "商品编码"))
                    {
                        descriptionCol = column;
                    }
                    else if (HeaderContains(header, "gross weight", "grossweight", "毛重"))
                    {
                        grossWeightCol = column;
                    }
                    else if (HeaderContains(header, "measurement", "measurements", "cbm", "体积"))
                    {
                        measurementCol = column;
                    }
                }

                if (marksCol <= 0 || cartonsCol <= 0 || descriptionCol <= 0)
                {
                    continue;
                }

                int dataStartRow = FindFirstBookingSheetDataRow(
                    worksheet,
                    row + 1,
                    cartonsCol,
                    descriptionCol,
                    grossWeightCol,
                    measurementCol);
                if (dataStartRow <= 0)
                {
                    continue;
                }

                return new BookingSheetGoodsTable
                {
                    HeaderRow = row,
                    DataStartRow = dataStartRow,
                    MarksCol = marksCol,
                    CartonsCol = cartonsCol,
                    DescriptionCol = descriptionCol,
                    GrossWeightCol = grossWeightCol,
                    MeasurementCol = measurementCol
                };
            }

            return null;
        }

        private int FindFirstBookingSheetDataRow(
            IExcelImportWorksheet worksheet,
            int startRow,
            int cartonsCol,
            int descriptionCol,
            int grossWeightCol,
            int measurementCol)
        {
            for (int row = startRow; row <= Math.Min(startRow + 12, 100); row++)
            {
                if (IsBookingSheetDataRow(worksheet, row, cartonsCol, descriptionCol, grossWeightCol, measurementCol))
                {
                    return row;
                }
            }

            return 0;
        }

        private IEnumerable<int> FindBookingSheetDataRows(IExcelImportWorksheet worksheet, BookingSheetGoodsTable table)
        {
            for (int row = table.DataStartRow; row <= Math.Min(table.HeaderRow + 20, 120); row++)
            {
                if (IsLikelyBookingSheetSummaryRow(worksheet, table, row))
                {
                    break;
                }

                if (IsBookingSheetDataRow(
                    worksheet,
                    row,
                    table.CartonsCol,
                    table.DescriptionCol,
                    table.GrossWeightCol,
                    table.MeasurementCol))
                {
                    yield return row;
                }
            }
        }

        private bool IsBookingSheetDataRow(
            IExcelImportWorksheet worksheet,
            int row,
            int cartonsCol,
            int descriptionCol,
            int grossWeightCol,
            int measurementCol)
        {
            string description = GetCellValue(worksheet.Cell(row, descriptionCol));
            if (string.IsNullOrWhiteSpace(description)
                || LooksLikeKnownDocumentLabel(description)
                || LooksLikeItemHeader(description)
                || LooksLikeHsCodeLine(description))
            {
                return false;
            }

            decimal cartons = ParseExcelDecimal(GetCellValue(worksheet.Cell(row, cartonsCol)));
            decimal grossWeight = grossWeightCol > 0 ? ParseExcelDecimal(GetCellValue(worksheet.Cell(row, grossWeightCol))) : 0;
            decimal measurement = measurementCol > 0 ? ParseExcelDecimal(GetCellValue(worksheet.Cell(row, measurementCol))) : 0;
            return (cartons > 0 || grossWeight > 0 || measurement > 0)
                && description.Any(char.IsLetter);
        }

        private bool IsLikelyBookingSheetSummaryRow(IExcelImportWorksheet worksheet, BookingSheetGoodsTable table, int row)
        {
            string markValue = GetCellValue(worksheet.Cell(row, table.MarksCol));
            string description = GetCellValue(worksheet.Cell(row, table.DescriptionCol));
            return IsLikelyTotalRow(markValue)
                || IsLikelyTotalRow(description)
                || NormalizeText(markValue).Contains("总计", StringComparison.Ordinal)
                || NormalizeText(markValue).Contains("total", StringComparison.Ordinal);
        }

        private List<string> CollectBookingSheetDescriptionLines(
            IExcelImportWorksheet worksheet,
            BookingSheetGoodsTable table,
            int dataRow)
        {
            var lines = new List<string>();
            for (int row = dataRow; row <= Math.Min(dataRow + 6, 120); row++)
            {
                if (row > dataRow
                    && IsBookingSheetDataRow(
                        worksheet,
                        row,
                        table.CartonsCol,
                        table.DescriptionCol,
                        table.GrossWeightCol,
                        table.MeasurementCol))
                {
                    break;
                }

                string value = NormalizeExcelTextBlock(GetCellValue(worksheet.Cell(row, table.DescriptionCol)));
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (row > dataRow)
                    {
                        break;
                    }

                    continue;
                }

                if (LooksLikeKnownDocumentLabel(value) || LooksLikeItemHeader(value))
                {
                    continue;
                }

                lines.Add(value);
            }

            return lines;
        }

        private Item BuildBookingSheetItem(
            IExcelImportWorksheet worksheet,
            BookingSheetGoodsTable table,
            int row,
            IReadOnlyList<string> descriptionLines)
        {
            string styleName = descriptionLines.FirstOrDefault(line =>
                !LooksLikeHsCodeLine(line) && line.Any(char.IsLetter) && !line.Any(IsCjk));
            string styleNameCn = descriptionLines.FirstOrDefault(line => line.Any(IsCjk));
            string hsCode = descriptionLines
                .Select(ExtractHsCode)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (string.IsNullOrWhiteSpace(styleName)
                && string.IsNullOrWhiteSpace(styleNameCn)
                && string.IsNullOrWhiteSpace(hsCode))
            {
                return null;
            }

            decimal cartons = ParseExcelDecimal(GetCellValue(worksheet.Cell(row, table.CartonsCol)));
            decimal grossWeight = table.GrossWeightCol > 0
                ? ParseExcelDecimal(GetCellValue(worksheet.Cell(row, table.GrossWeightCol)))
                : 0;
            decimal measurement = table.MeasurementCol > 0
                ? ParseExcelDecimal(GetCellValue(worksheet.Cell(row, table.MeasurementCol)))
                : 0;

            return new Item
            {
                StyleName = NormalizeExcelTextBlock(styleName),
                StyleNameCN = NormalizeExcelTextBlock(styleNameCn),
                HSCode = hsCode ?? string.Empty,
                Origin = "宁波其他",
                Quantity = 0,
                UnitEN = string.Empty,
                UnitCN = string.Empty,
                Cartons = cartons,
                CtnUnitEN = DetectBookingSheetCartonUnit(worksheet, table, row),
                CtnUnitCN = "箱",
                GWTotal = grossWeight,
                Volume = measurement
            };
        }

        private string GetBookingSheetShippingMarks(IExcelImportWorksheet worksheet, BookingSheetGoodsTable table)
        {
            var lines = new List<string>();
            int blankRows = 0;
            for (int row = table.HeaderRow + 1; row <= Math.Min(table.HeaderRow + 18, 120); row++)
            {
                string value = NormalizeExcelTextBlock(GetCellValue(worksheet.Cell(row, table.MarksCol)));
                if (string.IsNullOrWhiteSpace(value))
                {
                    blankRows++;
                    if (blankRows >= 3)
                    {
                        break;
                    }

                    continue;
                }

                if (IsLikelyBookingSheetSummaryRow(worksheet, table, row)
                    || LooksLikeKnownDocumentLabel(value)
                    || LooksLikeItemHeader(value))
                {
                    break;
                }

                blankRows = 0;
                lines.Add(value);
            }

            return NormalizeExcelTextBlock(string.Join(Environment.NewLine, lines));
        }

        private static string DetectBookingSheetCartonUnit(
            IExcelImportWorksheet worksheet,
            BookingSheetGoodsTable table,
            int row)
        {
            string combined = string.Join(
                " ",
                Enumerable.Range(Math.Max(1, table.HeaderRow), Math.Max(1, row - table.HeaderRow + 1))
                    .Select(currentRow => GetCellValueSafe(worksheet.Cell(currentRow, table.CartonsCol))));
            string normalized = NormalizeText(combined);
            return normalized.Contains("ctn", StringComparison.Ordinal) || normalized.Contains("carton", StringComparison.Ordinal)
                ? "CTNS"
                : string.Empty;
        }

        private static bool LooksLikeHsCodeLine(string value)
        {
            return !string.IsNullOrWhiteSpace(ExtractHsCode(value));
        }

        private static string ExtractHsCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var match = Regex.Match(
                value,
                @"\bH\s*\.?\s*S\s*\.?\s*[:：]?\s*(?<code>\d[\d\s\.-]{5,})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                match = Regex.Match(value, @"(?<code>\b\d{8,13}\b)", RegexOptions.CultureInvariant);
            }

            if (!match.Success)
            {
                return string.Empty;
            }

            string digits = Regex.Replace(match.Groups["code"].Value, @"\D", string.Empty);
            return digits.Length is >= 8 and <= 13 ? digits : string.Empty;
        }

        private static bool HeaderContains(string value, params string[] candidates)
        {
            string normalized = NormalizeHeader(value);
            return candidates
                .Select(NormalizeHeader)
                .Any(candidate => !string.IsNullOrWhiteSpace(candidate)
                    && (normalized == candidate || normalized.Contains(candidate, StringComparison.Ordinal)));
        }

        private static void ApplyBookingSheetLayoutToAnalysisReport(
            ExcelImportAnalysisReport analysisReport,
            string worksheetName,
            BookingSheetGoodsTable table)
        {
            if (analysisReport == null || table == null)
            {
                return;
            }

            analysisReport.ItemTable = new ExcelImportItemTableAnalysis
            {
                WorksheetName = worksheetName ?? string.Empty,
                HeaderRow = table.HeaderRow,
                HeaderDepth = Math.Max(1, table.DataStartRow - table.HeaderRow),
                DataStartRow = table.DataStartRow,
                Confidence = 0.66m,
                Columns = new ExcelImportItemColumnAnalysis
                {
                    StyleNameCol = table.DescriptionCol,
                    StyleNameCNCol = table.DescriptionCol,
                    HSCodeCol = table.DescriptionCol,
                    CartonsCol = table.CartonsCol,
                    CtnUnitENCol = table.CartonsCol,
                    GWTotalCol = table.GrossWeightCol,
                    VolumeCol = table.MeasurementCol
                }
            };
        }
    }
}

