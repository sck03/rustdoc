using ClosedXML.Excel;
using ExcelDataReader;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    public sealed class BuiltInExcelImportAnalyzer : IExcelImportAnalyzer
    {
        private const int MaxProfileRows = 140;
        private const int MaxProfileColumns = 60;

        private static readonly FieldDefinition[] FieldDefinitions =
        [
            new("ExporterNameEN", "出口商/SHIPPER", ["发票抬头", "出口商英文名称", "出口商", "发货人", "shipper/exporter", "shipper name", "exporter name", "shipper", "exporter", "seller", "consignor"]),
            new("ExporterNameCN", "出口商中文名称", ["出口商中文名称", "出口商中文", "中文抬头"]),
            new("ExporterAddressEN", "出口商地址", ["出口商地址", "发货人", "shipper/exporter", "shipper address", "exporter address", "shipper"], MultiLine: true),
            new("CustomerNameEN", "收货人/CONSIGNEE", ["收货人", "客户", "consignee name", "customer name", "buyer", "consignee", "customer"]),
            new("CustomerAddressEN", "收货人地址", ["收货人地址", "客户地址", "consignee address", "customer address", "consignee"], MultiLine: true),
            new("NotifyPartyName", "通知人", ["通知人", "通知方", "notify party name", "notify party"]),
            new("NotifyPartyAddress", "通知人地址", ["通知人地址", "通知方地址", "notify party address", "notify party"], MultiLine: true),
            new("InvoiceNo", "发票号", ["发票号", "发票号码", "invoice no", "invoice number", "invoice#", "invoice", "inv no"]),
            new("ContractNo", "合同号", ["合同号", "合同号码", "contract no", "contract number", "contract#", "contract", "s/c no", "sc no"]),
            new("InvoiceDate", "发票日期", ["发票日期", "日期", "时间", "invoice date", "date"]),
            new("PortOfLoading", "起运港", ["起运港", "装运港", "起运地", "port of loading", "loading port", "pol"]),
            new("PortOfDestination", "目的港/目的地", ["目的港", "目的地", "目的口岸", "port of destination", "destination port", "port of discharge", "discharge port", "pod", "destination"]),
            new("DestinationCountry", "目的国", ["目的国", "目的国家", "destination country", "country"]),
            new("TradeTerms", "贸易条款", ["贸易条款", "价格条款", "成交方式", "incoterms", "trade terms", "price terms"]),
            new("TransportMode", "运输方式", ["运输方式", "运输模式", "transport mode", "shipment mode", "mode of transport"]),
            new("PaymentTerms", "付款方式", ["付款方式", "收汇方式", "收回方式", "payment terms", "terms of payment", "payment"]),
            new("Currency", "币种", ["币种", "货币", "currency", "curr"]),
            new("SupervisionMode", "监管方式", ["监管方式", "贸易方式", "trade mode", "customs mode"]),
            new("LetterOfCreditNo", "信用证号", ["信用证号", "l/c no", "lc no", "letter of credit", "letter of credit no"]),
            new("IssuingBank", "开证行", ["开证行", "issuing bank"]),
            new("ShippingMarks", "唛头", ["唛头", "箱唛", "唛头信息", "shipping mark", "shipping marks", "marks", "marks and numbers"], MultiLine: true, PreferBelow: true)
        ];

        public Task<ExcelImportAnalysisReport> AnalyzeAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => AnalyzeInternal(filePath, settings ?? new ExcelImportSettings(), cancellationToken), cancellationToken);
        }

        private static ExcelImportAnalysisReport AnalyzeInternal(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sheets = ReadWorkbook(filePath, cancellationToken);
            var profiles = sheets
                .Select(sheet => AnalyzeSheet(sheet, settings, cancellationToken))
                .ToList();

            var selected = profiles
                .OrderByDescending(profile => profile.Score)
                .ThenBy(profile => profile.SheetIndex)
                .FirstOrDefault();

            var report = new ExcelImportAnalysisReport
            {
                AnalyzerId = "builtin-dotnet",
                SourcePath = Path.GetFullPath(filePath),
                SelectedWorksheetName = selected?.Sheet.Name ?? string.Empty,
                Confidence = selected == null ? 0 : ToDecimalConfidence(selected.Confidence),
                Sheets = profiles.Select(profile => new ExcelImportSheetAnalysis
                {
                    Name = profile.Sheet.Name,
                    UsedRowCount = profile.Sheet.UsedRowCount,
                    UsedColumnCount = profile.Sheet.UsedColumnCount,
                    FieldCandidateCount = profile.Fields.Count,
                    HasItemTable = profile.ItemTable != null,
                    Confidence = ToDecimalConfidence(profile.Confidence)
                }).ToList(),
                Fields = selected?.Fields.Values
                    .OrderBy(field => field.Row == 0 ? int.MaxValue : field.Row)
                    .ThenBy(field => field.Column == 0 ? int.MaxValue : field.Column)
                    .ThenBy(field => field.FieldKey, StringComparer.Ordinal)
                    .ToList() ?? new List<ExcelImportFieldAnalysis>(),
                ItemTable = selected?.ItemTable
            };

            AddCompletenessIssues(report);
            return report;
        }

        private static SheetProfile AnalyzeSheet(
            SheetGrid sheet,
            ExcelImportSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fields = DetectFields(sheet, settings);
            var itemTable = DetectItemTable(sheet, cancellationToken);

            int requiredFieldCount = CountPresent(fields, "InvoiceNo", "CustomerNameEN", "ExporterNameEN", "PortOfLoading", "PortOfDestination");
            double score = fields.Count * 0.7
                + requiredFieldCount * 1.5
                + (itemTable == null ? 0 : 5 + (double)itemTable.Confidence * 5);

            double confidence = Math.Min(1.0, score / 22.0);
            return new SheetProfile(sheet, fields, itemTable, score, confidence);
        }

        private static Dictionary<string, ExcelImportFieldAnalysis> DetectFields(
            SheetGrid sheet,
            ExcelImportSettings settings)
        {
            var fields = new Dictionary<string, ExcelImportFieldAnalysis>(StringComparer.Ordinal);

            foreach (var definition in FieldDefinitions)
            {
                var candidate = FindFieldByLabels(sheet, definition);
                var configured = FindFieldByConfiguredCell(sheet, definition, settings);
                var best = PickBetter(candidate, configured);
                if (best != null)
                {
                    fields[definition.Key] = best;
                }
            }

            PromotePartyBlocks(fields);
            return fields;
        }

        private static ExcelImportFieldAnalysis FindFieldByConfiguredCell(
            SheetGrid sheet,
            FieldDefinition definition,
            ExcelImportSettings settings)
        {
            string cellReference = GetConfiguredCellReference(definition.Key, settings);
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                return null;
            }

            try
            {
                var (row, column) = ParseCellReference(cellReference);
                string value = sheet.Get(row, column).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                return CreateField(definition, value, sheet.Name, row, column, 0.45m, "ConfiguredCell");
            }
            catch
            {
                return null;
            }
        }

        private static ExcelImportFieldAnalysis FindFieldByLabels(SheetGrid sheet, FieldDefinition definition)
        {
            ExcelImportFieldAnalysis best = null;

            for (int row = 1; row <= Math.Min(sheet.UsedRowCount, 100); row++)
            {
                for (int column = 1; column <= Math.Min(sheet.UsedColumnCount, 50); column++)
                {
                    string labelText = sheet.Get(row, column);
                    if (string.IsNullOrWhiteSpace(labelText))
                    {
                        continue;
                    }

                    var match = MatchLabel(labelText, definition.Labels);
                    if (match == null)
                    {
                        continue;
                    }

                    if (IsAddressLabelForDifferentField(labelText, definition))
                    {
                        continue;
                    }

                    string value = ExtractInlineValue(labelText, definition.Labels);
                    int valueRow = row;
                    int valueColumn = column;

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        var nearby = definition.PreferBelow
                            ? FindBestBelowValue(sheet, row, column, definition.MultiLine)
                            : FindNearbyValue(sheet, row, column, definition.MultiLine);

                        value = nearby.Value;
                        valueRow = nearby.Row;
                        valueColumn = nearby.Column;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (IsGenericPlaceholderValue(value))
                    {
                        continue;
                    }

                    decimal confidence = Math.Min(0.98m, match.Value.Score + (definition.MultiLine ? 0.02m : 0.05m));
                    var candidate = CreateField(
                        definition,
                        value,
                        sheet.Name,
                        valueRow,
                        valueColumn,
                        confidence,
                        match.Value.Kind);

                    best = PickBetter(best, candidate);
                }
            }

            return best;
        }

        private static ExcelImportItemTableAnalysis DetectItemTable(
            SheetGrid sheet,
            CancellationToken cancellationToken)
        {
            ExcelImportItemTableAnalysis best = null;
            int bestScore = 0;

            for (int row = 1; row <= Math.Min(sheet.UsedRowCount, 100); row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var columns = new ExcelImportItemColumnAnalysis();
                int score = 0;

                for (int headerRow = row; headerRow <= Math.Min(row + 2, sheet.UsedRowCount); headerRow++)
                {
                    for (int column = 1; column <= Math.Min(sheet.UsedColumnCount, 50); column++)
                    {
                        string header = NormalizeHeader(sheet.Get(headerRow, column));
                        if (string.IsNullOrWhiteSpace(header))
                        {
                            continue;
                        }

                        score += TrySetItemColumn(columns, header, column);
                    }
                }

                if (score <= bestScore || columns.QuantityCol <= 0 || (columns.StyleNoCol <= 0 && columns.StyleNameCol <= 0))
                {
                    continue;
                }

                if (columns.StyleNameCol == 0 && columns.StyleNoCol > 0 && columns.QuantityCol > columns.StyleNoCol + 1)
                {
                    columns.StyleNameCol = columns.StyleNoCol + 1;
                }

                int dataStartRow = FindFirstItemDataRow(sheet, row + 1, columns);
                if (dataStartRow == 0)
                {
                    continue;
                }

                ExcelImportColumnValueInference.InferMissingColumns(
                    columns,
                    dataStartRow,
                    sheet.UsedColumnCount,
                    sheet.Get);

                bestScore = score;
                best = new ExcelImportItemTableAnalysis
                {
                    WorksheetName = sheet.Name,
                    HeaderRow = row,
                    HeaderDepth = 3,
                    DataStartRow = dataStartRow,
                    Confidence = ToDecimalConfidence(Math.Min(1.0, score / 12.0)),
                    Columns = columns
                };
            }

            return bestScore >= 3 ? best : null;
        }

        private static int TrySetItemColumn(ExcelImportItemColumnAnalysis columns, string header, int column)
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

            if (IsHeader(header, "英文品名", "英文名称", "品名", "名称", "货物英文品名", "货物名称", "货物描述", "商品名称", "商品描述", "产品名称", "产品描述", "物料名称", "物料描述", "零件名称", "零件描述", "部件名称", "部件描述", "品名规格", "规格描述", "stylename", "description", "desc", "name", "product", "productname", "productdescription", "goods", "goodsname", "goodsdescription", "itemname", "itemdescription", "descriptionofgoods", "commodity", "commodityname", "commoditydescription", "materialname", "materialdescription", "partname", "partdescription", "componentname", "componentdescription"))
            {
                columns.StyleNameCol = column;
                return 1;
            }

            if (IsHeader(header, "style") && columns.StyleNoCol == 0)
            {
                columns.StyleNoCol = column;
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

        private static int FindFirstItemDataRow(SheetGrid sheet, int startRow, ExcelImportItemColumnAnalysis columns)
        {
            for (int row = startRow; row <= Math.Min(startRow + 35, sheet.UsedRowCount); row++)
            {
                if (IsItemDataRow(sheet, row, columns))
                {
                    return row;
                }
            }

            return 0;
        }

        private static bool IsItemDataRow(SheetGrid sheet, int row, ExcelImportItemColumnAnalysis columns)
        {
            string quantity = GetCell(sheet, row, columns.QuantityCol);
            string styleNo = GetCell(sheet, row, columns.StyleNoCol);
            string styleName = GetCell(sheet, row, columns.StyleNameCol);

            if (string.IsNullOrWhiteSpace(quantity) || (!quantity.Any(char.IsDigit) && ParseExcelDecimal(quantity) == 0))
            {
                return false;
            }

            if (IsLikelyTotalRow(styleNo) || IsLikelyTotalRow(styleName))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(styleNo) || !string.IsNullOrWhiteSpace(styleName);
        }

        private static void PromotePartyBlocks(Dictionary<string, ExcelImportFieldAnalysis> fields)
        {
            PromoteAddressIfNameLooksLikeAddress(fields, "ExporterNameEN", "ExporterAddressEN");
            PromoteAddressIfNameLooksLikeAddress(fields, "CustomerNameEN", "CustomerAddressEN");
            PromoteAddressIfNameLooksLikeAddress(fields, "NotifyPartyName", "NotifyPartyAddress");
        }

        private static void PromoteAddressIfNameLooksLikeAddress(
            Dictionary<string, ExcelImportFieldAnalysis> fields,
            string nameKey,
            string addressKey)
        {
            if (!fields.TryGetValue(nameKey, out var name) || string.IsNullOrWhiteSpace(name.Value))
            {
                return;
            }

            if (fields.ContainsKey(addressKey))
            {
                return;
            }

            string normalized = NormalizeText(name.Value);
            if (!normalized.Contains("road", StringComparison.Ordinal)
                && !normalized.Contains("street", StringComparison.Ordinal)
                && !normalized.Contains("address", StringComparison.Ordinal)
                && !normalized.Contains("大道", StringComparison.Ordinal)
                && !normalized.Contains("路", StringComparison.Ordinal)
                && !normalized.Contains("号", StringComparison.Ordinal))
            {
                return;
            }

            fields[addressKey] = new ExcelImportFieldAnalysis
            {
                FieldKey = addressKey,
                DisplayName = addressKey,
                Value = name.Value,
                WorksheetName = name.WorksheetName,
                Row = name.Row,
                Column = name.Column,
                Confidence = Math.Max(0.35m, name.Confidence - 0.2m),
                Source = "PromotedAddress"
            };
        }

        private static ExcelImportFieldAnalysis PickBetter(
            ExcelImportFieldAnalysis current,
            ExcelImportFieldAnalysis candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Value))
            {
                return current;
            }

            if (current == null)
            {
                return candidate;
            }

            if (candidate.Confidence > current.Confidence)
            {
                return candidate;
            }

            if (candidate.Confidence == current.Confidence
                && candidate.Row > 0
                && (current.Row == 0 || candidate.Row < current.Row))
            {
                return candidate;
            }

            return current;
        }

        private static ExcelImportFieldAnalysis CreateField(
            FieldDefinition definition,
            string value,
            string worksheetName,
            int row,
            int column,
            decimal confidence,
            string source)
        {
            return new ExcelImportFieldAnalysis
            {
                FieldKey = definition.Key,
                DisplayName = definition.DisplayName,
                Value = NormalizeFieldValue(value),
                WorksheetName = worksheetName,
                Row = row,
                Column = column,
                Confidence = Math.Min(1m, Math.Max(0m, confidence)),
                Source = source
            };
        }

        private static (string Value, int Row, int Column) FindNearbyValue(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            candidates.AddRange(FindSameRowValueCandidates(sheet, row, column, multiLine));
            candidates.AddRange(FindBelowValueCandidates(sheet, row, column, multiLine));
            if (ShouldProbeBelowNeighborColumn(sheet, row, column))
            {
                candidates.AddRange(FindBelowValueCandidates(sheet, row, column + 1, multiLine));
            }

            var best = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .FirstOrDefault();

            return best == null
                ? (string.Empty, 0, 0)
                : (best.Value, best.Row, best.Column);
        }

        private static (string Value, int Row, int Column) FindBestBelowValue(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var best = FindBelowValueCandidates(sheet, row, column, multiLine)
                .Concat(ShouldProbeBelowNeighborColumn(sheet, row, column)
                    ? FindBelowValueCandidates(sheet, row, column + 1, multiLine)
                    : Enumerable.Empty<NearbyValueCandidate>())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .FirstOrDefault();

            return best == null
                ? (string.Empty, 0, 0)
                : (best.Value, best.Row, best.Column);
        }

        private static bool ShouldProbeBelowNeighborColumn(SheetGrid sheet, int row, int column)
        {
            if (column + 1 > sheet.UsedColumnCount)
            {
                return false;
            }

            string neighborHeader = sheet.Get(row, column + 1);
            return string.IsNullOrWhiteSpace(neighborHeader)
                || (!IsFieldBoundaryValue(neighborHeader) && !LooksLikeSequenceHeader(neighborHeader));
        }

        private static bool LooksLikeSequenceHeader(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "序号" or "编号" or "行号" or "no" or "number" or "serialno" or "serialnumber" or "itemno";
        }

        private static IReadOnlyList<NearbyValueCandidate> FindSameRowValueCandidates(
            SheetGrid sheet,
            int row,
            int labelColumn,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            int startColumn = labelColumn + 1;
            int maxColumn = Math.Min(startColumn + (multiLine ? 8 : 2), sheet.UsedColumnCount);
            for (int column = startColumn; column <= maxColumn; column++)
            {
                string value = sheet.Get(row, column).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (IsFieldBoundaryValue(value))
                {
                    break;
                }

                if (HasFieldBoundaryBetween(sheet, row, labelColumn + 1, column - 1))
                {
                    break;
                }

                string candidateValue = multiLine
                    ? CollectVerticalBlock(sheet, row, column, value)
                    : value;

                decimal score = 100m
                    - ((column - startColumn) * 4m)
                    + ScoreValueCompleteness(candidateValue, multiLine);

                candidates.Add(new NearbyValueCandidate(candidateValue, row, column, score));

                if (!multiLine)
                {
                    continue;
                }
            }

            return candidates;
        }

        private static IReadOnlyList<NearbyValueCandidate> FindBelowValueCandidates(
            SheetGrid sheet,
            int row,
            int column,
            bool multiLine)
        {
            var candidates = new List<NearbyValueCandidate>();
            int blankRows = 0;
            for (int nextRow = row + 1; nextRow <= Math.Min(row + 8, sheet.UsedRowCount); nextRow++)
            {
                string value = sheet.Get(nextRow, column).Trim();
                int valueColumn = column;
                if (string.IsNullOrWhiteSpace(value) && column + 1 <= sheet.UsedColumnCount)
                {
                    value = sheet.Get(nextRow, column + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        valueColumn = column + 1;
                    }
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    blankRows++;
                    if (blankRows >= 2)
                    {
                        break;
                    }

                    continue;
                }

                if (IsFieldBoundaryValue(value) || HasKnownLabelBeforeColumn(sheet, nextRow, valueColumn))
                {
                    break;
                }

                string candidateValue = multiLine
                    ? CollectVerticalBlock(sheet, nextRow, valueColumn, value)
                    : value;

                decimal score = 88m
                    - ((nextRow - row - 1) * 6m)
                    - ((valueColumn - column) * 2m)
                    + ScoreValueCompleteness(candidateValue, multiLine);

                candidates.Add(new NearbyValueCandidate(candidateValue, nextRow, valueColumn, score));

                if (!multiLine)
                {
                    continue;
                }
            }

            return candidates;
        }

        private static bool HasFieldBoundaryBetween(SheetGrid sheet, int row, int startColumn, int endColumn)
        {
            for (int column = Math.Max(1, startColumn); column <= endColumn; column++)
            {
                if (IsFieldBoundaryValue(sheet.Get(row, column)))
                {
                    return true;
                }
            }

            return false;
        }

        private static decimal ScoreValueCompleteness(string value, bool multiLine)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0m;
            }

            if (!multiLine)
            {
                return value.Length >= 3 ? 2m : 0m;
            }

            int lineCount = value
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));

            return Math.Min(6m, lineCount * 1.5m);
        }

        private static string CollectVerticalBlock(SheetGrid sheet, int startRow, int column, string firstValue)
        {
            var lines = new List<string>();
            AddBlockLine(lines, firstValue);

            for (int row = startRow + 1; row <= Math.Min(startRow + 12, sheet.UsedRowCount); row++)
            {
                string value = sheet.Get(row, column).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    break;
                }

                if (IsFieldBoundaryValue(value))
                {
                    break;
                }

                AddBlockLine(lines, value);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static void AddBlockLine(List<string> lines, string value)
        {
            string normalized = NormalizeFieldValue(value);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !lines.Any(line => string.Equals(line, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(normalized);
            }
        }

        private static string ExtractInlineValue(string value, IReadOnlyList<string> labels)
        {
            foreach (string label in labels)
            {
                if (TryGetInlineTextAfterLabel(value, label, out string afterLabel))
                {
                    string extracted = afterLabel.TrimStart(' ', '\t', ':', '：', '#').Trim();
                    if (!string.IsNullOrWhiteSpace(extracted) && !LooksLikeKnownLabel(extracted))
                    {
                        return extracted;
                    }
                }
            }

            return string.Empty;
        }

        private static LabelMatch? MatchLabel(string value, IReadOnlyList<string> labels)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            foreach (string label in labels)
            {
                string normalizedLabel = NormalizeText(label);
                if (normalized == normalizedLabel)
                {
                    return new LabelMatch(0.9m, "LabelExact");
                }

                if (normalizedLabel.Length >= 4
                    && normalized.StartsWith(normalizedLabel, StringComparison.Ordinal)
                    && normalized.Length <= normalizedLabel.Length + 16)
                {
                    if (!TryGetInlineTextAfterLabel(value, label, out _))
                    {
                        continue;
                    }

                    if (LooksLikeCodeValue(value))
                    {
                        continue;
                    }

                    return new LabelMatch(0.82m, "LabelPrefix");
                }

                if (normalized.Contains(normalizedLabel, StringComparison.Ordinal)
                    && normalizedLabel.Length >= 3
                    && normalized.Length <= Math.Max(12, normalizedLabel.Length * 3))
                {
                    return new LabelMatch(0.72m, "LabelContains");
                }
            }

            return null;
        }

        private static bool TryGetInlineTextAfterLabel(string value, string label, out string afterLabel)
        {
            afterLabel = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            var pattern = Regex.Escape(label).Replace("\\ ", "\\s*");
            var match = Regex.Match(
                value,
                $@"^\s*{pattern}(?<after>.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            afterLabel = match.Groups["after"].Value;
            if (string.IsNullOrWhiteSpace(afterLabel))
            {
                return false;
            }

            char first = afterLabel[0];
            if (first is ':' or '：' or '#')
            {
                return true;
            }

            return char.IsWhiteSpace(first) && !IsSingleWordAsciiLabel(label);
        }

        private static bool IsSingleWordAsciiLabel(string label)
        {
            return label.All(c => c <= '\u007f')
                && !label.Any(char.IsWhiteSpace)
                && !label.Contains('/', StringComparison.Ordinal);
        }

        private static bool IsAddressLabelForDifferentField(string value, FieldDefinition definition)
        {
            if (definition.Key.Contains("Address", StringComparison.Ordinal))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            return normalized.Contains("address", StringComparison.Ordinal)
                || normalized.Contains("地址", StringComparison.Ordinal);
        }

        private static bool LooksLikeCodeValue(string value)
        {
            return value.Contains('-', StringComparison.Ordinal)
                && value.Any(char.IsDigit)
                && !value.Contains(':', StringComparison.Ordinal)
                && !value.Contains('：', StringComparison.Ordinal);
        }

        private static bool IsGenericPlaceholderValue(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "name" or "address" or "名称" or "地址" or "shipper" or "exporter" or "consignee" or "customer";
        }

        private static bool LooksLikeKnownLabel(string value)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return FieldDefinitions
                .SelectMany(definition => definition.Labels)
                .Any(label => normalized == NormalizeText(label) || TryGetInlineTextAfterLabel(value, label, out _));
        }

        private static bool LooksLikeItemHeader(string value)
        {
            var columns = new ExcelImportItemColumnAnalysis();
            return TrySetItemColumn(columns, NormalizeHeader(value), 1) > 0;
        }

        private static bool IsFieldBoundaryValue(string value)
        {
            return LooksLikeKnownLabel(value) || LooksLikeItemHeader(value);
        }

        private static bool HasKnownLabelBeforeColumn(SheetGrid sheet, int row, int column)
        {
            for (int previousColumn = Math.Max(1, column - 3); previousColumn < column; previousColumn++)
            {
                if (IsFieldBoundaryValue(sheet.Get(row, previousColumn)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLikelyTotalRow(string value)
        {
            string normalized = NormalizeText(value);
            return normalized is "合计" or "总计" or "total" or "subtotal";
        }

        private static int CountPresent(Dictionary<string, ExcelImportFieldAnalysis> fields, params string[] keys)
        {
            return keys.Count(key => fields.TryGetValue(key, out var field) && !string.IsNullOrWhiteSpace(field.Value));
        }

        private static void AddCompletenessIssues(ExcelImportAnalysisReport report)
        {
            if (report == null)
            {
                return;
            }

            AddMissingFieldIssue(report, "InvoiceNo", "未能高置信度识别发票号。");
            AddMissingFieldIssue(report, "CustomerNameEN", "未能高置信度识别收货人。");
            AddMissingFieldIssue(report, "ExporterNameEN", "未能高置信度识别出口商/SHIPPER。");
            AddMissingFieldIssue(report, "PortOfLoading", "未能高置信度识别起运港。");
            AddMissingFieldIssue(report, "PortOfDestination", "未能高置信度识别目的港/目的地。");

            if (report.ItemTable == null)
            {
                report.Issues.Add(new ExcelImportAnalysisIssue
                {
                    Severity = "Warning",
                    Code = "MissingItemTable",
                    Message = "未识别到商品明细表头，将回退到当前 Excel 导入方案的固定行列配置。"
                });
            }
        }

        private static void AddMissingFieldIssue(ExcelImportAnalysisReport report, string fieldKey, string message)
        {
            var field = report.Fields.FirstOrDefault(item => string.Equals(item.FieldKey, fieldKey, StringComparison.Ordinal));
            if (field != null && !string.IsNullOrWhiteSpace(field.Value) && field.Confidence >= 0.65m)
            {
                return;
            }

            report.Issues.Add(new ExcelImportAnalysisIssue
            {
                Severity = "Warning",
                Code = "LowConfidenceField",
                FieldKey = fieldKey,
                Message = message
            });
        }

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

        private static string GetConfiguredCellReference(string fieldKey, ExcelImportSettings settings)
        {
            return fieldKey switch
            {
                "ExporterNameCN" => settings.ExporterNameCNCell,
                "ExporterNameEN" => settings.ExporterNameCell,
                "ExporterAddressEN" => settings.ExporterAddressStartCell,
                "CustomerNameEN" => settings.CustomerNameCell,
                "CustomerAddressEN" => settings.CustomerAddressStartCell,
                "NotifyPartyName" => settings.NotifyPartyNameCell,
                "NotifyPartyAddress" => settings.NotifyPartyAddressStartCell,
                "InvoiceNo" => settings.InvoiceNoCell,
                "ContractNo" => settings.ContractNoCell,
                "InvoiceDate" => settings.InvoiceDateCell,
                "PortOfLoading" => settings.PortOfLoadingCell,
                "PortOfDestination" => settings.PortOfDestinationCell,
                "DestinationCountry" => settings.DestinationCountryCell,
                "TradeTerms" => settings.TradeTermsCell,
                "TransportMode" => settings.TransportModeCell,
                "PaymentTerms" => settings.PaymentTermsCell,
                "Currency" => settings.CurrencyCell,
                "SupervisionMode" => settings.SupervisionModeCell,
                "LetterOfCreditNo" => settings.LetterOfCreditNoCell,
                "IssuingBank" => settings.IssuingBankCell,
                "ShippingMarks" => settings.ShippingMarksCell,
                _ => string.Empty
            };
        }

        private static string GetCell(SheetGrid sheet, int row, int column)
        {
            return column <= 0 ? string.Empty : sheet.Get(row, column);
        }

        private static string NormalizeFieldValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lines = value
                .Replace('\u00a0', ' ')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None)
                .SelectMany(line => Regex.Split(line, @"[ \t]{4,}"))
                .Select(line => Regex.Replace(line.Trim(), @"[ \t]{2,}", " "))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return string.Join(Environment.NewLine, lines);
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

        private static bool IsHeader(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value == NormalizeHeader(candidate));
        }

        private static string NormalizeHeader(string value)
        {
            return NormalizeText(value);
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || IsCjk(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static bool IsCjk(char value)
        {
            return value >= '\u4e00' && value <= '\u9fff';
        }

        private static decimal ToDecimalConfidence(double value)
        {
            return Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
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

        private sealed record FieldDefinition(
            string Key,
            string DisplayName,
            string[] Labels,
            bool MultiLine = false,
            bool PreferBelow = false);

        private readonly record struct LabelMatch(decimal Score, string Kind);

        private sealed record NearbyValueCandidate(string Value, int Row, int Column, decimal Score);

        private sealed record SheetProfile(
            SheetGrid Sheet,
            Dictionary<string, ExcelImportFieldAnalysis> Fields,
            ExcelImportItemTableAnalysis ItemTable,
            double Score,
            double Confidence)
        {
            public int SheetIndex { get; init; }
        }

        private sealed class SheetGrid
        {
            private readonly IReadOnlyList<IReadOnlyList<string>> _rows;

            public SheetGrid(string name, IReadOnlyList<List<string>> rows)
            {
                Name = name ?? string.Empty;
                _rows = rows.Select(row => (IReadOnlyList<string>)row).ToList();

                for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
                {
                    var row = _rows[rowIndex];
                    for (int columnIndex = 0; columnIndex < row.Count; columnIndex++)
                    {
                        if (string.IsNullOrWhiteSpace(row[columnIndex]))
                        {
                            continue;
                        }

                        UsedRowCount = rowIndex + 1;
                        UsedColumnCount = Math.Max(UsedColumnCount, columnIndex + 1);
                    }
                }
            }

            public string Name { get; }

            public int UsedRowCount { get; }

            public int UsedColumnCount { get; }

            public string Get(int oneBasedRow, int oneBasedColumn)
            {
                if (oneBasedRow <= 0 || oneBasedColumn <= 0 || oneBasedRow > _rows.Count)
                {
                    return string.Empty;
                }

                var row = _rows[oneBasedRow - 1];
                if (oneBasedColumn > row.Count)
                {
                    return string.Empty;
                }

                return row[oneBasedColumn - 1] ?? string.Empty;
            }
        }
    }
}
