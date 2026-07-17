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
        private DetectedItemTableLayout DetectItemTableLayout(IExcelImportWorksheet worksheet)
        {
            DetectedItemTableLayout bestLayout = null;
            int bestScore = 0;

            for (int row = 1; row <= 80; row++)
            {
                var columns = new DetectedItemColumns();
                int score = 0;

                for (int headerRow = row; headerRow <= Math.Min(row + 2, 80); headerRow++)
                {
                    for (int column = 1; column <= 30; column++)
                    {
                        string header = NormalizeHeader(GetCellValue(worksheet.Cell(headerRow, column)));
                        if (string.IsNullOrWhiteSpace(header))
                        {
                            continue;
                        }

                        score += TrySetItemColumn(columns, header, column);
                    }
                }

                if (score > bestScore && columns.QuantityCol > 0 && (columns.StyleNoCol > 0 || columns.StyleNameCol > 0))
                {
                    if (columns.StyleNameCol == 0 && columns.StyleNoCol == 1 && columns.QuantityCol > 2)
                    {
                        columns.StyleNameCol = 2;
                    }

                    int dataStartRow = FindFirstDetectedItemDataRow(worksheet, row + 1, columns);
                    if (dataStartRow == 0)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestLayout = new DetectedItemTableLayout
                    {
                        HeaderRow = row,
                        DataStartRow = dataStartRow,
                        Columns = columns
                    };
                }
            }

            return bestScore >= 3 ? bestLayout : null;
        }

        private int FindFirstDetectedItemDataRow(IExcelImportWorksheet worksheet, int startRow, DetectedItemColumns columns)
        {
            for (int row = startRow; row <= Math.Min(startRow + 30, 120); row++)
            {
                if (IsItemDataRow(worksheet, row, columns))
                {
                    return row;
                }
            }

            return 0;
        }

        private static int TrySetItemColumn(DetectedItemColumns columns, string header, int column)
        {
            if (IsHeader(header, "客人订单号", "客户订单号", "订单号", "采购订单号", "销售订单号", "pono", "po", "po#", "purchaseorder", "orderno", "order"))
            {
                columns.PoNumberCol = column;
                return 1;
            }

            if (IsHeader(header, "客人款号", "款号", "货号", "品号", "产品编号", "产品货号", "商品编号", "商品货号", "物料号", "物料编号", "物料编码", "零件号", "零件编号", "部件号", "部件编号", "配件号", "产品型号", "款式", "型号", "款号款名", "款名款号", "styleno", "style#", "stylecode", "itemno", "item#", "itemcode", "itemnumber", "sku", "skuno", "productcode", "productno", "productid", "partno", "partnumber", "partcode", "partid", "materialno", "materialcode", "materialnumber", "componentno", "componentcode", "goodsno", "goodscode", "articleno", "article", "model", "modelno"))
            {
                columns.StyleNoCol = column;
                return 1;
            }

            if (IsHeader(header, "英文品名", "英文名称", "品名", "名称", "货物英文品名", "货物名称", "货物描述", "商品名称", "商品描述", "产品名称", "产品描述", "物料名称", "物料描述", "零件名称", "零件描述", "部件名称", "部件描述", "品名规格", "规格描述", "style", "stylename", "description", "desc", "name", "product", "productname", "productdescription", "goods", "goodsname", "goodsdescription", "itemname", "itemdescription", "descriptionofgoods", "commodity", "commodityname", "commoditydescription", "materialname", "materialdescription", "partname", "partdescription", "componentname", "componentdescription"))
            {
                columns.StyleNameCol = column;
                return 1;
            }

            if (IsHeader(header, "面料", "面料成分", "成份", "成分", "材质", "fabric", "composition", "material"))
            {
                columns.FabricCompositionCol = column;
                return 1;
            }

            if (IsHeader(header, "中文品名", "中文名称", "品名中文", "款式描述", "中文描述", "报关品名", "货物中文名称", "中文货物名称"))
            {
                columns.StyleNameCNCol = column;
                return 1;
            }

            if (IsHeader(header, "品牌", "品牌名", "商标", "brand", "label"))
            {
                columns.BrandCol = column;
                return 1;
            }

            if (IsHeader(header, "hscode", "hs", "hs编码", "海关编码", "商品编码", "商品HS编码", "编码", "税号", "税则号", "customscode", "commoditycode", "tariffcode", "tariffno", "htscode"))
            {
                columns.HSCodeCol = column;
                return 1;
            }

            if (IsHeader(header, "原产地", "产地", "原产国", "生产国", "制造国", "境内货源地", "origin", "madein", "countryoforigin", "countryofmanufacture", "manufacturingcountry"))
            {
                columns.OriginCol = column;
                return 1;
            }

            if (IsHeader(header, "数量", "总数量", "件数", "出货数量", "装运数量", "交货数量", "申报数量", "quantity", "qty", "pcs", "piece", "pieces", "qtypcs", "pcsqty", "totalqty", "units", "totalunits", "shipqty", "shippedqty", "deliveryqty", "exportqty", "declaredqty", "orderqty", "orderedqty"))
            {
                columns.QuantityCol = column;
                return 1;
            }

            if (IsHeader(header, "单位", "数量单位", "计量单位", "英文单位", "unit", "uom", "unitofmeasure", "measureunit", "um"))
            {
                columns.UnitENCol = column;
                return 1;
            }

            if (IsHeader(header, "箱数", "总箱数", "箱量", "包装件数", "包装数量", "包装", "件数箱数", "carton", "cartons", "ctns", "ctn", "ctnqty", "cartonqty", "noofctns", "noofcartons", "packages", "packageqty", "packagesqty", "numberofpackages", "pkg", "pkgs", "boxes", "box", "cases", "case", "pallets", "pallet"))
            {
                columns.CartonsCol = column;
                return 1;
            }

            if (IsHeader(header, "箱子尺寸", "箱规", "外箱尺寸", "包装尺寸", "规格", "尺寸", "长宽高", "cartonsize", "ctnsize", "cartondimension", "cartondimensions", "packingsize", "packsize", "packagedimension", "packagedimensions", "dimension", "dimensions", "size", "measurement"))
            {
                columns.DimensionCol = column;
                return 1;
            }

            if (IsHeader(header, "长", "长度", "长cm", "length", "l"))
            {
                columns.LengthCol = column;
                return 1;
            }

            if (IsHeader(header, "宽", "宽度", "宽cm", "width", "w"))
            {
                columns.WidthCol = column;
                return 1;
            }

            if (IsHeader(header, "高", "高度", "高cm", "height", "h"))
            {
                columns.HeightCol = column;
                return 1;
            }

            if (IsHeader(header, "体积", "总体积", "体积立方数", "立方数", "立方米", "方数", "空间", "volume", "measurement", "meas", "cbm", "cbms", "totalcbm", "totalcbms", "m3", "m³", "m"))
            {
                columns.VolumeCol = column;
                return 1;
            }

            if (IsHeader(header, "毛重箱", "毛重每箱", "每箱毛重", "单箱毛重", "毛重ctn", "gwctn", "gwperctn", "gwcarton", "gwctns", "grossweightctn", "grossweightcarton", "grossweightpercarton"))
            {
                columns.GWPerCtnCol = column;
                return 1;
            }

            if (IsHeader(header, "总毛重", "合计毛重", "毛重合计", "总重量", "毛重kg", "totalgw", "gwt", "grosskg", "grosskgs", "gwkg", "gwkgs", "totalgrossweight", "grossweighttotal", "grossweightkg", "grossweightkgs", "grosswt", "totalgross", "totalgrosskg", "totalgrosskgs", "totalgwkg", "totalgwkgs", "totalgweight"))
            {
                if (columns.GWTotalCol > 0 && columns.GWPerCtnCol == 0)
                {
                    columns.GWPerCtnCol = columns.GWTotalCol;
                }

                columns.GWTotalCol = column;
                return 1;
            }

            if (IsHeader(header, "毛重", "gw", "grossweight"))
            {
                if (columns.GWTotalCol > 0)
                {
                    columns.GWPerCtnCol = column;
                }
                else
                {
                    columns.GWTotalCol = column;
                }

                return 1;
            }

            if (IsHeader(header, "净重箱", "净重每箱", "每箱净重", "单箱净重", "净重ctn", "nwctn", "nwperctn", "nwcarton", "nwctns", "netweightctn", "netweightcarton", "netweightpercarton"))
            {
                columns.NWPerCtnCol = column;
                return 1;
            }

            if (IsHeader(header, "总净重", "合计净重", "净重合计", "净重kg", "totalnw", "nwt", "netkg", "netkgs", "nwkg", "nwkgs", "totalnetweight", "netweighttotal", "netweightkg", "netweightkgs", "netwt", "totalnet", "totalnetkg", "totalnetkgs", "totalnwkg", "totalnwkgs", "totalnweight"))
            {
                if (columns.NWTotalCol > 0 && columns.NWPerCtnCol == 0)
                {
                    columns.NWPerCtnCol = columns.NWTotalCol;
                }

                columns.NWTotalCol = column;
                return 1;
            }

            if (IsHeader(header, "净重", "nw", "netweight"))
            {
                if (columns.NWTotalCol > 0)
                {
                    columns.NWPerCtnCol = column;
                }
                else
                {
                    columns.NWTotalCol = column;
                }

                return 1;
            }

            if (IsHeader(header, "单价", "单价usd", "销售单价", "报关单价", "申报单价", "fob价", "unitprice", "unitpriceusd", "unitvalue", "unitvalueusd", "unitamount", "unitcost", "price", "priceusd", "priceperunit", "fobusd", "uprice", "customsunitprice", "declaredunitprice"))
            {
                columns.UnitPriceCol = column;
                return 1;
            }

            if (IsHeader(header, "总价", "金额", "金额usd", "总金额", "合计金额", "货值", "申报总价", "申报金额", "小计", "amount", "amountusd", "lineamount", "linevalue", "itemamount", "goodsvalue", "customsvalue", "declaredvalue", "exportamount", "invoiceamount", "total", "totalprice", "totalamount", "totalvalue", "subtotal", "value"))
            {
                columns.TotalPriceCol = column;
                return 1;
            }

            return 0;
        }

        private bool IsItemDataRow(IExcelImportWorksheet worksheet, int row, DetectedItemColumns columns)
        {
            string quantity = GetItemCellValue(worksheet, row, null, columns.QuantityCol);
            string styleNo = GetItemCellValue(worksheet, row, null, columns.StyleNoCol);
            string styleName = GetItemCellValue(worksheet, row, null, columns.StyleNameCol);

            if (string.IsNullOrWhiteSpace(quantity) || (!ContainsDigit(quantity) && ParseExcelDecimal(quantity) == 0))
            {
                return false;
            }

            if (IsLikelyTotalRow(styleNo) || IsLikelyTotalRow(styleName))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(styleNo) || !string.IsNullOrWhiteSpace(styleName);
        }

        private string GetItemCellValue(
            IExcelImportWorksheet worksheet,
            int row,
            DetectedItemTableLayout detectedLayout,
            int column,
            string defaultValue = "")
        {
            if (column <= 0)
            {
                return defaultValue;
            }

            string value = GetCellValue(worksheet.Cell(row, column));
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private static void ApplyDimensionsFromSingleCell(Item item, string dimensionText)
        {
            if (string.IsNullOrWhiteSpace(dimensionText) || (item.Length != 0 && item.Width != 0 && item.Height != 0))
            {
                return;
            }

            var compactParts = TryParseCompactDimensionParts(dimensionText);
            if (compactParts != null)
            {
                item.Length = compactParts.Value.Length;
                item.Width = compactParts.Value.Width;
                item.Height = compactParts.Value.Height;
                return;
            }

            var parts = dimensionText
                .Replace('×', '*')
                .Replace('X', '*')
                .Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length != 3)
            {
                parts = Regex
                    .Matches(dimensionText, @"\d+(?:\.\d+)?")
                    .Select(match => match.Value)
                    .ToArray();

                if (parts.Length != 3)
                {
                    return;
                }
            }

            item.Length = ParseExcelDecimal(parts[0]);
            item.Width = ParseExcelDecimal(parts[1]);
            item.Height = ParseExcelDecimal(parts[2]);
        }

        private static decimal ParseExcelDecimal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string normalized = text
                .Trim()
                .Replace('\u00a0', ' ')
                .Replace("，", ",")
                .Replace("．", ".")
                .Replace("－", "-")
                .Replace("（", "(")
                .Replace("）", ")");

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal currentCultureResult))
            {
                return currentCultureResult;
            }

            string numericText = Regex.Replace(normalized, @"[^\d\.\,\-\(\)]", string.Empty);
            if (string.IsNullOrWhiteSpace(numericText) || !numericText.Any(char.IsDigit))
            {
                return 0;
            }

            bool negative = numericText.StartsWith("(", StringComparison.Ordinal) && numericText.EndsWith(")", StringComparison.Ordinal);
            numericText = numericText.Trim('(', ')').Replace(",", string.Empty);

            if (decimal.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal invariantResult))
            {
                return negative ? -invariantResult : invariantResult;
            }

            return 0;
        }

        private static (decimal Length, decimal Width, decimal Height)? TryParseCompactDimensionParts(string dimensionText)
        {
            string digitsOnly = Regex.Replace(dimensionText, @"\D", string.Empty);
            if (digitsOnly.Length != 6 || digitsOnly != dimensionText.Trim())
            {
                return null;
            }

            decimal length = ParseExcelDecimal(digitsOnly[..2]);
            decimal width = ParseExcelDecimal(digitsOnly.Substring(2, 2));
            decimal height = ParseExcelDecimal(digitsOnly.Substring(4, 2));

            if (length <= 0 || width <= 0 || height <= 0)
            {
                return null;
            }

            return (length, width, height);
        }
    }
}

