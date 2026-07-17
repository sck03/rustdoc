using ClosedXML.Excel;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Data
{
    public class ExcelImportTemplateService : IExcelImportTemplateService
    {
        private const string ExporterNameCnCellAddress = "A1";
        private const string ShipmentDateCellAddress = "O13";
        private const double BookingSheetLabelColumnWidth = 16D;

        private static readonly int[] BookingSheetHiddenColumns = [8, 9, 11, 12, 14];

        private static readonly ExcelImportTemplateInfo DefaultTemplate = new()
        {
            DisplayName = "导入数据模板",
            ResourceRelativePath = Path.Combine("Resources", "ExcelTemplates", "invoice-import-template.xlsx"),
            DefaultFileName = "导入数据模板.xlsx"
        };

        private readonly ISettingsService _settingsService;
        private readonly IExporterReadRepository _exporterReadRepository;
        private readonly IAppPathProvider _pathProvider;

        public ExcelImportTemplateService(
            ISettingsService settingsService,
            IExporterReadRepository exporterReadRepository,
            IAppPathProvider pathProvider)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _exporterReadRepository = exporterReadRepository ?? throw new ArgumentNullException(nameof(exporterReadRepository));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public ExcelImportTemplateInfo GetDefaultTemplate()
        {
            return DefaultTemplate;
        }

        public virtual string EnsureDefaultTemplateAvailable()
        {
            string templatePath = ResolveDefaultTemplatePath();
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException("内置 Excel 导入模板不存在。", templatePath);
            }

            return templatePath;
        }

        public string ExportDefaultTemplate(string targetFilePath, bool overwrite = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

            string sourcePath = EnsureDefaultTemplateAvailable();
            string normalizedTargetPath = PrepareTargetPath(targetFilePath, overwrite);

            using var workbook = new XLWorkbook(sourcePath);
            PrepareBlankTemplateWorkbook(workbook);
            SaveWorkbookAtomic(workbook, normalizedTargetPath);
            return normalizedTargetPath;
        }

        public string ExportBlankBookingSheet(string targetFilePath, bool overwrite = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

            string sourcePath = EnsureDefaultTemplateAvailable();
            string normalizedTargetPath = PrepareTargetPath(targetFilePath, overwrite);

            using var workbook = new XLWorkbook(sourcePath);
            var worksheet = PrepareBlankTemplateWorkbook(workbook);
            ApplyBookingSheetLayout(worksheet);
            SaveWorkbookAtomic(workbook, normalizedTargetPath);
            return normalizedTargetPath;
        }

        public string ExportBookingSheet(string sourceFilePath, string targetFilePath, bool overwrite = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

            string normalizedSourcePath = Path.GetFullPath(sourceFilePath);
            if (!File.Exists(normalizedSourcePath))
            {
                throw new FileNotFoundException("源 Excel 文件不存在。", normalizedSourcePath);
            }

            string normalizedTargetPath = Path.GetFullPath(targetFilePath);
            if (string.Equals(normalizedSourcePath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("订舱托单必须另存为新文件，不能覆盖源 Excel。");
            }

            PrepareTargetPath(normalizedTargetPath, overwrite);

            using var workbook = new XLWorkbook(normalizedSourcePath);
            var worksheet = workbook.Worksheet(1);
            ApplyBookingSheetLayout(worksheet);
            SaveWorkbookAtomic(workbook, normalizedTargetPath);
            return normalizedTargetPath;
        }

        public string ExportBookingSheetFromInvoice(Invoice invoice, string targetFilePath, bool overwrite = true)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

            string sourcePath = EnsureDefaultTemplateAvailable();
            string normalizedTargetPath = PrepareTargetPath(targetFilePath, overwrite);

            using var workbook = new XLWorkbook(sourcePath);
            var worksheet = workbook.Worksheet(1);
            PopulateInvoiceWorkbook(worksheet, invoice, _settingsService.Settings?.ExcelImport ?? new ExcelImportSettings());
            ApplyBookingSheetLayout(worksheet);
            SaveWorkbookAtomic(workbook, normalizedTargetPath);
            return normalizedTargetPath;
        }

        internal static bool IsTemplateExporterPlaceholder(string value)
        {
            return ExcelImportTemplateText.IsExporterPlaceholder(value);
        }

        private string ResolveDefaultTemplatePath()
        {
            string templatePath = DefaultTemplate.ResourceRelativePath;
            return Path.GetFullPath(Path.IsPathRooted(templatePath)
                ? templatePath
                : Path.Combine(_pathProvider.AppRoot, templatePath));
        }

        private static string PrepareTargetPath(string targetFilePath, bool overwrite)
        {
            string normalizedTargetPath = Path.GetFullPath(targetFilePath);
            string targetDirectory = Path.GetDirectoryName(normalizedTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (!overwrite && File.Exists(normalizedTargetPath))
            {
                throw new IOException($"目标文件已存在：{normalizedTargetPath}");
            }

            return normalizedTargetPath;
        }

        private static void SaveWorkbookAtomic(XLWorkbook workbook, string normalizedTargetPath)
        {
            AtomicFileHelper.WriteFileAtomic(
                normalizedTargetPath,
                tempPath =>
                {
                    workbook.SaveAs(tempPath);
                    return normalizedTargetPath;
                });
        }

        private IXLWorksheet PrepareBlankTemplateWorkbook(XLWorkbook workbook)
        {
            var worksheet = workbook.Worksheet(1);
            worksheet.Cell(ExporterNameCnCellAddress).Value = ResolveExporterNameCnForTemplate();
            return worksheet;
        }

        private static void PopulateInvoiceWorkbook(IXLWorksheet worksheet, Invoice invoice, ExcelImportSettings settings)
        {
            WriteCell(worksheet, settings.ExporterNameCNCell, invoice.ExporterNameCN);
            WriteCell(worksheet, settings.ExporterNameCell, invoice.ExporterNameEN);
            WriteLines(worksheet, settings.ExporterAddressStartCell, settings.ExporterAddressLineCount, invoice.ExporterAddressEN);
            WriteCell(worksheet, settings.CreditCodeCell, invoice.ExporterCreditCode);

            WriteCell(worksheet, settings.CustomerNameCell, invoice.CustomerNameEN);
            WriteLines(worksheet, settings.CustomerAddressStartCell, settings.CustomerAddressLineCount, invoice.CustomerAddressEN);
            WriteCell(worksheet, settings.NotifyPartyNameCell, invoice.NotifyPartyName);
            WriteLines(worksheet, settings.NotifyPartyAddressStartCell, settings.NotifyPartyAddressLineCount, invoice.NotifyPartyAddress);

            WriteCell(worksheet, settings.InvoiceDateCell, FormatDate(invoice.InvoiceDate));
            WriteCell(worksheet, settings.ContractNoCell, invoice.ContractNo);
            WriteCell(worksheet, settings.IssuingBankCell, invoice.IssuingBank);
            WriteCell(worksheet, settings.CurrencyCell, invoice.Currency);
            WriteCell(worksheet, settings.InvoiceNoCell, invoice.InvoiceNo);
            WriteCell(worksheet, settings.SupervisionModeCell, invoice.SupervisionMode);
            WriteCell(worksheet, settings.LetterOfCreditNoCell, invoice.LetterOfCreditNo);
            WriteCell(worksheet, settings.PaymentTermsCell, invoice.PaymentTerms);
            WriteCell(worksheet, settings.TransportModeCell, invoice.TransportMode);
            WriteCell(worksheet, ShipmentDateCellAddress, FormatDate(invoice.ShipmentDate));
            WriteCell(worksheet, settings.TradeTermsCell, invoice.TradeTerms);
            WriteCell(worksheet, settings.PortOfLoadingCell, invoice.PortOfLoading);
            WriteCell(worksheet, settings.PortOfDestinationCell, invoice.PortOfDestination);
            WriteCell(worksheet, settings.DestinationCountryCell, invoice.DestinationCountry);
            WriteCell(worksheet, settings.ShippingMarksCell, invoice.ShippingMarks);

            var items = invoice.Items?
                .Where(item => item != null)
                .ToList()
                ?? [];

            for (int i = 0; i < items.Count; i++)
            {
                int row = settings.ItemsStartRow + i;
                WriteItemRow(worksheet, row, settings, items[i]);
            }
        }

        private static void WriteItemRow(IXLWorksheet worksheet, int row, ExcelImportSettings settings, Item item)
        {
            WriteCell(worksheet, row, settings.PoNumberCol, item.PoNumber);
            WriteCell(worksheet, row, settings.StyleNoCol, item.StyleNo);
            WriteCell(worksheet, row, settings.StyleNameCol, item.StyleName);
            WriteCell(worksheet, row, settings.FabricCompositionCol, item.FabricComposition);
            WriteCell(worksheet, row, settings.StyleNameCNCol, item.StyleNameCN);
            WriteCell(worksheet, row, settings.BrandCol, item.Brand);
            WriteCell(worksheet, row, settings.HSCodeCol, item.HSCode);
            WriteCell(worksheet, row, settings.OriginCol, item.Origin);
            WriteCell(worksheet, row, settings.QuantityCol, item.Quantity);
            WriteCell(worksheet, row, settings.UnitENCol, item.UnitEN);
            WriteCell(worksheet, row, settings.UnitCNCol, item.UnitCN);
            WriteCell(worksheet, row, settings.CartonsCol, item.Cartons);
            WriteCell(worksheet, row, settings.CtnUnitENCol, item.CtnUnitEN);
            WriteCell(worksheet, row, settings.LengthCol, item.Length);
            WriteCell(worksheet, row, settings.WidthCol, item.Width);
            WriteCell(worksheet, row, settings.HeightCol, item.Height);
            WriteCell(worksheet, row, settings.VolumeCol, item.Volume);
            WriteCell(worksheet, row, settings.GWPerCtnCol, item.GWPerCtn);
            WriteCell(worksheet, row, settings.GWTotalCol, item.GWTotal);
            WriteCell(worksheet, row, settings.NWPerCtnCol, item.NWPerCtn);
            WriteCell(worksheet, row, settings.NWTotalCol, item.NWTotal);
            WriteCell(worksheet, row, settings.UnitPriceCol, item.UnitPrice);
            WriteCell(worksheet, row, settings.TotalPriceCol, item.TotalPrice);
        }

        private static void WriteLines(IXLWorksheet worksheet, string startCellAddress, int lineCount, string value)
        {
            var startCell = worksheet.Cell(startCellAddress);
            int row = startCell.Address.RowNumber;
            int column = startCell.Address.ColumnNumber;
            var lines = SplitLines(value);
            int rowsToWrite = Math.Max(1, lineCount);

            for (int i = 0; i < rowsToWrite; i++)
            {
                WriteCell(worksheet, row + i, column, i < lines.Count ? lines[i] : string.Empty);
            }
        }

        private static IReadOnlyList<string> SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private static void WriteCell(IXLWorksheet worksheet, string cellAddress, string value)
        {
            if (!string.IsNullOrWhiteSpace(cellAddress))
            {
                worksheet.Cell(cellAddress).Value = value ?? string.Empty;
            }
        }

        private static void WriteCell(IXLWorksheet worksheet, int row, int column, string value)
        {
            if (row > 0 && column > 0)
            {
                worksheet.Cell(row, column).Value = value ?? string.Empty;
            }
        }

        private static void WriteCell(IXLWorksheet worksheet, int row, int column, decimal value)
        {
            if (row > 0 && column > 0)
            {
                worksheet.Cell(row, column).Value = value == 0m ? string.Empty : value;
            }
        }

        private static string FormatDate(DateTime value)
        {
            return value == default
                ? string.Empty
                : value.ToString("yyyy-MM-dd");
        }

        private static void ApplyBookingSheetLayout(IXLWorksheet worksheet)
        {
            foreach (int columnNumber in BookingSheetHiddenColumns)
            {
                worksheet.Column(columnNumber).Hide();
            }

            var labelColumn = worksheet.Column(13);
            if (labelColumn.Width < BookingSheetLabelColumnWidth)
            {
                labelColumn.Width = BookingSheetLabelColumnWidth;
            }
        }

        private string ResolveExporterNameCnForTemplate()
        {
            string configuredExporterNameCn = (_settingsService.Settings?.System?.DefaultTemplateExporterNameCn ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configuredExporterNameCn))
            {
                return configuredExporterNameCn;
            }

            var exporterNames = _exporterReadRepository.QueryAsync(new ExporterReadQuery())
                .GetAwaiter()
                .GetResult()
                .Select(exporter => exporter?.ExporterNameCN?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return exporterNames.Count == 1
                ? exporterNames[0]
                : ExcelImportTemplateText.ExporterPlaceholderText;
        }
    }
}
