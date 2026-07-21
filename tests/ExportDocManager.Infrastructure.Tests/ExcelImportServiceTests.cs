using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ExportDocManager.Infrastructure.Tests
{
    public class ExcelImportServiceTests
    {
        [Fact]
        public async Task ImportFromExcelAsync_ShouldHonorCancellationToken()
        {
            var service = new ExcelImportService(new StubSettingsService());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ImportFromExcelAsync("missing.xlsx", cancellation.Token));
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldReadLegacyXlsWorkbook()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "invoice.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteLegacyXlsWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("INV-XLS-001", result.Invoice.InvoiceNo);
                Assert.Equal("CONTRACT-XLS-001", result.Invoice.ContractNo);
                Assert.Equal("TEST CUSTOMER LTD.", result.Invoice.CustomerNameEN);
                Assert.Equal("NINGBO TEST EXPORT CO., LTD.", result.Invoice.ExporterNameEN);

                var item = Assert.Single(result.Invoice.Items);
                Assert.Equal("ST-XLS-001", item.StyleNo);
                Assert.Equal("T SHIRT", item.StyleName);
                Assert.Equal("61091000", item.HSCode);
                Assert.Equal(120m, item.Quantity);
                Assert.Equal(2.5m, item.UnitPrice);
                Assert.Equal(300m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldRejectAmountAsDestinationCountryAndRepairSwappedItemNames()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "invoice-country-and-item-language.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteDestinationCountryRegressionWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("AUSTRALIA", result.Invoice.DestinationCountry);
                var item = Assert.Single(result.Invoice.Items);
                Assert.Equal("bottle opener tee", item.StyleName);
                Assert.Equal("男式棉制针织圆领衫", item.StyleNameCN);
                Assert.Equal(5797.62m, item.TotalPrice);
                Assert.DoesNotContain(result.AnalysisReport.Fields, field => field.FieldKey == "DestinationCountry");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldReadOpenXmlXlsxWorkbook()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportFormatTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "invoice.xlsx");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteOpenXmlXlsxWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport);
                Assert.Equal("builtin-dotnet", result.AnalysisReport.AnalyzerId);
                Assert.Equal("OpenXML导入", result.AnalysisReport.SelectedWorksheetName);
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "InvoiceNo" && field.Value == "INV-XLSX-001");
                Assert.NotNull(result.AnalysisReport.ItemTable);
                Assert.Equal(9, result.AnalysisReport.ItemTable.DataStartRow);
                Assert.Equal(1, result.AnalysisReport.ItemTable.Columns.StyleNoCol);
                Assert.Equal(3, result.AnalysisReport.ItemTable.Columns.QuantityCol);
                Assert.Equal(5, result.AnalysisReport.ItemTable.Columns.DimensionCol);
                Assert.Equal(6, result.AnalysisReport.ItemTable.Columns.VolumeCol);
                Assert.Equal(13, result.AnalysisReport.ItemTable.Columns.HSCodeCol);

                Assert.Equal("INV-XLSX-001", result.Invoice.InvoiceNo);
                Assert.Equal("CONTRACT-XLSX-001", result.Invoice.ContractNo);
                Assert.Equal("OPENXML BUYER LTD.", result.Invoice.CustomerNameEN);
                Assert.Equal("NINGBO XLSX EXPORT CO., LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("HAMBURG", result.Invoice.PortOfDestination);
                Assert.Equal("FOB SHANGHAI", result.Invoice.TradeTerms);

                Assert.Equal(2, result.Invoice.Items.Count);
                var item = result.Invoice.Items[0];
                Assert.Equal("XLSX-TEE-001", item.StyleNo);
                Assert.Equal("OPENXML T SHIRT", item.StyleName);
                Assert.Equal("6109100021", item.HSCode);
                Assert.Equal("宁波", item.Origin);
                Assert.Equal(120m, item.Quantity);
                Assert.Equal(12m, item.Cartons);
                Assert.Equal(50m, item.Length);
                Assert.Equal(40m, item.Width);
                Assert.Equal(30m, item.Height);
                Assert.Equal(0.72m, item.Volume);
                Assert.Equal(8.5m, item.GWPerCtn);
                Assert.Equal(102m, item.GWTotal);
                Assert.Equal(7.5m, item.NWPerCtn);
                Assert.Equal(90m, item.NWTotal);
                Assert.Equal(3.2m, item.UnitPrice);
                Assert.Equal(384m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldCorrectStaleAnalyzerPartyLabelsAndKeepExplicitContractNo()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportFormatTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "default-template-party-regression.xlsx");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteDefaultTemplatePartyRegressionWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings, new StalePartyFieldAnalyzer());
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("2024AA001", result.Invoice.InvoiceNo);
                Assert.Equal("2024AA001", result.Invoice.ContractNo);
                Assert.Equal("ONIA LLC.", result.Invoice.CustomerNameEN);
                Assert.Equal("10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA", result.Invoice.CustomerAddressEN);
                Assert.Equal("ONIA LLC.", result.Invoice.NotifyPartyName);
                Assert.Equal("10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA", result.Invoice.NotifyPartyAddress);
                Assert.Equal("NINGBO BRIDGE IMP. & EXP. CO., LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA", result.Invoice.ExporterAddressEN);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldDetectSingleValueFieldsBelowLabels()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportFormatTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "below-label-fields.xlsx");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteBelowLabelFieldsWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport);
                Assert.Equal("builtin-dotnet", result.AnalysisReport.AnalyzerId);
                Assert.Contains(result.AnalysisReport.Fields, field =>
                    field.FieldKey == "InvoiceNo"
                    && field.Value == "INV-BELOW-001"
                    && field.Row == 2
                    && field.Column == 5);
                Assert.Contains(result.AnalysisReport.Fields, field =>
                    field.FieldKey == "CustomerNameEN"
                    && field.Value == "BELOW LABEL BUYER LLC"
                    && field.Row == 2
                    && field.Column == 3);

                Assert.Equal("INV-BELOW-001", result.Invoice.InvoiceNo);
                Assert.Equal("BELOW LABEL BUYER LLC", result.Invoice.CustomerNameEN);
                Assert.Equal("NINGBO BELOW EXPORT CO., LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("NINGBO", result.Invoice.PortOfLoading);
                Assert.Equal("ROTTERDAM", result.Invoice.PortOfDestination);
                Assert.Equal("FOB NINGBO", result.Invoice.TradeTerms);

                var item = Assert.Single(result.Invoice.Items);
                Assert.Equal("BL-TEE-001", item.StyleNo);
                Assert.Equal("BELOW LABEL TEE", item.StyleName);
                Assert.Equal(100m, item.Quantity);
                Assert.Equal(250m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldDetectShippingAdviceTableLayout()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "shipping-advice.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteShippingAdviceWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport);
                Assert.Equal("builtin-dotnet", result.AnalysisReport.AnalyzerId);
                Assert.Equal("报关和清关", result.AnalysisReport.SelectedWorksheetName);
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "InvoiceNo" && field.Value == "2026YH013");
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "ExporterNameEN" && field.Value.Contains("NINGBO BRIDGE"));
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "CustomerNameEN" && field.Value == "RDP Ltd");
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "PortOfLoading" && field.Value == "ningbo");
                Assert.Contains(result.AnalysisReport.Fields, field => field.FieldKey == "PortOfDestination" && field.Value == "hongkong");
                Assert.NotNull(result.AnalysisReport.ItemTable);
                Assert.Equal(19, result.AnalysisReport.ItemTable.DataStartRow);
                Assert.Equal("2026YH013", result.Invoice.InvoiceNo);
                Assert.Equal("RDP Ltd", result.Invoice.CustomerNameEN);
                Assert.Equal("DDP Hongkong", result.Invoice.TradeTerms);
                Assert.Equal("hongkong", result.Invoice.PortOfDestination);

                var item = Assert.Single(result.Invoice.Items);
                Assert.Equal("633133", item.PoNumber);
                Assert.Equal("116094-116097", item.StyleNo);
                Assert.Equal("toy story 5 kids hoodie", item.StyleName);
                Assert.Equal("棉制针织男童戴帽衫", item.StyleNameCN);
                Assert.Equal(611m, item.Quantity);
                Assert.Equal(30m, item.Cartons);
                Assert.Equal(30m, item.Length);
                Assert.Equal(28m, item.Width);
                Assert.Equal(30m, item.Height);
                Assert.Equal(0.756m, item.Volume);
                Assert.Equal(315m, item.GWTotal);
                Assert.Equal(264m, item.NWTotal);
                Assert.Equal(7701.45m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldDetectMultiRowBookingSheetLayout()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "booking-sheet.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteMultiRowBookingSheetWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("2025ATX004", result.Invoice.InvoiceNo);
                Assert.Equal("2024ATX-2,3,7,8", result.Invoice.ContractNo);
                Assert.Equal("T/T", result.Invoice.PaymentTerms);
                Assert.Equal("DDP LA ( OR LB)", result.Invoice.TradeTerms);
                Assert.Equal("LOS ANGELES ( OR LONG BEACH), USA", result.Invoice.PortOfDestination);
                Assert.Equal("FAME FASHION HOUSE LLC", result.Invoice.CustomerNameEN);
                Assert.Equal(
                    string.Join(Environment.NewLine, "1735 Jersey Ave, North Brunswick NJ 08902 ，UNITED STATES OF AMERICA", "Tel# 212-287-9023", "sallyyang@jnmworldwide.com&imports@tfxny.com"),
                    result.Invoice.CustomerAddressEN);
                Assert.Equal("NINGBO BRIDGE IMP. & EXP. CO. LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("宁波布利杰进出口有限公司", result.Invoice.ExporterNameCN);
                Assert.Equal(
                    string.Join(Environment.NewLine, "NO.668 BAIZHANG EAST ROAD.", "NINGBO 315040 CHINA"),
                    result.Invoice.ExporterAddressEN);
                Assert.Equal(
                    string.Join(Environment.NewLine, "SHIP TO: ROSS STORES, INC", "3404 INDIAN AVENUE", "PERRIS, CA 92572", "FROM: FAME FASHION HOUSE"),
                    result.Invoice.ShippingMarks);

                Assert.Equal(3, result.Invoice.Items.Count);
                var item = result.Invoice.Items[0];
                Assert.Equal("无门襟无扣的T恤衫， HS.6109100021", item.StyleName);
                Assert.Equal(26712m, item.Quantity);
                Assert.Equal(1113m, item.Cartons);
                Assert.Equal(5.4m, item.GWPerCtn);
                Assert.Equal(6010.2m, item.GWTotal);
                Assert.Equal(4.4m, item.NWPerCtn);
                Assert.Equal(4897.2m, item.NWTotal);
                Assert.Equal(53m, item.Length);
                Assert.Equal(31m, item.Width);
                Assert.Equal(14m, item.Height);
                Assert.Equal(25.6m, item.Volume);
                Assert.Equal(109592.88m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldParseAelNingboBookingForm()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportFormatTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "ael-ningbo-booking-form.xlsx");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteAelNingboBookingFormWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings, new BookingFormWrongNeighborAnalyzer());
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("NINGBO BRIDGE IMP. & EXP. CO. LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal(
                    string.Join(Environment.NewLine, "NO.668 BAIZHANG EAST ROAD,", "NINGBO 315040 CHINA"),
                    result.Invoice.ExporterAddressEN);
                Assert.Equal("Sensation Events", result.Invoice.CustomerNameEN);
                Assert.Equal(
                    string.Join(Environment.NewLine, "De Wetering 119, 4906 CT, Oosterhout,", "The Netherlands"),
                    result.Invoice.CustomerAddressEN);
                Assert.Equal("SAME AS CONSIGNEE", result.Invoice.NotifyPartyName);
                Assert.Equal(result.Invoice.CustomerAddressEN, result.Invoice.NotifyPartyAddress);
                Assert.Equal("NINGBO,CHINA", result.Invoice.PortOfLoading);
                Assert.Equal("ROTTERDAM, THE NETHERLANDS", result.Invoice.PortOfDestination);
                Assert.Equal(
                    string.Join(Environment.NewLine, "SENSATION EVENTS", "C/NO. 1 OF 8", "PO-NO: PW26-TEXH"),
                    result.Invoice.ShippingMarks);

                var item = Assert.Single(result.Invoice.Items);
                Assert.Equal("WOMEN T-SHIRT 100% COTTON", item.StyleName);
                Assert.Equal("棉制针织女式T恤衫", item.StyleNameCN);
                Assert.Equal("6109100000", item.HSCode);
                Assert.Equal(8m, item.Cartons);
                Assert.Equal("CTNS", item.CtnUnitEN);
                Assert.Equal(152m, item.GWTotal);
                Assert.Equal(0.564m, item.Volume);
                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(30, result.AnalysisReport.ItemTable.HeaderRow);
                Assert.Equal(32, result.AnalysisReport.ItemTable.DataStartRow);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldDetectShippingAdviceRowsWithSideLabels()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "shipping-advice-italy.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteShippingAdviceWithSideLabelsWorkbookAsync(filePath);

                var service = new ExcelImportService(new StubSettingsService());
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("2026YH018", result.Invoice.InvoiceNo);
                Assert.Equal(string.Empty, result.Invoice.ContractNo);
                Assert.Equal(string.Empty, result.Invoice.LetterOfCreditNo);
                Assert.Equal("ONIA LLC", result.Invoice.CustomerNameEN);
                Assert.Equal("10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA", result.Invoice.CustomerAddressEN);
                Assert.Equal("NINGBO BRIDGE IMP. & EXP. CO., LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("宁波布利杰进出口有限公司", result.Invoice.ExporterNameCN);
                Assert.Equal("N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA", result.Invoice.ExporterAddressEN);
                Assert.Equal("shanghai", result.Invoice.PortOfLoading);
                Assert.Equal("USA", result.Invoice.PortOfDestination);
                Assert.Contains("CLIENT", result.Invoice.ShippingMarks);
                Assert.Contains("STYLE# & DESCRIPTION", result.Invoice.ShippingMarks);
                Assert.Contains("UPC", result.Invoice.ShippingMarks);
                Assert.Equal(2, result.Invoice.Items.Count);

                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(3, result.AnalysisReport.ItemTable.Columns.StyleNoCol);
                Assert.Equal(4, result.AnalysisReport.ItemTable.Columns.StyleNameCol);
                Assert.Equal(6, result.AnalysisReport.ItemTable.Columns.StyleNameCNCol);
                Assert.Equal(0, result.AnalysisReport.ItemTable.Columns.BrandCol);

                Assert.Equal("300000024", result.Invoice.Items[0].PoNumber);
                Assert.Equal("HAM01", result.Invoice.Items[0].StyleNo);
                Assert.Equal("EVERYDAY TEE", result.Invoice.Items[0].StyleName);
                Assert.Equal("男式短袖圆领衫", result.Invoice.Items[0].StyleNameCN);
                Assert.Equal(string.Empty, result.Invoice.Items[0].Brand);
                Assert.Equal(130m, result.Invoice.Items[0].Quantity);
                Assert.Equal(60m, result.Invoice.Items[0].Length);
                Assert.Equal(38m, result.Invoice.Items[0].Width);
                Assert.Equal(24m, result.Invoice.Items[0].Height);
                Assert.Equal(11m, result.Invoice.Items[0].GWPerCtn);
                Assert.Equal(22m, result.Invoice.Items[0].GWTotal);
                Assert.Equal(10m, result.Invoice.Items[0].NWPerCtn);
                Assert.Equal(20m, result.Invoice.Items[0].NWTotal);
                Assert.Equal(369.2m, result.Invoice.Items[0].TotalPrice);
                Assert.Equal(260m, result.Invoice.TotalQuantity);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldRepairMisclassifiedSideLabelItemColumns()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "shipping-advice-misclassified.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteShippingAdviceWithSideLabelsWorkbookAsync(filePath);

                var service = new ExcelImportService(new StubSettingsService(), new MisclassifiedSideLabelAnalyzer());
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(3, result.AnalysisReport.ItemTable.Columns.StyleNoCol);
                Assert.Equal(4, result.AnalysisReport.ItemTable.Columns.StyleNameCol);
                Assert.Equal(6, result.AnalysisReport.ItemTable.Columns.StyleNameCNCol);
                Assert.Equal(0, result.AnalysisReport.ItemTable.Columns.BrandCol);
                Assert.Equal(9, result.AnalysisReport.ItemTable.Columns.DimensionCol);

                var item = result.Invoice.Items[0];
                Assert.Equal("HAM01", item.StyleNo);
                Assert.Equal("EVERYDAY TEE", item.StyleName);
                Assert.Equal("男式短袖圆领衫", item.StyleNameCN);
                Assert.Equal(string.Empty, item.Brand);
                Assert.Equal(60m, item.Length);
                Assert.Equal(38m, item.Width);
                Assert.Equal(24m, item.Height);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldUseDefaultExporterCnAndDetectGfrDetailTable()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "gfr-customs-data.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteGfrCustomsDataWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.Equal("26GFR-038", result.Invoice.InvoiceNo);
                Assert.Equal("GLOBAL FASHION RESOURCE INC", result.Invoice.CustomerNameEN);
                Assert.Equal("NINGBO BRIDGE IMP. & EXP. CO. LTD.", result.Invoice.ExporterNameEN);
                Assert.Equal("宁波布利杰进出口有限公司", result.Invoice.ExporterNameCN);
                Assert.NotEqual(result.Invoice.ExporterNameEN, result.Invoice.ExporterNameCN);
                Assert.Equal("SHANGHAI", result.Invoice.PortOfLoading);
                Assert.Equal("LOS ANGLES", result.Invoice.PortOfDestination);
                Assert.Equal("FOB SHANGHAI", result.Invoice.TradeTerms);
                Assert.Equal("BY SEA", result.Invoice.TransportMode);
                Assert.Contains("GFR", result.Invoice.ShippingMarks);

                Assert.Equal(6, result.Invoice.Items.Count);
                var item = result.Invoice.Items[0];
                Assert.Equal("60188JFT0660-GP1326-SMS7039", item.StyleNo);
                Assert.Equal("60% Cotton 40% Polyeter  Lady's KNIT PANTS", item.StyleName);
                Assert.Equal("棉制针织女式起绒长裤", item.StyleNameCN);
                Assert.Equal("LAZY SUNDAY", item.Brand);
                Assert.Equal(6000m, item.Quantity);
                Assert.Equal(5.6m, item.UnitPrice);
                Assert.Equal(33600m, item.TotalPrice);
                Assert.Equal(1000m, item.Cartons);
                Assert.Equal(20.520m, item.Volume);
                Assert.Equal(38m, item.Length);
                Assert.Equal(30m, item.Width);
                Assert.Equal(18m, item.Height);
                Assert.Equal(3300m, item.NWTotal);
                Assert.Equal(3500m, item.GWTotal);
                Assert.Equal(24274m, result.Invoice.TotalQuantity);
                Assert.Equal(3506m, result.Invoice.TotalCartons);
                Assert.Equal(10013.3m, result.Invoice.TotalNetWeight);
                Assert.Equal(10978.8m, result.Invoice.TotalGrossWeight);
                Assert.Equal(137697.8m, result.Invoice.TotalAmount);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldInferItemColumnsFromValueProfiles()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "value-profile-columns.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteValueProfileColumnsWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(4, result.AnalysisReport.ItemTable.Columns.HSCodeCol);
                Assert.Equal(5, result.AnalysisReport.ItemTable.Columns.DimensionCol);
                Assert.Equal(6, result.AnalysisReport.ItemTable.Columns.UnitPriceCol);
                Assert.Equal(7, result.AnalysisReport.ItemTable.Columns.TotalPriceCol);

                Assert.Equal(2, result.Invoice.Items.Count);
                var item = result.Invoice.Items[0];
                Assert.Equal("VP-TEE-001", item.StyleNo);
                Assert.Equal("6109100021", item.HSCode);
                Assert.Equal(100m, item.Quantity);
                Assert.Equal(60m, item.Length);
                Assert.Equal(40m, item.Width);
                Assert.Equal(30m, item.Height);
                Assert.Equal(2.5m, item.UnitPrice);
                Assert.Equal(250m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldDetectGenericIndustryDetailHeaders()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "generic-industry-details.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteGenericIndustryWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(1, result.AnalysisReport.ItemTable.Columns.StyleNoCol);
                Assert.Equal(2, result.AnalysisReport.ItemTable.Columns.StyleNameCol);
                Assert.Equal(3, result.AnalysisReport.ItemTable.Columns.QuantityCol);
                Assert.Equal(4, result.AnalysisReport.ItemTable.Columns.UnitENCol);
                Assert.Equal(5, result.AnalysisReport.ItemTable.Columns.CartonsCol);
                Assert.Equal(6, result.AnalysisReport.ItemTable.Columns.DimensionCol);
                Assert.Equal(7, result.AnalysisReport.ItemTable.Columns.UnitPriceCol);
                Assert.Equal(8, result.AnalysisReport.ItemTable.Columns.TotalPriceCol);
                Assert.Equal(9, result.AnalysisReport.ItemTable.Columns.HSCodeCol);
                Assert.Equal(10, result.AnalysisReport.ItemTable.Columns.OriginCol);
                Assert.Equal(11, result.AnalysisReport.ItemTable.Columns.GWTotalCol);
                Assert.Equal(12, result.AnalysisReport.ItemTable.Columns.NWTotalCol);

                Assert.Equal("GEN-INV-001", result.Invoice.InvoiceNo);
                Assert.Equal("GENERIC BUYER LLC", result.Invoice.CustomerNameEN);
                Assert.Equal(2, result.Invoice.Items.Count);

                var item = result.Invoice.Items[0];
                Assert.Equal("BRG-6201", item.StyleNo);
                Assert.Equal("BALL BEARING 6201", item.StyleName);
                Assert.Equal("8482102000", item.HSCode);
                Assert.Equal("CHINA", item.Origin);
                Assert.Equal(500m, item.Quantity);
                Assert.Equal("PCS", item.UnitEN);
                Assert.Equal(25m, item.Cartons);
                Assert.Equal(40m, item.Length);
                Assert.Equal(30m, item.Width);
                Assert.Equal(20m, item.Height);
                Assert.Equal(1.2m, item.UnitPrice);
                Assert.Equal(600m, item.TotalPrice);
                Assert.Equal(350m, item.GWTotal);
                Assert.Equal(320m, item.NWTotal);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public async Task ImportFromExcelAsync_ShouldInferWeightsAndVolumeFromColumnRelationships()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "ExcelImportXlsTests",
                Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "relationship-profile-columns.xls");

            Directory.CreateDirectory(directory);
            try
            {
                await WriteRelationshipProfileColumnsWorkbookAsync(filePath);

                var settings = new StubSettingsService();
                settings.Settings.System.DefaultTemplateExporterNameCn = "宁波布利杰进出口有限公司";
                var service = new ExcelImportService(settings);
                var result = await service.ImportFromExcelAsync(filePath);

                Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
                Assert.NotNull(result.Invoice);
                Assert.NotNull(result.AnalysisReport?.ItemTable);
                Assert.Equal(11, result.AnalysisReport.ItemTable.Columns.GWPerCtnCol);
                Assert.Equal(12, result.AnalysisReport.ItemTable.Columns.GWTotalCol);
                Assert.Equal(13, result.AnalysisReport.ItemTable.Columns.NWPerCtnCol);
                Assert.Equal(14, result.AnalysisReport.ItemTable.Columns.NWTotalCol);
                Assert.Equal(15, result.AnalysisReport.ItemTable.Columns.VolumeCol);

                Assert.Equal(2, result.Invoice.Items.Count);
                var item = result.Invoice.Items[0];
                Assert.Equal("REL-TEE-001", item.StyleNo);
                Assert.Equal("KNIT TEE", item.StyleName);
                Assert.Equal(100m, item.Quantity);
                Assert.Equal(10m, item.Cartons);
                Assert.Equal(50m, item.Length);
                Assert.Equal(40m, item.Width);
                Assert.Equal(30m, item.Height);
                Assert.Equal(0.6m, item.Volume);
                Assert.Equal(11m, item.GWPerCtn);
                Assert.Equal(110m, item.GWTotal);
                Assert.Equal(10m, item.NWPerCtn);
                Assert.Equal(100m, item.NWTotal);
                Assert.Equal(2.5m, item.UnitPrice);
                Assert.Equal(250m, item.TotalPrice);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static async Task WriteLegacyXlsWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("明细单");

            WriteCell(sheet, "A1", "宁波测试出口有限公司");
            WriteCell(sheet, "B3", "NINGBO TEST EXPORT CO., LTD.");
            WriteCell(sheet, "B4", "NO.1 TEST ROAD");
            WriteCell(sheet, "B8", "TEST CUSTOMER LTD.");
            WriteCell(sheet, "B9", "CUSTOMER ADDRESS");
            WriteCell(sheet, "O3", "2026.07.01");
            WriteCell(sheet, "O5", "CONTRACT-XLS-001");
            WriteCell(sheet, "O8", "USD");
            WriteCell(sheet, "O9", "INV-XLS-001");
            WriteCell(sheet, "O10", "一般贸易");
            WriteCell(sheet, "O14", "FOB");
            WriteCell(sheet, "O15", "NINGBO");
            WriteCell(sheet, "O16", "JAKARTA");
            WriteCell(sheet, "O17", "INDONESIA");

            WriteCell(sheet, 20, 2, "PO-XLS");
            WriteCell(sheet, 20, 3, "ST-XLS-001");
            WriteCell(sheet, 20, 4, "T SHIRT");
            WriteCell(sheet, 20, 6, "T恤");
            WriteCell(sheet, 20, 8, "61091000");
            WriteCell(sheet, 20, 9, "宁波其他");
            WriteCell(sheet, 20, 10, 120d);
            WriteCell(sheet, 20, 11, "PCS");
            WriteCell(sheet, 20, 12, "件");
            WriteCell(sheet, 20, 13, 10d);
            WriteCell(sheet, 20, 14, "CTNS");
            WriteCell(sheet, 20, 23, 2.5d);
            WriteCell(sheet, 20, 24, 300d);

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteDestinationCountryRegressionWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            var sheet = workbook.CreateSheet("Sheet1");
            WriteCell(sheet, 1, 1, "宁波布利杰进出口有限公司出口货物明细单");
            WriteCell(sheet, 2, 7, "时间");
            WriteCell(sheet, 2, 8, "2026.04.07");
            WriteCell(sheet, 3, 1, "发票抬头");
            WriteCell(sheet, 3, 2, "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, 5, 7, "发票号");
            WriteCell(sheet, 5, 8, "2026YH024");
            WriteCell(sheet, 6, 1, "收货人");
            WriteCell(sheet, 6, 2, "Peak Marketing");
            WriteCell(sheet, 7, 1, "consignee");
            WriteCell(sheet, 7, 2, "1/40 Yarraman Place, Virginia, 4014 Queensland,");
            WriteCell(sheet, 8, 2, "Brisbane, Australia");
            WriteCell(sheet, 8, 7, "贸易条款");
            WriteCell(sheet, 8, 8, "fob");
            WriteCell(sheet, 9, 7, "起运港");
            WriteCell(sheet, 9, 8, "ningbo");
            WriteCell(sheet, 10, 7, "目的地");
            WriteCell(sheet, 10, 8, "australia");
            WriteCell(sheet, 12, 2, "款号");
            WriteCell(sheet, 12, 4, "英文品名");
            WriteCell(sheet, 12, 5, "中文品名");
            WriteCell(sheet, 12, 6, "数量");
            WriteCell(sheet, 12, 7, "箱数");
            WriteCell(sheet, 12, 8, "箱子尺寸");
            WriteCell(sheet, 12, 9, "体积");
            WriteCell(sheet, 12, 10, "毛重/箱");
            WriteCell(sheet, 12, 11, "毛重");
            WriteCell(sheet, 12, 12, "净重/箱");
            WriteCell(sheet, 12, 13, "净重");
            WriteCell(sheet, 12, 14, "单价");
            WriteCell(sheet, 12, 15, "总价");
            WriteCell(sheet, 17, 2, "PMSEPT26006");
            WriteCell(sheet, 17, 4, "男式棉制针织圆领衫");
            WriteCell(sheet, 17, 5, "bottle opener tee");
            WriteCell(sheet, 17, 6, "999");
            WriteCell(sheet, 17, 7, "26");
            WriteCell(sheet, 17, 8, "60*40*23");
            WriteCell(sheet, 17, 9, "1.4352");
            WriteCell(sheet, 17, 10, "13.5");
            WriteCell(sheet, 17, 11, "351");
            WriteCell(sheet, 17, 12, "12.5");
            WriteCell(sheet, 17, 13, "325");
            WriteCell(sheet, 17, 15, "5797.62");

            await using var stream = File.Create(filePath);
            workbook.Write(stream, leaveOpen: false);
            workbook.Close();
        }

        private static async Task WriteOpenXmlXlsxWorkbookAsync(string filePath)
        {
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("OpenXML导入");

            WriteCell(sheet, "A1", "出口商");
            WriteCell(sheet, "B1", "NINGBO XLSX EXPORT CO., LTD.");
            WriteCell(sheet, "A2", "收货人");
            WriteCell(sheet, "B2", "OPENXML BUYER LTD.");
            WriteCell(sheet, "A3", "发票号");
            WriteCell(sheet, "B3", "INV-XLSX-001");
            WriteCell(sheet, "A4", "合同号");
            WriteCell(sheet, "B4", "CONTRACT-XLSX-001");
            WriteCell(sheet, "A5", "起运港");
            WriteCell(sheet, "B5", "SHANGHAI");
            WriteCell(sheet, "A6", "目的港");
            WriteCell(sheet, "B6", "HAMBURG");
            WriteCell(sheet, "A7", "贸易条款");
            WriteCell(sheet, "B7", "FOB SHANGHAI");
            WriteCell(sheet, "C7", "付款方式");
            WriteCell(sheet, "D7", "T/T");

            WriteCell(sheet, "A8", "款号");
            WriteCell(sheet, "B8", "英文品名");
            WriteCell(sheet, "C8", "数量");
            WriteCell(sheet, "D8", "箱数");
            WriteCell(sheet, "E8", "箱子尺寸");
            WriteCell(sheet, "F8", "体积");
            WriteCell(sheet, "G8", "毛重/箱");
            WriteCell(sheet, "H8", "总毛重");
            WriteCell(sheet, "I8", "净重/箱");
            WriteCell(sheet, "J8", "总净重");
            WriteCell(sheet, "K8", "单价USD");
            WriteCell(sheet, "L8", "金额USD");
            WriteCell(sheet, "M8", "HS编码");
            WriteCell(sheet, "N8", "原产地");

            WriteCell(sheet, 9, 1, "XLSX-TEE-001");
            WriteCell(sheet, 9, 2, "OPENXML T SHIRT");
            WriteCell(sheet, 9, 3, 120d);
            WriteCell(sheet, 9, 4, 12d);
            WriteCell(sheet, 9, 5, "50*40*30");
            WriteCell(sheet, 9, 6, 0.72d);
            WriteCell(sheet, 9, 7, 8.5d);
            WriteCell(sheet, 9, 8, 102d);
            WriteCell(sheet, 9, 9, 7.5d);
            WriteCell(sheet, 9, 10, 90d);
            WriteCell(sheet, 9, 11, 3.2d);
            WriteCell(sheet, 9, 12, 384d);
            WriteCell(sheet, 9, 13, "6109100021");
            WriteCell(sheet, 9, 14, "宁波");

            WriteCell(sheet, 10, 1, "XLSX-POLO-002");
            WriteCell(sheet, 10, 2, "OPENXML POLO SHIRT");
            WriteCell(sheet, 10, 3, 80d);
            WriteCell(sheet, 10, 4, 8d);
            WriteCell(sheet, 10, 5, "60*40*25");
            WriteCell(sheet, 10, 6, 0.48d);
            WriteCell(sheet, 10, 7, 9d);
            WriteCell(sheet, 10, 8, 72d);
            WriteCell(sheet, 10, 9, 8d);
            WriteCell(sheet, 10, 10, 64d);
            WriteCell(sheet, 10, 11, 4d);
            WriteCell(sheet, 10, 12, 320d);
            WriteCell(sheet, 10, 13, "6105100090");
            WriteCell(sheet, 10, 14, "宁波");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteDefaultTemplatePartyRegressionWorkbookAsync(string filePath)
        {
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("明细单");

            WriteCell(sheet, "A1", "宁波布利杰进出口有限公司");
            WriteCell(sheet, "A2", "SHIPPING ADVISE");
            WriteCell(sheet, "A3", "发货人 SHIPPER");
            WriteCell(sheet, "B3", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "A4", "Address");
            WriteCell(sheet, "B4", "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA");
            WriteCell(sheet, "M5", "合同号");
            WriteCell(sheet, "O5", "2024AA001");
            WriteCell(sheet, "A8", "收货人 CONSIGNEEE");
            WriteCell(sheet, "B8", "ONIA LLC.");
            WriteCell(sheet, "A9", "Address");
            WriteCell(sheet, "B9", "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA");
            WriteCell(sheet, "M9", "发票号");
            WriteCell(sheet, "O9", "2024AA001");
            WriteCell(sheet, "A13", "通知人 NOTIFY PARTY");
            WriteCell(sheet, "B13", "ONIA LLC.");
            WriteCell(sheet, "A14", "Address");
            WriteCell(sheet, "B14", "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA");
            WriteCell(sheet, "M14", "贸易条款");
            WriteCell(sheet, "O14", "FOB NINGBO");
            WriteCell(sheet, "M15", "起运地");
            WriteCell(sheet, "O15", "NINGBO");
            WriteCell(sheet, "M16", "目的港");
            WriteCell(sheet, "O16", "USA");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteBelowLabelFieldsWorkbookAsync(string filePath)
        {
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("下方标签导入");

            WriteCell(sheet, "A1", "出口商");
            WriteCell(sheet, "C1", "收货人");
            WriteCell(sheet, "E1", "发票号");
            WriteCell(sheet, "G1", "合同号");
            WriteCell(sheet, "A2", "NINGBO BELOW EXPORT CO., LTD.");
            WriteCell(sheet, "C2", "BELOW LABEL BUYER LLC");
            WriteCell(sheet, "E2", "INV-BELOW-001");
            WriteCell(sheet, "G2", "CONTRACT-BELOW-001");

            WriteCell(sheet, "A4", "起运港");
            WriteCell(sheet, "C4", "目的港");
            WriteCell(sheet, "E4", "贸易条款");
            WriteCell(sheet, "G4", "付款方式");
            WriteCell(sheet, "A5", "NINGBO");
            WriteCell(sheet, "C5", "ROTTERDAM");
            WriteCell(sheet, "E5", "FOB NINGBO");
            WriteCell(sheet, "G5", "T/T");

            WriteCell(sheet, "A7", "款号");
            WriteCell(sheet, "B7", "英文品名");
            WriteCell(sheet, "C7", "数量");
            WriteCell(sheet, "D7", "箱数");
            WriteCell(sheet, "E7", "箱子尺寸");
            WriteCell(sheet, "F7", "单价");
            WriteCell(sheet, "G7", "总价");
            WriteCell(sheet, "H7", "HS编码");
            WriteCell(sheet, 8, 1, "BL-TEE-001");
            WriteCell(sheet, 8, 2, "BELOW LABEL TEE");
            WriteCell(sheet, 8, 3, 100d);
            WriteCell(sheet, 8, 4, 10d);
            WriteCell(sheet, 8, 5, "50*40*30");
            WriteCell(sheet, 8, 6, 2.5d);
            WriteCell(sheet, 8, 7, 250d);
            WriteCell(sheet, 8, 8, "6109100021");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteShippingAdviceWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("报关和清关");

            WriteCell(sheet, "A1", "宁波布利杰进出口有限公司出口货物明细单");
            WriteCell(sheet, "A2", "SHIPPING ADVISE");
            WriteCell(sheet, "A3", "业务员");
            WriteCell(sheet, "B3", "严浩 13867805981");
            WriteCell(sheet, "H3", "时间");
            WriteCell(sheet, "I3", "2026.04.28");
            WriteCell(sheet, "A4", "发票抬头");
            WriteCell(sheet, "B4", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "H4", "合同号");
            WriteCell(sheet, "A5", "SHIPPER");
            WriteCell(sheet, "B5", "NO.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA");
            WriteCell(sheet, "H6", "发票号");
            WriteCell(sheet, "I6", "2026YH013");
            WriteCell(sheet, "A7", "收货人");
            WriteCell(sheet, "B7", "RDP Ltd");
            WriteCell(sheet, "H7", "贸易方式");
            WriteCell(sheet, "I7", "一般贸易");
            WriteCell(sheet, "A8", "consignee");
            WriteCell(sheet, "B8", "25-27 Riding House St, London, W1W 7DU. UK");
            WriteCell(sheet, "H8", "收回方式");
            WriteCell(sheet, "I8", "T/T");
            WriteCell(sheet, "H9", "贸易条款");
            WriteCell(sheet, "I9", "DDP Hongkong");
            WriteCell(sheet, "A10", "通知人");
            WriteCell(sheet, "B10", "RDP Ltd");
            WriteCell(sheet, "H10", "起运港");
            WriteCell(sheet, "I10", "ningbo");
            WriteCell(sheet, "A11", "notify party");
            WriteCell(sheet, "B11", "25-27 Riding House St, London, W1W 7DU. UK");
            WriteCell(sheet, "H11", "目的地");
            WriteCell(sheet, "I11", "hongkong");
            WriteCell(sheet, "A13", "唛头");
            WriteCell(sheet, "B13", "客人订单号");
            WriteCell(sheet, "C13", "客人款号");
            WriteCell(sheet, "D13", "英文品名");
            WriteCell(sheet, "E13", "面料");
            WriteCell(sheet, "F13", "中文品名");
            WriteCell(sheet, "G13", "数量");
            WriteCell(sheet, "H13", "箱数");
            WriteCell(sheet, "I13", "箱子尺寸");
            WriteCell(sheet, "J13", "体积");
            WriteCell(sheet, "K13", "毛重/箱");
            WriteCell(sheet, "L13", "毛重");
            WriteCell(sheet, "M13", "净重/箱");
            WriteCell(sheet, "N13", "净重");
            WriteCell(sheet, "O13", "单价");
            WriteCell(sheet, "P13", "总价");
            WriteCell(sheet, "A14", "SHIPPING MARK");
            WriteCell(sheet, "C14", "STYLE NO.");
            WriteCell(sheet, "G14", "QUANTITY");
            WriteCell(sheet, "A17", "RDP LONDON");
            WriteCell(sheet, "A18", "PO NO.:");

            WriteCell(sheet, 19, 1, "C/NO.");
            WriteCell(sheet, 19, 2, "633133");
            WriteCell(sheet, 19, 3, "116094-116097");
            WriteCell(sheet, 19, 4, "toy story 5 kids hoodie");
            WriteCell(sheet, 19, 5, "100% COTTON");
            WriteCell(sheet, 19, 6, "棉制针织男童戴帽衫");
            WriteCell(sheet, 19, 7, 611d);
            WriteCell(sheet, 19, 8, 30d);
            WriteCell(sheet, 19, 9, "302830");
            WriteCell(sheet, 19, 10, 0.756d);
            WriteCell(sheet, 19, 11, "10.5 kgs");
            WriteCell(sheet, 19, 12, "315 KGS");
            WriteCell(sheet, 19, 13, 8.8d);
            WriteCell(sheet, 19, 14, 264d);
            WriteCell(sheet, 19, 16, "USD 7,701.45");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteMultiRowBookingSheetWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("详细报关");

            WriteCell(sheet, "A1", "宁波布利杰进出口有限公司出口货物明细单 (托书)");
            WriteCell(sheet, "L2", new DateTime(2025, 7, 7));
            WriteCell(sheet, "A3", "发票抬头");
            WriteCell(sheet, "B3", "NINGBO BRIDGE IMP. & EXP. CO. LTD.                                                                                                                                                                                                             NO.668 BAIZHANG EAST ROAD.                                                                                                                                                                                                                           NINGBO 315040 CHINA");
            WriteCell(sheet, "I3", "合同号");
            WriteCell(sheet, "L3", "2024ATX-2,3,7,8");
            WriteCell(sheet, "I4", "发票号");
            WriteCell(sheet, "L4", "2025ATX004");
            WriteCell(sheet, "A5", "收货人");
            WriteCell(sheet, "B5", "FAME FASHION HOUSE  LLC                                                                                                                                                                                                                                          1735 Jersey Ave, North Brunswick NJ 08902 ，UNITED STATES OF AMERICA                                                                                                                                                                                                                                                        Tel# 212-287-9023                                                                                                                                                                                                              sallyyang@jnmworldwide.com&imports@tfxny.com");
            WriteCell(sheet, "I5", "收汇方式");
            WriteCell(sheet, "L5", "T/T");
            WriteCell(sheet, "I6", "贸易方式");
            WriteCell(sheet, "L6", "G.T.");
            WriteCell(sheet, "I7", "价格条款");
            WriteCell(sheet, "L7", "DDP LA ( OR LB)");
            WriteCell(sheet, "A8", "通知人");
            WriteCell(sheet, "B8", "ENJ INTERNATIONAL");
            WriteCell(sheet, "I8", "起运港");
            WriteCell(sheet, "L8", "NINGBO");
            WriteCell(sheet, "I9", "目的港");
            WriteCell(sheet, "L9", "LOS ANGELES ( OR LONG BEACH), USA");
            WriteCell(sheet, "A11", "品名");
            WriteCell(sheet, "B11", "棉制针织男T恤衫");
            WriteCell(sheet, "B12", "H.S.: 6109100021");
            WriteCell(sheet, "A13", "款号");
            WriteCell(sheet, "C13", "数量");
            WriteCell(sheet, "D13", "箱数");
            WriteCell(sheet, "E13", "毛重");
            WriteCell(sheet, "G13", "净重");
            WriteCell(sheet, "I13", "体积/立方数");
            WriteCell(sheet, "M13", "单价");
            WriteCell(sheet, "N13", "总价");
            WriteCell(sheet, "C14", "( piece)");
            WriteCell(sheet, "D14", "(ctn.)");
            WriteCell(sheet, "L14", "m³");
            WriteCell(sheet, "F15", "总毛重");
            WriteCell(sheet, "H15", "总净重");
            WriteCell(sheet, "I15", "长");
            WriteCell(sheet, "J15", "宽");
            WriteCell(sheet, "K15", "高");
            WriteCell(sheet, "B16", "MENS T-SHIRT");

            WriteCell(sheet, 17, 2, "无门襟无扣的T恤衫， HS.6109100021");
            WriteCell(sheet, 17, 3, 26712d);
            WriteCell(sheet, 17, 4, 1113d);
            WriteCell(sheet, 17, 5, "5.4 kgs");
            WriteCell(sheet, 17, 6, "6,010.2");
            WriteCell(sheet, 17, 7, 4.4d);
            WriteCell(sheet, 17, 8, 4897.2d);
            WriteCell(sheet, 17, 9, 53d);
            WriteCell(sheet, 17, 10, 31d);
            WriteCell(sheet, 17, 11, 14d);
            WriteCell(sheet, 17, 12, 25.6d);
            WriteCell(sheet, 17, 14, "$109,592.88");

            WriteCell(sheet, 18, 2, "有门襟有扣的T恤衫， HS.6109100021");
            WriteCell(sheet, 18, 3, 11544d);
            WriteCell(sheet, 18, 4, 481d);
            WriteCell(sheet, 18, 6, 2886d);
            WriteCell(sheet, 18, 8, 2405d);
            WriteCell(sheet, 18, 12, 12.642d);
            WriteCell(sheet, 18, 14, 50205.6d);

            WriteCell(sheet, 19, 2, "有门襟有扣的POLO衫，HS.6105100090");
            WriteCell(sheet, 19, 3, 21504d);
            WriteCell(sheet, 19, 4, 896d);
            WriteCell(sheet, 19, 6, 5376d);
            WriteCell(sheet, 19, 8, 4480d);
            WriteCell(sheet, 19, 12, 23.554d);
            WriteCell(sheet, 19, 14, 107249.28d);
            WriteCell(sheet, 22, 1, "合 计");
            WriteCell(sheet, 22, 3, 59760d);
            WriteCell(sheet, 22, 4, 2490d);
            WriteCell(sheet, 22, 8, 11782.2d);
            WriteCell(sheet, 22, 12, 61.796d);
            WriteCell(sheet, 22, 14, 267047.76d);
            WriteCell(sheet, 23, 1, "唛 头");
            WriteCell(sheet, 24, 1, "SHIP TO: ROSS STORES, INC                                                                                                                                                                    3404 INDIAN AVENUE                                                                                                                                                          PERRIS, CA 92572                                                                                                                                                             FROM: FAME FASHION HOUSE");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteAelNingboBookingFormWorkbookAsync(string filePath)
        {
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("BOOKING FORM");

            WriteCell(sheet, "C3", "托   运    单/SHIPPING ORDER");
            WriteCell(sheet, "A7", "Shipper/Exporter(提单)");
            WriteCell(sheet, "B7", "发货人");
            WriteCell(sheet, "G7", "To        : Ael-Berkman NINGBO");
            WriteCell(sheet, "A8", "NINGBO BRIDGE IMP. & EXP. CO. LTD.  ");
            WriteCell(sheet, "G8", "Attn     :");
            WriteCell(sheet, "H8", "FANNY FANG");
            WriteCell(sheet, "A9", "NO.668 BAIZHANG EAST ROAD,");
            WriteCell(sheet, "G9", "Fm       : ");
            WriteCell(sheet, "H9", "HOWARD XIANG");
            WriteCell(sheet, "A10", "NINGBO 315040 CHINA");
            WriteCell(sheet, "G10", "TEL:");
            WriteCell(sheet, "H10", "13003722990");

            WriteCell(sheet, "A14", "Consignee:");
            WriteCell(sheet, "B14", "收货人");
            WriteCell(sheet, "G14", "订单号（Order no.):");
            WriteCell(sheet, "A15", "Sensation Events");
            WriteCell(sheet, "G15", "PO NO.: PW26-TEXH");
            WriteCell(sheet, "A16", "De Wetering 119, 4906 CT, Oosterhout,");
            WriteCell(sheet, "G16", "货好时间：");
            WriteCell(sheet, "A17", "The Netherlands");
            WriteCell(sheet, "G17", " 03-30-26");

            WriteCell(sheet, "A19", "Notify Party:");
            WriteCell(sheet, "B19", "通知人");
            WriteCell(sheet, "A20", " SAME AS CONSIGNEE");

            WriteCell(sheet, "A24", "Pre-carriage by");
            WriteCell(sheet, "D24", "Place of receipt");
            WriteCell(sheet, "G24", "Service Code");
            WriteCell(sheet, "G25", "LCL/LCL    □");
            WriteCell(sheet, "H25", "FCL/LCL");
            WriteCell(sheet, "A26", "Vessel/Voyage No.");
            WriteCell(sheet, "D26", "Port of Loading");
            WriteCell(sheet, "G26", "LCL/FCL    □");
            WriteCell(sheet, "H26", "FCL/FCL");
            WriteCell(sheet, "D27", "NINGBO,CHINA");
            WriteCell(sheet, "G27", "Nos. of Original B/L Required");
            WriteCell(sheet, "A28", "Port of discharge");
            WriteCell(sheet, "B28", "目的港");
            WriteCell(sheet, "D28", "Place of delivery");
            WriteCell(sheet, "A29", "ROTTERDAM, THE NETHERLANDS");

            WriteCell(sheet, "A30", "Marks and numbers");
            WriteCell(sheet, "C30", "Quantity & type");
            WriteCell(sheet, "D30", "Description of goods");
            WriteCell(sheet, "G30", "Gross Weight ");
            WriteCell(sheet, "H30", "Measurement");
            WriteCell(sheet, "A31", "SENSATION EVENTS");
            WriteCell(sheet, "C31", "CTNS（最大外包装");
            WriteCell(sheet, "D31", "(中文/英文/商品编码）");
            WriteCell(sheet, "G31", "     kilos");
            WriteCell(sheet, "H31", "    cbm");
            WriteCell(sheet, "A32", "C/NO. 1 OF 8");
            WriteCell(sheet, 32, 3, 8d);
            WriteCell(sheet, "D32", "WOMEN T-SHIRT 100% COTTON");
            WriteCell(sheet, 32, 7, 152d);
            WriteCell(sheet, 32, 8, 0.564d);
            WriteCell(sheet, "A33", "PO-NO: PW26-TEXH");
            WriteCell(sheet, "D33", "棉制针织女式T恤衫");
            WriteCell(sheet, "D34", "HS.: 6109100000");
            WriteCell(sheet, "B42", "总计");
            WriteCell(sheet, "C42", "8 CTNS");
            WriteCell(sheet, "G42", "152 KGS");
            WriteCell(sheet, "H42", "0.564 CBM");
            WriteCell(sheet, "D49", "贸易条款：");
            WriteCell(sheet, "E49", "FOB NINGBO");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteShippingAdviceWithSideLabelsWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("报关和清关");

            WriteCell(sheet, "A1", "宁波布利杰进出口有限公司出口货物明细单");
            WriteCell(sheet, "A2", "SHIPPING ADVISE");
            WriteCell(sheet, "H3", "时间");
            WriteCell(sheet, "I3", "2026.05.11");
            WriteCell(sheet, "A4", "发票抬头");
            WriteCell(sheet, "B4", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "H4", "合同号");
            WriteCell(sheet, "I4", "信用证号");
            WriteCell(sheet, "A5", "SHIPPER");
            WriteCell(sheet, "B5", "N0.668, EAST BAIZHANG ROAD, NINGBO, 315040, CHINA");
            WriteCell(sheet, "H6", "发票号");
            WriteCell(sheet, "I6", "2026YH018");
            WriteCell(sheet, "A7", "收货人");
            WriteCell(sheet, "B7", "ONIA LLC");
            WriteCell(sheet, "A8", "consignee");
            WriteCell(sheet, "B8", "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA");
            WriteCell(sheet, "H8", "收回方式");
            WriteCell(sheet, "I8", "T/T");
            WriteCell(sheet, "H9", "贸易条款");
            WriteCell(sheet, "I9", "fob");
            WriteCell(sheet, "A10", "通知人");
            WriteCell(sheet, "B10", "ONIA LLC");
            WriteCell(sheet, "H10", "起运港");
            WriteCell(sheet, "I10", "shanghai");
            WriteCell(sheet, "A11", "notify party");
            WriteCell(sheet, "B11", "10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA");
            WriteCell(sheet, "H11", "目的地");
            WriteCell(sheet, "I11", "USA");
            WriteCell(sheet, "A13", "唛头");
            WriteCell(sheet, "B13", "客人订单号");
            WriteCell(sheet, "C13", "客人款号");
            WriteCell(sheet, "D13", "英文品名");
            WriteCell(sheet, "E13", "面料");
            WriteCell(sheet, "F13", "中文品名");
            WriteCell(sheet, "G13", "数量");
            WriteCell(sheet, "H13", "箱数");
            WriteCell(sheet, "I13", "箱子尺寸");
            WriteCell(sheet, "J13", "体积");
            WriteCell(sheet, "K13", "毛重/箱");
            WriteCell(sheet, "L13", "毛重");
            WriteCell(sheet, "M13", "净重/箱");
            WriteCell(sheet, "N13", "净重");
            WriteCell(sheet, "O13", "单价");
            WriteCell(sheet, "P13", "总价");
            WriteCell(sheet, "A14", "SHIPPING MARK");

            WriteCell(sheet, "A16", "CLIENT");
            WriteCell(sheet, "A17", "BRAND");
            WriteCell(sheet, "A18", "CARTON #");
            WriteCell(sheet, "A19", "PO#");
            WriteCell(sheet, "A20", "DESTINATION");

            WriteCell(sheet, 21, 1, "STYLE# & DESCRIPTION");
            WriteCell(sheet, 21, 2, "300000024");
            WriteCell(sheet, 21, 3, "HAM01");
            WriteCell(sheet, 21, 4, "EVERYDAY TEE");
            WriteCell(sheet, 21, 5, "96% polyester 4% spandex");
            WriteCell(sheet, 21, 6, "男式短袖圆领衫");
            WriteCell(sheet, 21, 7, 130d);
            WriteCell(sheet, 21, 8, 2d);
            WriteCell(sheet, 21, 9, "60*38*24");
            WriteCell(sheet, 21, 10, 0.10944d);
            WriteCell(sheet, 21, 11, 11d);
            WriteCell(sheet, 21, 12, 22d);
            WriteCell(sheet, 21, 13, 10d);
            WriteCell(sheet, 21, 14, 20d);
            WriteCell(sheet, 21, 15, 2.84d);
            WriteCell(sheet, 21, 16, 369.2d);

            WriteCell(sheet, 22, 1, "COLOR CODE & DESCRIPTION");
            WriteCell(sheet, 22, 2, "300000024");
            WriteCell(sheet, 22, 3, "HAM02");
            WriteCell(sheet, 22, 4, "EVERYDAY POLO");
            WriteCell(sheet, 22, 5, "96% polyester 4% spandex");
            WriteCell(sheet, 22, 6, "男式短袖门襟衫");
            WriteCell(sheet, 22, 7, 130d);
            WriteCell(sheet, 22, 8, 2d);
            WriteCell(sheet, 22, 9, "60*38*32");
            WriteCell(sheet, 22, 10, 0.14592d);
            WriteCell(sheet, 22, 11, 12d);
            WriteCell(sheet, 22, 12, 24d);
            WriteCell(sheet, 22, 13, 11d);
            WriteCell(sheet, 22, 14, 22d);
            WriteCell(sheet, 22, 15, 3.81d);
            WriteCell(sheet, 22, 16, 495.3d);

            WriteCell(sheet, 23, 1, "SIZE / QTY");
            WriteCell(sheet, 24, 1, "TOTAL UNITS");
            WriteCell(sheet, 25, 1, "COO");
            WriteCell(sheet, 26, 1, "CARTON WEIGHT");
            WriteCell(sheet, 27, 1, "CARTON DIMENSIONS");
            WriteCell(sheet, 27, 7, 260d);
            WriteCell(sheet, 27, 8, 4d);
            WriteCell(sheet, 27, 16, 864.5d);
            WriteCell(sheet, 28, 1, "UPC");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteGfrCustomsDataWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("走货资料");

            WriteCell(sheet, "A1", "出口商");
            WriteCell(sheet, "B1", "NINGBO BRIDGE IMP. & EXP. CO. LTD. NO.668 BAIZHANG EAST ROAD. NINGBO 315040 CHINA");
            WriteCell(sheet, "R2", "发票号");
            WriteCell(sheet, "S2", "26GFR-038");
            WriteCell(sheet, "A3", "收货人");
            WriteCell(sheet, "B3", "GLOBAL FASHION RESOURCE INC                                                                                                                                                                                                                       3315 S.BROADWAY                                                                                                                                                                                                                       LOS ANGELES CA 90007, USA                                                                                                                                                                                                                       TEL:(213)973-5941");
            WriteCell(sheet, "R3", "监管方式");
            WriteCell(sheet, "S3", "一般贸易");
            WriteCell(sheet, "R4", "付款方式");
            WriteCell(sheet, "S4", "T/T");
            WriteCell(sheet, "R5", "起运港");
            WriteCell(sheet, "S5", "SHANGHAI");
            WriteCell(sheet, "R6", "目的港");
            WriteCell(sheet, "S6", "LOS ANGLES");

            WriteCell(sheet, "A9", "箱唛");
            WriteCell(sheet, "B9", "序号");
            WriteCell(sheet, "C9", "款号/款名");
            WriteCell(sheet, "D9", "品名");
            WriteCell(sheet, "E9", "款式描述");
            WriteCell(sheet, "F9", "数量");
            WriteCell(sheet, "G9", "单价USD");
            WriteCell(sheet, "H9", "金额USD");
            WriteCell(sheet, "K9", "箱数");
            WriteCell(sheet, "L9", "总体积");
            WriteCell(sheet, "M9", "长");
            WriteCell(sheet, "N9", "宽");
            WriteCell(sheet, "O9", "高");
            WriteCell(sheet, "P9", "合计净重");
            WriteCell(sheet, "Q9", "合计毛重");
            WriteCell(sheet, "S9", "工厂信息");

            WriteGfrItemRow(sheet, 10, "GFR", 1, "60188JFT0660-GP1326-SMS7039", "60% Cotton 40% Polyeter  Lady's KNIT PANTS", "棉制针织女式起绒长裤     品牌名：LAZY SUNDAY", 6000d, 5.60d, 33600d, 1000d, 20.520d, 38d, 30d, 18d, 3300d, 3500d);
            WriteGfrItemRow(sheet, 11, "", 2, "60188JFT0660-GP1326-SMS7052", "60% Cotton 40% Polyeter  Lady's KNIT PANTS", "棉制针织女式起绒长裤     品牌名：LAZY SUNDAY", 30d, 5.60d, 168d, 1d, 0.101d, 60d, 40d, 42d, 15d, 16d);
            WriteGfrItemRow(sheet, 12, "", 3, "60278JFT0927-SMS7039", "57% Cotton 37% Modal 6% Spandex Lady's KNIT Shorts", "棉制针织女式短裤         品牌名：LAZY SUNDAY", 11880d, 5.45d, 64746d, 1485d, 17.963d, 36d, 28d, 12d, 3415.50d, 3862d);
            WriteGfrItemRow(sheet, 13, "", 4, "60278JFT0927-SMS7054", "57% Cotton 37% Modal 6% Spandex Lady's KNIT Shorts", "棉制针织女式短裤         品牌名：LAZY SUNDAY", 304d, 5.45d, 1656.80d, 16d, 0.419d, 36d, 28d, 26d, 68.80d, 84.80d);
            WriteGfrItemRow(sheet, 14, "", 5, "60278JFT0927-SMS7053", "57% Cotton 37% Modal 6% Spandex Lady's KNIT Shorts", "棉制针织女式短裤         品牌名：LAZY SUNDAY", 60d, 5.45d, 327d, 4d, 0.108d, 60d, 30d, 15d, 14d, 16d);
            WriteGfrItemRow(sheet, 15, "", 6, "21310JFT0888-GP1327-SMS7039", "60% Cotton 40% Polyeter  Lady's KNIT Top", "棉制针织女式起绒套头衫   品牌名：LAZY SUNDAY", 6000d, 6.20d, 37200d, 1000d, 22.800d, 38d, 30d, 20d, 3200d, 3500d);
            WriteCell(sheet, 16, 1, "总计:");
            WriteCell(sheet, 16, 6, 24274d);
            WriteCell(sheet, 16, 8, 137697.8d);
            WriteCell(sheet, 16, 11, 3506d);
            WriteCell(sheet, 16, 12, 61.911d);
            WriteCell(sheet, 16, 16, 10013.3d);
            WriteCell(sheet, 16, 17, 10978.8d);
            WriteCell(sheet, 17, 6, "所需单据及备注");

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static void WriteGfrItemRow(
            ISheet sheet,
            int row,
            string shippingMark,
            int sequence,
            string styleNo,
            string styleName,
            string styleDescription,
            double quantity,
            double unitPrice,
            double amount,
            double cartons,
            double volume,
            double length,
            double width,
            double height,
            double netWeight,
            double grossWeight)
        {
            WriteCell(sheet, row, 1, shippingMark);
            WriteCell(sheet, row, 2, sequence);
            WriteCell(sheet, row, 3, styleNo);
            WriteCell(sheet, row, 4, styleName);
            WriteCell(sheet, row, 5, styleDescription);
            WriteCell(sheet, row, 6, quantity);
            WriteCell(sheet, row, 7, unitPrice);
            WriteCell(sheet, row, 8, amount);
            WriteCell(sheet, row, 11, cartons);
            WriteCell(sheet, row, 12, volume);
            WriteCell(sheet, row, 13, length);
            WriteCell(sheet, row, 14, width);
            WriteCell(sheet, row, 15, height);
            WriteCell(sheet, row, 16, netWeight);
            WriteCell(sheet, row, 17, grossWeight);
        }

        private static async Task WriteValueProfileColumnsWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("报关资料");

            WriteCell(sheet, "A1", "发票抬头");
            WriteCell(sheet, "B1", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "A2", "收货人");
            WriteCell(sheet, "B2", "VALUE PROFILE BUYER LTD.");
            WriteCell(sheet, "A3", "发票号");
            WriteCell(sheet, "B3", "VP-INV-001");
            WriteCell(sheet, "A4", "起运港");
            WriteCell(sheet, "B4", "NINGBO");
            WriteCell(sheet, "A5", "目的港");
            WriteCell(sheet, "B5", "LOS ANGELES");

            WriteCell(sheet, "A8", "款号");
            WriteCell(sheet, "B8", "品名");
            WriteCell(sheet, "C8", "数量");
            WriteCell(sheet, "D8", "编码");
            WriteCell(sheet, "E8", "规格");
            WriteCell(sheet, "F8", "FOB");
            WriteCell(sheet, "G8", "小计");

            WriteCell(sheet, 9, 1, "VP-TEE-001");
            WriteCell(sheet, 9, 2, "KNIT TEE");
            WriteCell(sheet, 9, 3, 100d);
            WriteCell(sheet, 9, 4, "6109100021");
            WriteCell(sheet, 9, 5, "60*40*30");
            WriteCell(sheet, 9, 6, 2.5d);
            WriteCell(sheet, 9, 7, 250d);

            WriteCell(sheet, 10, 1, "VP-POLO-002");
            WriteCell(sheet, 10, 2, "KNIT POLO");
            WriteCell(sheet, 10, 3, 200d);
            WriteCell(sheet, 10, 4, "6105100090");
            WriteCell(sheet, 10, 5, "58*38*28");
            WriteCell(sheet, 10, 6, 3d);
            WriteCell(sheet, 10, 7, 600d);

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteRelationshipProfileColumnsWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("智能列关系");

            WriteCell(sheet, "A1", "发票抬头");
            WriteCell(sheet, "B1", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "A2", "收货人");
            WriteCell(sheet, "B2", "RELATIONSHIP BUYER LTD.");
            WriteCell(sheet, "A3", "发票号");
            WriteCell(sheet, "B3", "REL-INV-001");
            WriteCell(sheet, "A4", "起运港");
            WriteCell(sheet, "B4", "NINGBO");
            WriteCell(sheet, "A5", "目的港");
            WriteCell(sheet, "B5", "LOS ANGELES");

            WriteCell(sheet, "A8", "货号");
            WriteCell(sheet, "B8", "产品名称");
            WriteCell(sheet, "C8", "件数");
            WriteCell(sheet, "D8", "包装件数");
            WriteCell(sheet, "E8", "规格");
            WriteCell(sheet, "F8", "FOB USD");
            WriteCell(sheet, "G8", "小计");
            WriteCell(sheet, "H8", "备注1");
            WriteCell(sheet, "I8", "备注2");
            WriteCell(sheet, "J8", "备注3");
            WriteCell(sheet, "K8", "备注4");
            WriteCell(sheet, "L8", "备注5");
            WriteCell(sheet, "M8", "备注6");
            WriteCell(sheet, "N8", "备注7");
            WriteCell(sheet, "O8", "备注8");

            WriteCell(sheet, 9, 1, "REL-TEE-001");
            WriteCell(sheet, 9, 2, "KNIT TEE");
            WriteCell(sheet, 9, 3, 100d);
            WriteCell(sheet, 9, 4, 10d);
            WriteCell(sheet, 9, 5, "50*40*30");
            WriteCell(sheet, 9, 6, 2.5d);
            WriteCell(sheet, 9, 7, 250d);
            WriteCell(sheet, 9, 11, 11d);
            WriteCell(sheet, 9, 12, 110d);
            WriteCell(sheet, 9, 13, 10d);
            WriteCell(sheet, 9, 14, 100d);
            WriteCell(sheet, 9, 15, 0.6d);

            WriteCell(sheet, 10, 1, "REL-POLO-002");
            WriteCell(sheet, 10, 2, "KNIT POLO");
            WriteCell(sheet, 10, 3, 200d);
            WriteCell(sheet, 10, 4, 20d);
            WriteCell(sheet, 10, 5, "60*40*25");
            WriteCell(sheet, 10, 6, 3d);
            WriteCell(sheet, 10, 7, 600d);
            WriteCell(sheet, 10, 11, 12d);
            WriteCell(sheet, 10, 12, 240d);
            WriteCell(sheet, 10, 13, 11d);
            WriteCell(sheet, 10, 14, 220d);
            WriteCell(sheet, 10, 15, 1.2d);

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static async Task WriteGenericIndustryWorkbookAsync(string filePath)
        {
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Generic goods");

            WriteCell(sheet, "A1", "Exporter");
            WriteCell(sheet, "B1", "NINGBO BRIDGE IMP. & EXP. CO., LTD.");
            WriteCell(sheet, "A2", "Consignee");
            WriteCell(sheet, "B2", "GENERIC BUYER LLC");
            WriteCell(sheet, "A3", "Invoice No.");
            WriteCell(sheet, "B3", "GEN-INV-001");
            WriteCell(sheet, "A4", "Port of Loading");
            WriteCell(sheet, "B4", "NINGBO");
            WriteCell(sheet, "A5", "Destination");
            WriteCell(sheet, "B5", "ROTTERDAM");

            WriteCell(sheet, "A8", "Part Number");
            WriteCell(sheet, "B8", "Product Description");
            WriteCell(sheet, "C8", "Ordered Qty");
            WriteCell(sheet, "D8", "U/M");
            WriteCell(sheet, "E8", "Boxes");
            WriteCell(sheet, "F8", "Package Dimensions");
            WriteCell(sheet, "G8", "Unit Value");
            WriteCell(sheet, "H8", "Line Value");
            WriteCell(sheet, "I8", "HTS Code");
            WriteCell(sheet, "J8", "Country of Manufacture");
            WriteCell(sheet, "K8", "Gross KGS");
            WriteCell(sheet, "L8", "Net KGS");

            WriteCell(sheet, 9, 1, "BRG-6201");
            WriteCell(sheet, 9, 2, "BALL BEARING 6201");
            WriteCell(sheet, 9, 3, 500d);
            WriteCell(sheet, 9, 4, "PCS");
            WriteCell(sheet, 9, 5, 25d);
            WriteCell(sheet, 9, 6, "40*30*20");
            WriteCell(sheet, 9, 7, 1.2d);
            WriteCell(sheet, 9, 8, 600d);
            WriteCell(sheet, 9, 9, "8482102000");
            WriteCell(sheet, 9, 10, "CHINA");
            WriteCell(sheet, 9, 11, 350d);
            WriteCell(sheet, 9, 12, 320d);

            WriteCell(sheet, 10, 1, "MTR-24V");
            WriteCell(sheet, 10, 2, "DC MOTOR 24V");
            WriteCell(sheet, 10, 3, 100d);
            WriteCell(sheet, 10, 4, "PCS");
            WriteCell(sheet, 10, 5, 10d);
            WriteCell(sheet, 10, 6, "50*40*30");
            WriteCell(sheet, 10, 7, 8.5d);
            WriteCell(sheet, 10, 8, 850d);
            WriteCell(sheet, 10, 9, "8501109990");
            WriteCell(sheet, 10, 10, "CHINA");
            WriteCell(sheet, 10, 11, 120d);
            WriteCell(sheet, 10, 12, 110d);

            await using var output = File.Create(filePath);
            workbook.Write(output);
        }

        private static void WriteCell(ISheet sheet, string cellReference, string value)
        {
            var (row, column) = ParseCellReference(cellReference);
            WriteCell(sheet, row, column, value);
        }

        private static void WriteCell(ISheet sheet, string cellReference, DateTime value)
        {
            var (row, column) = ParseCellReference(cellReference);
            GetOrCreateRow(sheet, row).CreateCell(column - 1).SetCellValue(value);
        }

        private static void WriteCell(ISheet sheet, int row, int column, string value)
        {
            GetOrCreateRow(sheet, row).CreateCell(column - 1).SetCellValue(value);
        }

        private static void WriteCell(ISheet sheet, int row, int column, double value)
        {
            GetOrCreateRow(sheet, row).CreateCell(column - 1).SetCellValue(value);
        }

        private static IRow GetOrCreateRow(ISheet sheet, int oneBasedRow)
        {
            return sheet.GetRow(oneBasedRow - 1) ?? sheet.CreateRow(oneBasedRow - 1);
        }

        private static (int Row, int Column) ParseCellReference(string cellReference)
        {
            int column = 0;
            int index = 0;
            while (index < cellReference.Length && char.IsLetter(cellReference[index]))
            {
                column = (column * 26) + (char.ToUpperInvariant(cellReference[index]) - 'A' + 1);
                index++;
            }

            int row = int.Parse(cellReference[index..]);
            return (row, column);
        }

        private sealed class StubSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();

            public Task LoadAsync() => Task.CompletedTask;

            public Task SaveAsync() => Task.CompletedTask;
        }

        private sealed class StalePartyFieldAnalyzer : IExcelImportAnalyzer
        {
            public Task<ExcelImportAnalysisReport> AnalyzeAsync(
                string filePath,
                ExcelImportSettings settings,
                CancellationToken cancellationToken = default)
            {
                var report = new ExcelImportAnalysisReport
                {
                    AnalyzerId = "stale-party-test",
                    SourcePath = filePath,
                    SelectedWorksheetName = "明细单",
                    Confidence = 0.9m,
                    Fields =
                    [
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "InvoiceNo",
                            Value = "2024AA001",
                            WorksheetName = "明细单",
                            Row = 9,
                            Column = 15,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "CustomerNameEN",
                            Value = "CONSIGNEE",
                            WorksheetName = "明细单",
                            Row = 8,
                            Column = 1,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "CustomerAddressEN",
                            Value = "ONIA LLC.\n10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA",
                            WorksheetName = "明细单",
                            Row = 8,
                            Column = 2,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "NotifyPartyName",
                            Value = "ONIA LLC.",
                            WorksheetName = "明细单",
                            Row = 13,
                            Column = 2,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "NotifyPartyAddress",
                            Value = "ONIA LLC.\n10 EAST 40TH STREET, 37TH FL, NEW YORK, NY, 10017,USA",
                            WorksheetName = "明细单",
                            Row = 13,
                            Column = 2,
                            Confidence = 0.95m,
                            Source = "Test"
                        }
                    ]
                };

                return Task.FromResult(report);
            }
        }

        private sealed class MisclassifiedSideLabelAnalyzer : IExcelImportAnalyzer
        {
            public Task<ExcelImportAnalysisReport> AnalyzeAsync(
                string filePath,
                ExcelImportSettings settings,
                CancellationToken cancellationToken = default)
            {
                var report = new ExcelImportAnalysisReport
                {
                    AnalyzerId = "misclassified-side-label-test",
                    SourcePath = filePath,
                    SelectedWorksheetName = "报关和清关",
                    Confidence = 0.95m,
                    ItemTable = new ExcelImportItemTableAnalysis
                    {
                        WorksheetName = "报关和清关",
                        HeaderRow = 13,
                        HeaderDepth = 3,
                        DataStartRow = 21,
                        Confidence = 0.95m,
                        Columns = new ExcelImportItemColumnAnalysis
                        {
                            PoNumberCol = 2,
                            StyleNoCol = 4,
                            StyleNameCol = 4,
                            FabricCompositionCol = 5,
                            StyleNameCNCol = 6,
                            BrandCol = 6,
                            QuantityCol = 7,
                            CartonsCol = 8,
                            VolumeCol = 10,
                            GWPerCtnCol = 11,
                            GWTotalCol = 12,
                            NWPerCtnCol = 13,
                            NWTotalCol = 14,
                            UnitPriceCol = 15,
                            TotalPriceCol = 16
                        }
                    }
                };

                return Task.FromResult(report);
            }
        }

        private sealed class BookingFormWrongNeighborAnalyzer : IExcelImportAnalyzer
        {
            public Task<ExcelImportAnalysisReport> AnalyzeAsync(
                string filePath,
                ExcelImportSettings settings,
                CancellationToken cancellationToken = default)
            {
                var report = new ExcelImportAnalysisReport
                {
                    AnalyzerId = "booking-form-wrong-neighbor-test",
                    SourcePath = filePath,
                    SelectedWorksheetName = "BOOKING FORM",
                    Confidence = 0.9m,
                    Fields =
                    [
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "ExporterNameEN",
                            Value = "NINGBO BRIDGE IMP. & EXP. CO. LTD.",
                            WorksheetName = "BOOKING FORM",
                            Row = 8,
                            Column = 1,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "ExporterAddressEN",
                            Value = "To: Ael-Berkman NINGBO\nAttn:\nFm:\nTEL:\nFAX:\nMOBILE:",
                            WorksheetName = "BOOKING FORM",
                            Row = 7,
                            Column = 7,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "CustomerNameEN",
                            Value = "Sensation Events",
                            WorksheetName = "BOOKING FORM",
                            Row = 15,
                            Column = 1,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "CustomerAddressEN",
                            Value = "De Wetering 119, 4906 CT, Oosterhout,\nThe Netherlands",
                            WorksheetName = "BOOKING FORM",
                            Row = 16,
                            Column = 1,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "NotifyPartyName",
                            Value = "Place of receipt",
                            WorksheetName = "BOOKING FORM",
                            Row = 24,
                            Column = 4,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "PortOfLoading",
                            Value = "LCL/FCL □",
                            WorksheetName = "BOOKING FORM",
                            Row = 26,
                            Column = 7,
                            Confidence = 0.95m,
                            Source = "Test"
                        },
                        new ExcelImportFieldAnalysis
                        {
                            FieldKey = "PortOfDestination",
                            Value = "Place of delivery",
                            WorksheetName = "BOOKING FORM",
                            Row = 28,
                            Column = 4,
                            Confidence = 0.95m,
                            Source = "Test"
                        }
                    ]
                };

                return Task.FromResult(report);
            }
        }
    }
}
