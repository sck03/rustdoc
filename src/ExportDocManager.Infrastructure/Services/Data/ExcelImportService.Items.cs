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
                        string quantity = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.QuantityCol ?? settings.QuantityCol);
                        string styleNo = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.StyleNoCol ?? settings.StyleNoCol);

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

                        string unitPriceText = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.UnitPriceCol ?? settings.UnitPriceCol);
                        string totalPriceText = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.TotalPriceCol ?? settings.TotalPriceCol);
                        var item = new Item
                        {
                            InvoiceId = invoice.Id,
                            PoNumber = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.PoNumberCol ?? settings.PoNumberCol),
                            StyleNo = styleNo,
                            StyleName = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.StyleNameCol ?? settings.StyleNameCol),
                            FabricComposition = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.FabricCompositionCol ?? settings.FabricCompositionCol),
                            StyleNameCN = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.StyleNameCNCol ?? settings.StyleNameCNCol),
                            Brand = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.BrandCol ?? settings.BrandCol),
                            HSCode = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.HSCodeCol ?? settings.HSCodeCol),
                            Origin = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.OriginCol ?? settings.OriginCol, "宁波其他"),
                            Quantity = ParseExcelDecimal(quantity),
                            UnitEN = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.UnitENCol ?? settings.UnitENCol, "PCS"),
                            UnitCN = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.UnitCNCol ?? settings.UnitCNCol, "件"),
                            Cartons = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.CartonsCol ?? settings.CartonsCol)),
                            CtnUnitEN = GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.CtnUnitENCol ?? settings.CtnUnitENCol, "CTNS"),
                            Length = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.LengthCol ?? settings.LengthCol)),
                            Width = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.WidthCol ?? settings.WidthCol)),
                            Height = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.HeightCol ?? settings.HeightCol)),
                            Volume = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.VolumeCol ?? settings.VolumeCol)),
                            GWPerCtn = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.GWPerCtnCol ?? settings.GWPerCtnCol)),
                            GWTotal = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.GWTotalCol ?? settings.GWTotalCol)),
                            NWPerCtn = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.NWPerCtnCol ?? settings.NWPerCtnCol)),
                            NWTotal = ParseExcelDecimal(GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.NWTotalCol ?? settings.NWTotalCol)),
                            UnitPrice = ParseExcelDecimal(unitPriceText),
                            TotalPrice = ParseExcelDecimal(totalPriceText)
                        };

                        NormalizeItemDescriptionLanguages(item);
                        NormalizeItemDescriptionAndBrand(item);
                        ApplyDimensionsFromSingleCell(item, GetItemCellValue(worksheet, currentRow, detectedLayout?.Columns.DimensionCol ?? 0));
                        NormalizeImportedItemMeasurements(item);
                        NormalizeImportedItemPrice(
                            item,
                            hasUnitPriceValue: !string.IsNullOrWhiteSpace(unitPriceText),
                            hasTotalPriceValue: !string.IsNullOrWhiteSpace(totalPriceText));

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
                invoice.TotalGrossWeight = ItemMeasurementPrecisionPolicy.RoundWeight(invoice.Items.Sum(i => i.GWTotal));
                invoice.TotalNetWeight = ItemMeasurementPrecisionPolicy.RoundWeight(invoice.Items.Sum(i => i.NWTotal));
                invoice.TotalVolume = ItemMeasurementPrecisionPolicy.RoundVolume(invoice.Items.Sum(i => i.Volume));
                invoice.TotalAmount = decimal.Round(
                    invoice.Items.Sum(i => i.TotalPrice),
                    2,
                    MidpointRounding.AwayFromZero);
            }
        }

        private static void NormalizeImportedItemPrice(Item item, bool hasUnitPriceValue, bool hasTotalPriceValue)
        {
            if (item == null)
            {
                return;
            }

            bool calculatedTotalMatches = item.Quantity > 0 &&
                decimal.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero) ==
                decimal.Round(item.TotalPrice, 2, MidpointRounding.AwayFromZero);

            if (hasTotalPriceValue && (!hasUnitPriceValue || !calculatedTotalMatches))
            {
                item.CalculateUnitPriceFromTotal();
                return;
            }

            item.PriceCalculationMode = ItemPriceCalculationModeCatalog.UnitPriceDriven;
            item.CalculateTotalPrice();
        }

        private static void NormalizeImportedItemMeasurements(Item item)
        {
            if (item == null)
            {
                return;
            }

            item.Volume = ItemMeasurementPrecisionPolicy.RoundVolume(item.Volume);
            item.GWPerCtn = ItemMeasurementPrecisionPolicy.RoundWeight(item.GWPerCtn);
            item.GWTotal = ItemMeasurementPrecisionPolicy.RoundWeight(item.GWTotal);
            item.NWPerCtn = ItemMeasurementPrecisionPolicy.RoundWeight(item.NWPerCtn);
            item.NWTotal = ItemMeasurementPrecisionPolicy.RoundWeight(item.NWTotal);
        }

        private static void NormalizeItemDescriptionAndBrand(Item item)
        {
            if (item == null)
            {
                return;
            }

            string? descriptionSource = !string.IsNullOrWhiteSpace(item.StyleNameCN)
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

        private static bool ContainsCjkText(string? value) =>
            (value ?? string.Empty).Any(character => character is >= '\u3400' and <= '\u9fff');

        private static bool ContainsLatinText(string? value) =>
            (value ?? string.Empty).Any(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
    }
}
