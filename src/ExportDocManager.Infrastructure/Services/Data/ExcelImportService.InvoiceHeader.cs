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
        private void ParseInvoiceMainInfo(
            IExcelImportWorksheet worksheet,
            Invoice invoice,
            List<string> errors,
            ExcelImportSettings settings,
            ExcelImportAnalysisReport analysisReport)
        {
            try
            {
                invoice.InvoiceNo = GetAnalyzedValue(analysisReport, "InvoiceNo")
                    ?? GetValueByLabelsOrCell(worksheet, settings.InvoiceNoCell, ["发票号", "invoice no", "invoice number", "invoice"]);
                invoice.ContractNo = GetAnalyzedValue(analysisReport, "ContractNo")
                    ?? GetValueByLabelsOrCell(worksheet, settings.ContractNoCell, ["合同号", "contract no", "contract number", "contract"]);

                try
                {
                    invoice.InvoiceDate = ParseDate(
                        GetAnalyzedValue(analysisReport, "InvoiceDate")
                        ?? GetValueByLabelsOrCell(worksheet, settings.InvoiceDateCell, ["时间", "日期", "发票日期", "date"]));
                }
                catch
                {
                    invoice.InvoiceDate = DateTime.Now;
                }

                invoice.InvoiceDate = DateTimeValueHelper.NormalizeBusinessDate(invoice.InvoiceDate);
                invoice.ShipmentDate = invoice.InvoiceDate;
                invoice.PortOfLoading = GetAnalyzedValue(analysisReport, "PortOfLoading", IsUsablePortValue)
                    ?? GetValueByLabelsOrCell(
                        worksheet,
                        settings.PortOfLoadingCell,
                        ["起运港", "port of loading", "loading port"],
                        string.Empty,
                        IsUsablePortValue);
                invoice.PortOfDestination = GetAnalyzedValue(analysisReport, "PortOfDestination", IsUsablePortValue)
                    ?? GetValueByLabelsOrCell(
                        worksheet,
                        settings.PortOfDestinationCell,
                        ["目的港", "目的地", "port of destination", "port of discharge", "destination"],
                        string.Empty,
                        IsUsablePortValue);
                invoice.DestinationCountry = GetAnalyzedValue(analysisReport, "DestinationCountry")
                    ?? GetValueByLabelsOrCell(worksheet, settings.DestinationCountryCell, ["目的国", "destination country", "country"]);
                invoice.LetterOfCreditNo = GetAnalyzedValue(analysisReport, "LetterOfCreditNo")
                    ?? GetValueByLabelsOrCell(worksheet, settings.LetterOfCreditNoCell, ["信用证号", "l/c no", "lc no", "letter of credit"]);
                invoice.IssuingBank = GetAnalyzedValue(analysisReport, "IssuingBank")
                    ?? GetValueByLabelsOrCell(worksheet, settings.IssuingBankCell, ["开证行", "issuing bank"]);
                ClearHeaderNumbersCopiedFromInvoiceNo(invoice, clearContractNo: false);
                invoice.ShippingMarks = PickMoreCompleteMultilineValue(
                    GetAnalyzedValue(analysisReport, "ShippingMarks"),
                    GetShippingMarks(worksheet, settings));
                invoice.ShippingMarks = NormalizeExcelTextBlock(invoice.ShippingMarks);
                string tradeTerms = GetAnalyzedValue(analysisReport, "TradeTerms")
                    ?? GetValueByLabelsOrCell(
                        worksheet,
                        settings.TradeTermsCell,
                        ["贸易条款", "价格条款", "trade terms", "incoterms"],
                        string.Empty,
                        IsUsableTradeTermsValue);
                invoice.TradeTerms = NormalizeTradeTermsForImport(tradeTerms, invoice.PortOfLoading);

                string transportMode = GetAnalyzedValue(analysisReport, "TransportMode")
                    ?? GetValueByLabelsOrCell(
                        worksheet,
                        settings.TransportModeCell,
                        ["运输方式", "transport mode", "shipment mode"],
                        string.Empty,
                        IsUsableTransportModeValue);
                invoice.TransportMode = NormalizeTransportModeForImport(transportMode);
                invoice.PaymentTerms = GetAnalyzedValue(analysisReport, "PaymentTerms")
                    ?? GetValueByLabelsOrCell(worksheet, settings.PaymentTermsCell, ["付款方式", "收汇方式", "收回方式", "payment terms"], "T/T");
                invoice.Currency = GetAnalyzedValue(analysisReport, "Currency")
                    ?? GetValueByLabelsOrCell(worksheet, settings.CurrencyCell, ["币种", "currency"], "USD");
                invoice.SupervisionMode = GetAnalyzedValue(analysisReport, "SupervisionMode")
                    ?? GetValueByLabelsOrCell(worksheet, settings.SupervisionModeCell, ["监管方式", "贸易方式", "trade mode"], "一般贸易");
                invoice.Type = "报关数据";
            }
            catch (Exception ex)
            {
                errors.Add($"解析发票主信息时出错: {ex.Message}");
            }
        }


        private static void ClearHeaderNumbersCopiedFromInvoiceNo(Invoice invoice, bool clearContractNo = true)
        {
            if (string.IsNullOrWhiteSpace(invoice?.InvoiceNo))
            {
                return;
            }

            if (clearContractNo && string.Equals(invoice.ContractNo?.Trim(), invoice.InvoiceNo.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                invoice.ContractNo = string.Empty;
            }

            if (string.Equals(invoice.LetterOfCreditNo?.Trim(), invoice.InvoiceNo.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                invoice.LetterOfCreditNo = string.Empty;
            }
        }    }
}

