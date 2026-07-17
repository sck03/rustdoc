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
    public partial class ExcelImportService : IExcelImportService
    {
        private static readonly string[] DocumentLabelAliases =
        [
            "发票抬头", "出口商英文名称", "出口商", "发货人", "shipper/exporter", "shipper name", "exporter name", "shipper", "exporter", "seller", "consignor",
            "出口商中文名称", "出口商中文", "中文抬头", "出口商地址", "shipper address", "exporter address",
            "收货人", "客户", "consignee name", "customer name", "buyer", "consignee", "customer", "收货人地址", "客户地址", "consignee address", "customer address",
            "通知人", "通知方", "notify party name", "notify party", "通知人地址", "通知方地址", "notify party address",
            "发票号", "发票号码", "invoice no", "invoice number", "invoice#", "invoice", "inv no",
            "合同号", "合同号码", "contract no", "contract number", "contract#", "contract", "s/c no", "sc no",
            "发票日期", "日期", "时间", "invoice date", "date",
            "起运港", "装运港", "起运地", "port of loading", "loading port", "place of receipt", "pre-carriage by", "vessel/voyage no", "pol",
            "目的港", "目的地", "目的口岸", "port of destination", "destination port", "port of discharge", "discharge port", "place of delivery", "pod", "destination",
            "目的国", "目的国家", "destination country", "country",
            "贸易条款", "价格条款", "成交方式", "incoterms", "trade terms", "price terms",
            "运输方式", "运输模式", "transport mode", "shipment mode", "mode of transport",
            "付款方式", "收汇方式", "收回方式", "payment terms", "terms of payment", "payment",
            "币种", "货币", "currency", "curr",
            "监管方式", "贸易方式", "trade mode", "customs mode",
            "信用证号", "l/c no", "lc no", "letter of credit", "letter of credit no",
            "开证行", "issuing bank", "唛头", "箱唛", "唛头信息", "shipping mark", "shipping marks", "marks", "marks and numbers",
            "quantity & type", "description of goods", "gross weight", "measurement", "service code", "nos. of original b/l required"
        ];

        private readonly ISettingsService _settingsService;
        private readonly IExcelImportAnalyzer _analyzer;

        public ExcelImportService(ISettingsService settingsService)
            : this(settingsService, new BuiltInExcelImportAnalyzer())
        {
        }

        public ExcelImportService(ISettingsService settingsService, IExcelImportAnalyzer analyzer)
        {
            _settingsService = settingsService;
            _analyzer = analyzer;
        }

        public async Task<ImportResult> ImportFromExcelAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ExcelImportAnalysisReport analysisReport = null;
            var settings = _settingsService.Settings.ExcelImport ?? new ExcelImportSettings();
            try
            {
                analysisReport = await _analyzer
                    .AnalyzeAsync(filePath, settings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                analysisReport = new ExcelImportAnalysisReport
                {
                    AnalyzerId = "analysis-unavailable",
                    SourcePath = filePath,
                    Issues =
                    [
                        new ExcelImportAnalysisIssue
                        {
                            Severity = "Warning",
                            Code = "AnalyzerFailed",
                            Message = $"Excel 智能分析层未能完成，已回退到传统导入解析: {ex.Message}"
                        }
                    ]
                };
            }

            return await Task.Run(() => ImportFromExcelInternal(filePath, analysisReport, cancellationToken), cancellationToken);
        }

        private ImportResult ImportFromExcelInternal(
            string filePath,
            ExcelImportAnalysisReport analysisReport,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = new ImportResult
            {
                AnalysisReport = analysisReport
            };
            var settings = _settingsService.Settings.ExcelImport ?? new ExcelImportSettings();

            try
            {
                using (var workbook = OpenWorkbook(filePath, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var worksheet = FindAppropriateWorksheet(workbook, analysisReport);
                    if (worksheet == null)
                    {
                        result.Errors.Add("无法找到合适的工作表，请确保Excel文件格式正确。");
                        return result;
                    }

                    var invoice = new Invoice
                    {
                        Items = new List<Item>()
                    };
                    var customer = new Customer();
                    var exporter = new Exporter();

                    ParseInvoiceMainInfo(worksheet, invoice, result.Errors, settings, analysisReport);
                    ParseCustomerInfo(worksheet, invoice, customer, result.Errors, settings, analysisReport);
                    ParseExporterInfo(worksheet, invoice, exporter, result.Errors, settings, analysisReport);
                    ParseItemsInfo(worksheet, invoice, result.Errors, settings, analysisReport, cancellationToken);

                    result.Invoice = invoice;
                    result.Customer = customer;
                    result.Exporter = exporter;

                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"导入Excel时发生严重错误: {ex.Message}");
                return result;
            }
        }

        private IExcelImportWorksheet FindAppropriateWorksheet(IExcelImportWorkbook workbook, ExcelImportAnalysisReport analysisReport)
        {
            if (!string.IsNullOrWhiteSpace(analysisReport?.SelectedWorksheetName))
            {
                var analyzedWorksheet = workbook.Worksheets.FirstOrDefault(
                    w => string.Equals(w.Name, analysisReport.SelectedWorksheetName, StringComparison.OrdinalIgnoreCase));
                if (analyzedWorksheet != null)
                {
                    return analyzedWorksheet;
                }
            }

            var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("明细单"));
            if (worksheet != null)
                return worksheet;

            foreach (var sheet in workbook.Worksheets)
            {
                try
                {
                    var settings = _settingsService.Settings.ExcelImport;
                    bool hasInvoice = false;
                    bool hasContract = false;
                    bool hasConsignee = false;

                    try { hasInvoice = sheet.Cell(settings.InvoiceNoCell).GetValue().Contains("Invoice"); } catch { }
                    try { hasContract = sheet.Cell(settings.ContractNoCell).GetValue().Contains("Contract"); } catch { }
                    try { hasConsignee = sheet.Cell(settings.CustomerNameCell).GetValue().Contains("Consignee"); } catch { }

                    if (hasInvoice || hasContract || hasConsignee)
                    {
                        return sheet;
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (workbook.Worksheets.Count > 0)
            {
                return workbook.Worksheet(1);
            }

            return null;
        }
    }
}
