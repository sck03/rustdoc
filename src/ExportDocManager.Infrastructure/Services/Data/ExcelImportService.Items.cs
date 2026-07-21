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
        private void ParseItemsInfo(
            IExcelImportWorksheet worksheet,
            Invoice invoice,
            List<string> errors,
            ExcelImportSettings settings,
            ExcelImportAnalysisReport analysisReport,
            CancellationToken cancellationToken)
        {
            try
            {
                var detectedLayout = GetDetectedLayoutFromAnalysis(analysisReport)
                    ?? DetectItemTableLayout(worksheet);
                if (detectedLayout != null)
                {
                    RepairDetectedLayoutFromWorksheetHeaders(worksheet, detectedLayout);
                    InferMissingColumnsFromValues(worksheet, detectedLayout);
                    ApplyDetectedLayoutToAnalysisReport(analysisReport, worksheet.Name, detectedLayout);
                }

                if (detectedLayout == null && TryParseBookingSheetGoodsTable(worksheet, invoice, analysisReport))
                {
                    CalculateInvoiceTotals(invoice);
                    return;
                }

                int startRow = detectedLayout?.DataStartRow ?? settings.ItemsStartRow;
                int endRow = settings.ItemsEndRow;

                if (endRow > 0 && endRow < startRow)
                {
                    return;
                }

                int currentRow = startRow;
                int maxRows = endRow > 0 ? Math.Max(0, endRow - startRow + 1) : 200;
                int rowCount = 0;
                int blankRowCount = 0;

                while (rowCount < maxRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        string quantity = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.QuantityCol ?? settings.QuantityCol);
                        string styleNo = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.StyleNoCol ?? settings.StyleNoCol);

                        if (detectedLayout != null && !IsItemDataRow(worksheet, currentRow, detectedLayout.Columns))
                        {
                            blankRowCount++;
                            if (blankRowCount >= 5)
                            {
                                break;
                            }

                            currentRow++;
                            rowCount++;
                            continue;
                        }

                        if (detectedLayout == null && string.IsNullOrEmpty(quantity) && string.IsNullOrEmpty(styleNo))
                            break;

                        var item = new Item
                        {
                            InvoiceId = invoice.Id,
                            PoNumber = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.PoNumberCol ?? settings.PoNumberCol),
                            StyleNo = styleNo,
                            StyleName = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.StyleNameCol ?? settings.StyleNameCol),
                            FabricComposition = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.FabricCompositionCol ?? settings.FabricCompositionCol),
                            StyleNameCN = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.StyleNameCNCol ?? settings.StyleNameCNCol),
                            Brand = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.BrandCol ?? settings.BrandCol),
                            HSCode = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.HSCodeCol ?? settings.HSCodeCol),
                            Origin = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.OriginCol ?? settings.OriginCol, "宁波其他"),
                            Quantity = ParseExcelDecimal(quantity),
                            UnitEN = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.UnitENCol ?? settings.UnitENCol, "PCS"),
                            UnitCN = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.UnitCNCol ?? settings.UnitCNCol, "件"),
                            Cartons = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.CartonsCol ?? settings.CartonsCol)),
                            CtnUnitEN = GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.CtnUnitENCol ?? settings.CtnUnitENCol, "CTNS"),
                            Length = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.LengthCol ?? settings.LengthCol)),
                            Width = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.WidthCol ?? settings.WidthCol)),
                            Height = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.HeightCol ?? settings.HeightCol)),
                            Volume = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.VolumeCol ?? settings.VolumeCol)),
                            GWPerCtn = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.GWPerCtnCol ?? settings.GWPerCtnCol)),
                            GWTotal = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.GWTotalCol ?? settings.GWTotalCol)),
                            NWPerCtn = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.NWPerCtnCol ?? settings.NWPerCtnCol)),
                            NWTotal = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.NWTotalCol ?? settings.NWTotalCol)),
                            UnitPrice = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.UnitPriceCol ?? settings.UnitPriceCol)),
                            TotalPrice = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.TotalPriceCol ?? settings.TotalPriceCol))
                        };

                        NormalizeItemDescriptionLanguages(item);
                        NormalizeItemDescriptionAndBrand(item);
                        ApplyDimensionsFromSingleCell(item, GetItemCellValue(worksheet, currentRow, detectedLayout, detectedLayout?.Columns.DimensionCol ?? 0));
                        if (item.UnitPrice == 0 && item.Quantity != 0 && item.TotalPrice != 0)
                        {
                            item.UnitPrice = Math.Round(item.TotalPrice / item.Quantity, 4, MidpointRounding.AwayFromZero);
                        }

                        invoice.Items.Add(item);
                        blankRowCount = 0;
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"解析第{currentRow}行时出错: {ex.Message}";
                        errors.Add(errorMsg);
                    }

                    currentRow++;
                    rowCount++;
                }

                CalculateInvoiceTotals(invoice);
            }
            catch (Exception ex)
            {
                errors.Add($"解析商品明细时出错: {ex.Message}");
            }
        }

        private void CalculateInvoiceTotals(Invoice invoice)
        {
            if (invoice.Items != null && invoice.Items.Count > 0)
            {
                invoice.TotalCartons = invoice.Items.Sum(i => i.Cartons);
                invoice.TotalQuantity = invoice.Items.Sum(i => i.Quantity);
                invoice.TotalGrossWeight = invoice.Items.Sum(i => i.GWTotal);
                invoice.TotalNetWeight = invoice.Items.Sum(i => i.NWTotal);
                invoice.TotalVolume = invoice.Items.Sum(i => i.Volume);
                invoice.TotalAmount = invoice.Items.Sum(i => i.TotalPrice);
            }
        }

        private static void NormalizeItemDescriptionAndBrand(Item item)
        {
            if (item == null)
            {
                return;
            }

            string descriptionSource = !string.IsNullOrWhiteSpace(item.StyleNameCN)
                ? item.StyleNameCN
                : item.Brand;
            if (string.IsNullOrWhiteSpace(descriptionSource))
            {
                return;
            }

            var match = Regex.Match(
                descriptionSource,
                @"(?<name>.*?)(?:品牌名|品牌)\s*[:：]\s*(?<brand>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return;
            }

            string chineseName = NormalizeExcelTextBlock(match.Groups["name"].Value);
            string brand = NormalizeExcelTextBlock(match.Groups["brand"].Value);

            if (!string.IsNullOrWhiteSpace(chineseName)
                && (string.IsNullOrWhiteSpace(item.StyleNameCN)
                    || string.Equals(item.StyleNameCN, descriptionSource, StringComparison.OrdinalIgnoreCase)))
            {
                item.StyleNameCN = chineseName;
            }

            if (!string.IsNullOrWhiteSpace(brand)
                && (string.IsNullOrWhiteSpace(item.Brand)
                    || string.Equals(item.Brand, descriptionSource, StringComparison.OrdinalIgnoreCase)))
            {
                item.Brand = brand;
            }
        }

        private static void NormalizeItemDescriptionLanguages(Item item)
        {
            if (item == null
                || !ContainsCjkText(item.StyleName)
                || ContainsCjkText(item.StyleNameCN)
                || !ContainsLatinText(item.StyleNameCN))
            {
                return;
            }

            (item.StyleName, item.StyleNameCN) = (item.StyleNameCN, item.StyleName);
        }

        private static bool ContainsCjkText(string value) =>
            (value ?? string.Empty).Any(character => character is >= '\u3400' and <= '\u9fff');

        private static bool ContainsLatinText(string value) =>
            (value ?? string.Empty).Any(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
    }
}
