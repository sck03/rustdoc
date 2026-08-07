using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Data
{
    public sealed partial class BuiltInExcelImportAnalyzer : IExcelImportAnalyzer
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

        public async Task<ExcelImportAnalysisReport> AnalyzeAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default)
        {
            using IDisposable lease = await ExcelAnalysisExecutionGate.EnterAsync(cancellationToken)
                .ConfigureAwait(false);
            return await AnalyzeWithoutExecutionGateAsync(filePath, settings, cancellationToken)
                .ConfigureAwait(false);
        }

        internal Task<ExcelImportAnalysisReport> AnalyzeWithoutExecutionGateAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(
                    () => AnalyzeInternal(filePath, settings ?? new ExcelImportSettings(), cancellationToken),
                    cancellationToken);
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
                if (string.IsNullOrWhiteSpace(value) || !IsUsableFieldValue(definition.Key, value))
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

                    if (!IsUsableFieldValue(definition.Key, value))
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

    }
}
