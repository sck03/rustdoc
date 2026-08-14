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
        private static DetectedItemTableLayout? GetDetectedLayoutFromAnalysis(ExcelImportAnalysisReport analysisReport)
        {
            var table = analysisReport?.ItemTable;
            if (table == null || table.DataStartRow <= 0 || table.Columns == null)
            {
                return null;
            }

            if (table.Columns.QuantityCol <= 0
                || (table.Columns.StyleNoCol <= 0 && table.Columns.StyleNameCol <= 0))
            {
                return null;
            }

            return new DetectedItemTableLayout
            {
                HeaderRow = table.HeaderRow,
                DataStartRow = table.DataStartRow,
                Columns = new DetectedItemColumns
                {
                    PoNumberCol = table.Columns.PoNumberCol,
                    StyleNoCol = table.Columns.StyleNoCol,
                    StyleNameCol = table.Columns.StyleNameCol,
                    FabricCompositionCol = table.Columns.FabricCompositionCol,
                    StyleNameCNCol = table.Columns.StyleNameCNCol,
                    BrandCol = table.Columns.BrandCol,
                    HSCodeCol = table.Columns.HSCodeCol,
                    OriginCol = table.Columns.OriginCol,
                    QuantityCol = table.Columns.QuantityCol,
                    UnitENCol = table.Columns.UnitENCol,
                    UnitCNCol = table.Columns.UnitCNCol,
                    CartonsCol = table.Columns.CartonsCol,
                    CtnUnitENCol = table.Columns.CtnUnitENCol,
                    LengthCol = table.Columns.LengthCol,
                    WidthCol = table.Columns.WidthCol,
                    HeightCol = table.Columns.HeightCol,
                    DimensionCol = table.Columns.DimensionCol,
                    VolumeCol = table.Columns.VolumeCol,
                    GWPerCtnCol = table.Columns.GWPerCtnCol,
                    GWTotalCol = table.Columns.GWTotalCol,
                    NWPerCtnCol = table.Columns.NWPerCtnCol,
                    NWTotalCol = table.Columns.NWTotalCol,
                    UnitPriceCol = table.Columns.UnitPriceCol,
                    TotalPriceCol = table.Columns.TotalPriceCol
                }
            };
        }

        private void InferMissingColumnsFromValues(IExcelImportWorksheet worksheet, DetectedItemTableLayout layout)
        {
            var columns = ToAnalysisColumns(layout.Columns);
            ExcelImportColumnValueInference.InferMissingColumns(
                columns,
                layout.DataStartRow,
                60,
                (row, column) => GetCellValue(worksheet.Cell(row, column)));
            ApplyAnalysisColumns(layout.Columns, columns);
        }

        private void RepairDetectedLayoutFromWorksheetHeaders(
            IExcelImportWorksheet worksheet,
            DetectedItemTableLayout layout)
        {
            if (worksheet == null || layout?.Columns == null || layout.HeaderRow <= 0)
            {
                return;
            }

            bool hasExplicitBrandHeader = false;
            int headerDepth = Math.Max(1, Math.Min(3, layout.DataStartRow - layout.HeaderRow));
            for (int column = 1; column <= 30; column++)
            {
                var headers = GetHeaderPath(worksheet, layout.HeaderRow, headerDepth, column);
                if (headers.Count == 0)
                {
                    continue;
                }

                if (HeaderPathContains(headers, "客人订单号", "客户订单号", "订单号", "采购订单号", "销售订单号", "po number", "po no", "pono", "ponumber", "po", "po#", "purchaseorder", "orderno"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.PoNumber, column);
                }

                if (HeaderPathContains(headers, "客人款号", "款号", "货号", "产品编号", "styleno", "style no", "style no.", "style code", "sku"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.StyleNo, column);
                }

                if (HeaderPathContains(headers, "英文品名", "英文名称", "款名", "货物英文品名", "货物名称", "商品名称", "产品名称", "stylename", "description", "product description"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.StyleName, column);
                }

                if (HeaderPathContains(headers, "面料", "面料成分", "成份", "成分", "材质", "fabric", "composition", "material"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.FabricComposition, column);
                }

                if (HeaderPathContains(headers, "中文品名", "中文名称", "品名中文", "款式描述", "中文描述", "报关品名", "货物中文名称"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.StyleNameCN, column);
                }

                if (HeaderPathContains(headers, "品牌", "品牌名", "商标", "brand", "label"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.Brand, column);
                    hasExplicitBrandHeader = true;
                }

                if (HeaderPathContains(headers, "数量", "总数量", "quantity", "qty", "pcs"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.Quantity, column);
                }

                if (HeaderPathContains(headers, "箱子尺寸", "箱规", "外箱尺寸", "包装尺寸", "尺寸", "长宽高", "carton size", "ctn size", "cartonsize", "dimension", "dimensions"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.Dimension, column);
                }
                else if (HeaderPathContains(headers, "箱数", "总箱数", "箱量", "包装件数", "carton", "cartons", "ctns", "ctn"))
                {
                    SetExplicitItemColumn(layout.Columns, ItemColumnKind.Cartons, column);
                }
            }

            if (!hasExplicitBrandHeader)
            {
                layout.Columns.BrandCol = 0;
            }
        }

        private List<string> GetHeaderPath(
            IExcelImportWorksheet worksheet,
            int headerRow,
            int headerDepth,
            int column)
        {
            var headers = new List<string>();
            for (int row = headerRow; row < headerRow + headerDepth; row++)
            {
                string value = GetCellValue(worksheet.Cell(row, column));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    headers.Add(value);
                }
            }

            return headers;
        }

        private static bool HeaderPathContains(IReadOnlyList<string> headers, params string[] aliases)
        {
            return headers.Any(header => IsHeader(NormalizeHeader(header), aliases));
        }

        private static void SetExplicitItemColumn(
            DetectedItemColumns columns,
            ItemColumnKind kind,
            int column)
        {
            ClearItemColumnAssignments(columns, column);
            switch (kind)
            {
                case ItemColumnKind.PoNumber:
                    columns.PoNumberCol = column;
                    break;
                case ItemColumnKind.StyleNo:
                    columns.StyleNoCol = column;
                    break;
                case ItemColumnKind.StyleName:
                    columns.StyleNameCol = column;
                    break;
                case ItemColumnKind.FabricComposition:
                    columns.FabricCompositionCol = column;
                    break;
                case ItemColumnKind.StyleNameCN:
                    columns.StyleNameCNCol = column;
                    break;
                case ItemColumnKind.Brand:
                    columns.BrandCol = column;
                    break;
                case ItemColumnKind.Quantity:
                    columns.QuantityCol = column;
                    break;
                case ItemColumnKind.Cartons:
                    columns.CartonsCol = column;
                    break;
                case ItemColumnKind.Dimension:
                    columns.DimensionCol = column;
                    break;
            }
        }

        private static void ClearItemColumnAssignments(DetectedItemColumns columns, int column)
        {
            if (column <= 0)
            {
                return;
            }

            if (columns.PoNumberCol == column) columns.PoNumberCol = 0;
            if (columns.StyleNoCol == column) columns.StyleNoCol = 0;
            if (columns.StyleNameCol == column) columns.StyleNameCol = 0;
            if (columns.FabricCompositionCol == column) columns.FabricCompositionCol = 0;
            if (columns.StyleNameCNCol == column) columns.StyleNameCNCol = 0;
            if (columns.BrandCol == column) columns.BrandCol = 0;
            if (columns.HSCodeCol == column) columns.HSCodeCol = 0;
            if (columns.OriginCol == column) columns.OriginCol = 0;
            if (columns.QuantityCol == column) columns.QuantityCol = 0;
            if (columns.UnitENCol == column) columns.UnitENCol = 0;
            if (columns.UnitCNCol == column) columns.UnitCNCol = 0;
            if (columns.CartonsCol == column) columns.CartonsCol = 0;
            if (columns.CtnUnitENCol == column) columns.CtnUnitENCol = 0;
            if (columns.LengthCol == column) columns.LengthCol = 0;
            if (columns.WidthCol == column) columns.WidthCol = 0;
            if (columns.HeightCol == column) columns.HeightCol = 0;
            if (columns.DimensionCol == column) columns.DimensionCol = 0;
            if (columns.VolumeCol == column) columns.VolumeCol = 0;
            if (columns.GWPerCtnCol == column) columns.GWPerCtnCol = 0;
            if (columns.GWTotalCol == column) columns.GWTotalCol = 0;
            if (columns.NWPerCtnCol == column) columns.NWPerCtnCol = 0;
            if (columns.NWTotalCol == column) columns.NWTotalCol = 0;
            if (columns.UnitPriceCol == column) columns.UnitPriceCol = 0;
            if (columns.TotalPriceCol == column) columns.TotalPriceCol = 0;
        }

        private static void ApplyDetectedLayoutToAnalysisReport(
            ExcelImportAnalysisReport analysisReport,
            string worksheetName,
            DetectedItemTableLayout layout)
        {
            if (analysisReport == null || layout?.Columns == null)
            {
                return;
            }

            analysisReport.ItemTable ??= new ExcelImportItemTableAnalysis
            {
                WorksheetName = worksheetName ?? string.Empty,
                HeaderRow = layout.HeaderRow,
                HeaderDepth = 3,
                DataStartRow = layout.DataStartRow,
                Confidence = 0.65m
            };

            analysisReport.ItemTable.Columns = ToAnalysisColumns(layout.Columns);
        }

        private static ExcelImportItemColumnAnalysis ToAnalysisColumns(DetectedItemColumns columns)
        {
            return new ExcelImportItemColumnAnalysis
            {
                PoNumberCol = columns.PoNumberCol,
                StyleNoCol = columns.StyleNoCol,
                StyleNameCol = columns.StyleNameCol,
                FabricCompositionCol = columns.FabricCompositionCol,
                StyleNameCNCol = columns.StyleNameCNCol,
                BrandCol = columns.BrandCol,
                HSCodeCol = columns.HSCodeCol,
                OriginCol = columns.OriginCol,
                QuantityCol = columns.QuantityCol,
                UnitENCol = columns.UnitENCol,
                UnitCNCol = columns.UnitCNCol,
                CartonsCol = columns.CartonsCol,
                CtnUnitENCol = columns.CtnUnitENCol,
                LengthCol = columns.LengthCol,
                WidthCol = columns.WidthCol,
                HeightCol = columns.HeightCol,
                DimensionCol = columns.DimensionCol,
                VolumeCol = columns.VolumeCol,
                GWPerCtnCol = columns.GWPerCtnCol,
                GWTotalCol = columns.GWTotalCol,
                NWPerCtnCol = columns.NWPerCtnCol,
                NWTotalCol = columns.NWTotalCol,
                UnitPriceCol = columns.UnitPriceCol,
                TotalPriceCol = columns.TotalPriceCol
            };
        }

        private static void ApplyAnalysisColumns(DetectedItemColumns target, ExcelImportItemColumnAnalysis source)
        {
            target.PoNumberCol = source.PoNumberCol;
            target.StyleNoCol = source.StyleNoCol;
            target.StyleNameCol = source.StyleNameCol;
            target.FabricCompositionCol = source.FabricCompositionCol;
            target.StyleNameCNCol = source.StyleNameCNCol;
            target.BrandCol = source.BrandCol;
            target.HSCodeCol = source.HSCodeCol;
            target.OriginCol = source.OriginCol;
            target.QuantityCol = source.QuantityCol;
            target.UnitENCol = source.UnitENCol;
            target.UnitCNCol = source.UnitCNCol;
            target.CartonsCol = source.CartonsCol;
            target.CtnUnitENCol = source.CtnUnitENCol;
            target.LengthCol = source.LengthCol;
            target.WidthCol = source.WidthCol;
            target.HeightCol = source.HeightCol;
            target.DimensionCol = source.DimensionCol;
            target.VolumeCol = source.VolumeCol;
            target.GWPerCtnCol = source.GWPerCtnCol;
            target.GWTotalCol = source.GWTotalCol;
            target.NWPerCtnCol = source.NWPerCtnCol;
            target.NWTotalCol = source.NWTotalCol;
            target.UnitPriceCol = source.UnitPriceCol;
            target.TotalPriceCol = source.TotalPriceCol;
        }


        private sealed class DetectedItemTableLayout
        {
            public int HeaderRow { get; set; }

            public int DataStartRow { get; set; }

            public DetectedItemColumns Columns { get; set; } = new();
        }

        private enum ItemColumnKind
        {
            PoNumber,
            StyleNo,
            StyleName,
            FabricComposition,
            StyleNameCN,
            Brand,
            Quantity,
            Cartons,
            Dimension
        }

        private sealed class BookingSheetGoodsTable
        {
            public int HeaderRow { get; set; }

            public int DataStartRow { get; set; }

            public int MarksCol { get; set; }

            public int CartonsCol { get; set; }

            public int DescriptionCol { get; set; }

            public int GrossWeightCol { get; set; }

            public int MeasurementCol { get; set; }
        }

        private sealed class DetectedItemColumns
        {
            public int PoNumberCol { get; set; }

            public int StyleNoCol { get; set; }

            public int StyleNameCol { get; set; }

            public int FabricCompositionCol { get; set; }

            public int StyleNameCNCol { get; set; }

            public int BrandCol { get; set; }

            public int HSCodeCol { get; set; }

            public int OriginCol { get; set; }

            public int QuantityCol { get; set; }

            public int UnitENCol { get; set; }

            public int UnitCNCol { get; set; }

            public int CartonsCol { get; set; }

            public int CtnUnitENCol { get; set; }

            public int LengthCol { get; set; }

            public int WidthCol { get; set; }

            public int HeightCol { get; set; }

            public int DimensionCol { get; set; }

            public int VolumeCol { get; set; }

            public int GWPerCtnCol { get; set; }

            public int GWTotalCol { get; set; }

            public int NWPerCtnCol { get; set; }

            public int NWTotalCol { get; set; }

            public int UnitPriceCol { get; set; }

            public int TotalPriceCol { get; set; }
        }
    }
}
