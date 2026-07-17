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
        private void ParseCustomerInfo(
            IExcelImportWorksheet worksheet,
            Invoice invoice,
            Customer customer,
            List<string> errors,
            ExcelImportSettings settings,
            ExcelImportAnalysisReport analysisReport)
        {
            try
            {
                var customerPartyBlock = GetUsablePartyBlock(GetAnalyzedValue(analysisReport, "CustomerNameEN"))
                    ?? FindPartyBlockBelowLabel(worksheet, ["收货人", "consignee name", "consignee", "customer"])
                    ?? GetUsablePartyBlock(GetValueByLabelsOrCell(worksheet, settings.CustomerNameCell, ["收货人", "consignee name", "customer"]));
                if (customerPartyBlock == null)
                {
                    customerPartyBlock = GetUsablePartyBlock(GetValueByLabelsOrCell(worksheet, settings.CustomerNameCell, ["consignee"]));
                }

                var notifyPartyBlock = GetUsablePartyBlock(GetAnalyzedValue(analysisReport, "NotifyPartyName"))
                    ?? FindPartyBlockBelowLabel(worksheet, ["通知人", "通知方", "notify party name", "notify party"])
                    ?? GetUsablePartyBlock(GetValueByLabelsOrCell(worksheet, settings.NotifyPartyNameCell, ["通知人", "通知方", "notify party name"]));
                if (notifyPartyBlock == null)
                {
                    notifyPartyBlock = GetUsablePartyBlock(GetValueByLabelsOrCell(worksheet, settings.NotifyPartyNameCell, ["notify party"]));
                }

                if (customerPartyBlock != null)
                {
                    string customerName = customerPartyBlock.Value.Name;
                    string customerAddressFromBlock = customerPartyBlock.Value.Address;
                    string notifypartyName = notifyPartyBlock?.Name ?? string.Empty;
                    string notifyPartyAddressFromBlock = notifyPartyBlock?.Address ?? string.Empty;

                    int customerAddressLines = Math.Max(1, settings.CustomerAddressLineCount);
                    int notifyPartyAddressLines = Math.Max(1, settings.NotifyPartyAddressLineCount);
                    string customerAddress = GetAnalyzedValue(analysisReport, "CustomerAddressEN")
                        ?? GetValueByLabelsOrCell(worksheet, settings.CustomerAddressStartCell, ["consignee", "收货人地址", "customer address"]);
                    var customerAddressBlock = SplitPartyNameAndAddress(customerAddress);
                    if (!string.IsNullOrWhiteSpace(customerAddressBlock.Address)
                        && ArePartyNamesEqual(customerAddressBlock.Name, customerName))
                    {
                        customerAddress = customerAddressBlock.Address;
                    }
                    else if (string.IsNullOrWhiteSpace(customerAddress)
                        || string.Equals(customerAddress, customerName, StringComparison.OrdinalIgnoreCase)
                        || ArePartyNamesEqual(customerAddressBlock.Name, customerName))
                    {
                        customerAddress = !string.IsNullOrWhiteSpace(customerAddressFromBlock)
                            ? customerAddressFromBlock
                            : GetMultiLineAddress(worksheet, settings.CustomerAddressStartCell, customerAddressLines);
                    }

                    string notifypartyAddress = GetAnalyzedValue(analysisReport, "NotifyPartyAddress")
                        ?? GetValueByLabelsOrCell(worksheet, settings.NotifyPartyAddressStartCell, ["notify party", "通知人地址", "通知方地址"]);
                    notifypartyAddress = RemoveLeadingPartyNameFromAddress(notifypartyAddress, notifypartyName);
                    if (string.IsNullOrWhiteSpace(notifypartyAddress) || ArePartyNamesEqual(notifypartyAddress, notifypartyName))
                    {
                        notifypartyAddress = !string.IsNullOrWhiteSpace(notifyPartyAddressFromBlock)
                            ? notifyPartyAddressFromBlock
                            : GetMultiLineAddress(worksheet, settings.NotifyPartyAddressStartCell, notifyPartyAddressLines);
                    }

                    customerAddress = NormalizeExcelTextBlock(customerAddress);
                    if (IsSameAsConsignee(notifypartyName) || IsSameAsConsignee(notifypartyAddress))
                    {
                        notifypartyName = "SAME AS CONSIGNEE";
                        notifypartyAddress = customerAddress;
                    }

                    notifypartyAddress = NormalizeExcelTextBlock(notifypartyAddress);

                    invoice.CustomerNameEN = customerName;
                    invoice.CustomerAddressEN = customerAddress;
                    invoice.NotifyPartyName = notifypartyName;
                    invoice.NotifyPartyAddress = notifypartyAddress;

                    customer.CustomerNameEN = customerName;
                    customer.AddressEN = customerAddress;
                    customer.NotifyPartyName = notifypartyName;
                    customer.NotifyPartyAddress = notifypartyAddress;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"解析客户信息时出错: {ex.Message}");
            }
        }

        private void ParseExporterInfo(
            IExcelImportWorksheet worksheet,
            Invoice invoice,
            Exporter exporter,
            List<string> errors,
            ExcelImportSettings settings,
            ExcelImportAnalysisReport analysisReport)
        {
            try
            {
                var analyzedExporterPartyBlock = GetUsablePartyBlock(GetAnalyzedValue(analysisReport, "ExporterNameEN"));
                var sheetExporterPartyBlock = FindPartyBlockBelowLabel(worksheet, ["发货人", "shipper/exporter", "shipper", "exporter"]);
                var exporterPartyBlock = MergePartyBlocks(analyzedExporterPartyBlock, sheetExporterPartyBlock);
                string exporterName = exporterPartyBlock?.Name
                    ?? GetValueByLabelsOrCell(worksheet, settings.ExporterNameCell, ["发票抬头", "出口商英文名称", "发货人", "shipper/exporter", "exporter name"]);
                if (string.IsNullOrWhiteSpace(exporterName))
                {
                    exporterName = GetValueByLabelsOrCell(worksheet, settings.ExporterNameCell, ["shipper"]);
                }

                string creditCode = GetValueByLabelsOrCell(worksheet, settings.CreditCodeCell, ["统一社会信用代码", "信用代码", "credit code"]);
                string exporterNameCN = ResolveExporterNameCn(worksheet, settings, analysisReport);
                if (string.IsNullOrWhiteSpace(exporterNameCN))
                {
                    errors.Add("Excel 未识别到出口商中文名，请先在系统设置填写“默认出口商中文名”，或在导入草稿中手工补填。");
                }

                if (!string.IsNullOrWhiteSpace(exporterName)
                    || !string.IsNullOrWhiteSpace(exporterNameCN)
                    || !string.IsNullOrWhiteSpace(creditCode))
                {
                    var exporterBlock = SplitPartyNameAndAddress(exporterName);
                    string exporterNameFromBlock = exporterBlock.Name;
                    string exporterAddressFromBlock = exporterBlock.Address;
                    if (!string.IsNullOrWhiteSpace(exporterNameFromBlock))
                    {
                        exporterName = exporterNameFromBlock;
                    }

                    int exporterAddressLines = Math.Max(1, settings.ExporterAddressLineCount);
                    bool exporterAddressFromPartyBlock = !string.IsNullOrWhiteSpace(exporterPartyBlock?.Address);
                    string exporterAddress = exporterPartyBlock?.Address;
                    if (string.IsNullOrWhiteSpace(exporterAddress))
                    {
                        exporterAddress = GetAnalyzedValue(analysisReport, "ExporterAddressEN", IsUsablePartyAddressValue)
                            ?? GetValueByLabelsOrCell(
                                worksheet,
                                settings.ExporterAddressStartCell,
                                ["shipper/exporter", "shipper", "发货人", "出口商地址"],
                                configuredValueValidator: IsUsablePartyAddressValue);
                    }
                    var exporterAddressBlock = exporterAddressFromPartyBlock
                        ? (Name: string.Empty, Address: string.Empty)
                        : SplitPartyNameAndAddress(exporterAddress);
                    if (!exporterAddressFromPartyBlock
                        && !string.IsNullOrWhiteSpace(exporterAddressBlock.Address)
                        && ArePartyNamesEqual(exporterAddressBlock.Name, exporterName))
                    {
                        exporterAddress = exporterAddressBlock.Address;
                    }
                    else if (string.IsNullOrWhiteSpace(exporterAddress)
                        || string.Equals(exporterAddress, exporterName, StringComparison.OrdinalIgnoreCase)
                        || ArePartyNamesEqual(exporterAddressBlock.Name, exporterName))
                    {
                        exporterAddress = !string.IsNullOrWhiteSpace(exporterAddressFromBlock)
                            ? exporterAddressFromBlock
                            : GetMultiLineAddress(worksheet, settings.ExporterAddressStartCell, exporterAddressLines);
                    }

                    exporterAddress = NormalizeExcelTextBlock(exporterAddress);

                    invoice.ExporterNameEN = exporterName;
                    invoice.ExporterNameCN = exporterNameCN;
                    invoice.ExporterAddressEN = exporterAddress;
                    invoice.ExporterAddressCN = "";
                    invoice.ExporterCreditCode = creditCode;

                    exporter.ExporterNameEN = exporterName;
                    exporter.ExporterNameCN = exporterNameCN;
                    exporter.AddressEN = exporterAddress;
                    exporter.CreditCode = creditCode;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"解析出口商信息时出错: {ex.Message}");
            }
        }


        private string ResolveExporterNameCn(
            IExcelImportWorksheet worksheet,
            ExcelImportSettings settings,
            ExcelImportAnalysisReport analysisReport)
        {
            string analyzed = NormalizeExporterNameCn(GetAnalyzedValue(analysisReport, "ExporterNameCN"));
            if (IsUsableExporterNameCn(analyzed))
            {
                return analyzed;
            }

            string explicitLabelValue = NormalizeExporterNameCn(
                FindValueBesideLabel(worksheet, ["出口商中文名称", "出口商中文", "中文抬头"]));
            if (IsUsableExporterNameCn(explicitLabelValue))
            {
                return explicitLabelValue;
            }

            string configuredCellValue = NormalizeExporterNameCn(GetCellValue(worksheet, settings.ExporterNameCNCell));
            if (IsUsableExporterNameCn(configuredCellValue))
            {
                return configuredCellValue;
            }

            string configuredDefault = NormalizeExporterNameCn(
                _settingsService.Settings?.System?.DefaultTemplateExporterNameCn);
            return IsUsableExporterNameCn(configuredDefault) ? configuredDefault : string.Empty;
        }

        private static bool IsUsableExporterNameCn(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || ExcelImportTemplateText.IsExporterPlaceholder(value)
                || LooksLikeKnownDocumentLabel(value)
                || LooksLikeItemHeader(value))
            {
                return false;
            }

            return value.Any(IsCjk)
                && (value.Contains("公司", StringComparison.Ordinal)
                    || value.Contains("集团", StringComparison.Ordinal)
                    || value.Contains("厂", StringComparison.Ordinal)
                    || value.Contains("贸易", StringComparison.Ordinal)
                    || value.Contains("进出口", StringComparison.Ordinal));
        }

        private static string NormalizeTradeTermsForImport(string value, string portOfLoading)
        {
            string normalized = NormalizeExcelTextBlock(value).Replace(Environment.NewLine, " ").Trim();
            if (IsUsableTradeTermsValue(normalized))
            {
                return normalized;
            }

            string port = NormalizeExcelTextBlock(portOfLoading).Replace(Environment.NewLine, " ").Trim();
            return string.IsNullOrWhiteSpace(port)
                ? "FOB"
                : $"FOB {port.ToUpperInvariant()}";
        }

        private static string NormalizeTransportModeForImport(string value)
        {
            string normalized = NormalizeExcelTextBlock(value).Replace(Environment.NewLine, " ").Trim();
            return IsUsableTransportModeValue(normalized) ? normalized : "BY SEA";
        }

        private static bool IsUsableTradeTermsValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsNumericOnly(value))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            string[] incoterms =
            [
                "exw", "fca", "fas", "fob", "cfr", "cnf", "cif", "cpt", "cip", "dap", "dpu", "dat", "ddp", "ddu"
            ];

            return incoterms.Any(term => normalized.StartsWith(term, StringComparison.Ordinal));
        }

        private static bool IsUsableTransportModeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsNumericOnly(value))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            return normalized.Contains("sea", StringComparison.Ordinal)
                || normalized.Contains("ocean", StringComparison.Ordinal)
                || normalized.Contains("vessel", StringComparison.Ordinal)
                || normalized.Contains("air", StringComparison.Ordinal)
                || normalized.Contains("truck", StringComparison.Ordinal)
                || normalized.Contains("rail", StringComparison.Ordinal)
                || normalized.Contains("road", StringComparison.Ordinal)
                || normalized.Contains("陆运", StringComparison.Ordinal)
                || normalized.Contains("海运", StringComparison.Ordinal)
                || normalized.Contains("空运", StringComparison.Ordinal)
                || normalized.Contains("铁路", StringComparison.Ordinal);
        }

        private static bool IsUsablePortValue(string value)
        {
            string normalized = NormalizeExcelTextBlock(value).Replace(Environment.NewLine, " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized) || IsNumericOnly(normalized))
            {
                return false;
            }

            string key = NormalizeText(normalized);
            if (key.Contains("lclfcl", StringComparison.Ordinal)
                || key.Contains("fclfcl", StringComparison.Ordinal)
                || key.Contains("lcllcl", StringComparison.Ordinal)
                || key.Contains("fcllcl", StringComparison.Ordinal)
                || key.Contains("servicecode", StringComparison.Ordinal)
                || key.Contains("placeofreceipt", StringComparison.Ordinal)
                || key.Contains("placeofdelivery", StringComparison.Ordinal)
                || normalized.Contains('□'))
            {
                return false;
            }

            return !LooksLikeKnownDocumentLabel(normalized)
                && normalized.Any(c => char.IsLetter(c) || IsCjk(c));
        }

        private static bool IsUsablePartyAddressValue(string value)
        {
            string normalized = NormalizeExcelTextBlock(value).Replace(Environment.NewLine, " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized) || IsNumericOnly(normalized))
            {
                return false;
            }

            if (LooksLikeKnownDocumentLabel(normalized)
                || LooksLikeItemHeader(normalized)
                || LooksLikeContactOnlyBlock(normalized))
            {
                return false;
            }

            return normalized.Any(c => char.IsLetter(c) || IsCjk(c));
        }

        private static bool LooksLikeContactOnlyBlock(string value)
        {
            string normalized = NormalizeExcelTextBlock(value).Replace(Environment.NewLine, " ").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string compact = NormalizeText(normalized);
            int contactTokenCount = 0;
            if (compact.StartsWith("to", StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"(^|[\s;,])to\s*[:：]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                contactTokenCount++;
            }

            foreach (string token in new[] { "attn", "tel", "fax", "mobile", "phone", "email" })
            {
                if (compact.Contains(token, StringComparison.Ordinal))
                {
                    contactTokenCount++;
                }
            }

            return contactTokenCount >= 2 && !LooksLikePostalAddressFragment(normalized);
        }

        private static bool LooksLikePostalAddressFragment(string value)
        {
            string normalized = value?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return normalized.Contains("road", StringComparison.Ordinal)
                || normalized.Contains("rd", StringComparison.Ordinal)
                || normalized.Contains("street", StringComparison.Ordinal)
                || normalized.Contains("st", StringComparison.Ordinal)
                || normalized.Contains("avenue", StringComparison.Ordinal)
                || normalized.Contains("ave", StringComparison.Ordinal)
                || normalized.Contains("building", StringComparison.Ordinal)
                || normalized.Contains("floor", StringComparison.Ordinal)
                || normalized.Contains("china", StringComparison.Ordinal)
                || normalized.Contains("united states", StringComparison.Ordinal)
                || normalized.Contains("netherlands", StringComparison.Ordinal)
                || normalized.Contains("usa", StringComparison.Ordinal)
                || normalized.Contains("路", StringComparison.Ordinal)
                || normalized.Contains("号", StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"\bno\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || (normalized.Any(char.IsDigit) && normalized.Contains(',', StringComparison.Ordinal));
        }

        private static bool IsNumericOnly(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length > 0
                && normalized.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c is '.' or ',' or '-' or '+');
        }


        private static string NormalizeExporterNameCn(string value)
        {
            if (ExcelImportTemplateText.IsExporterPlaceholder(value))
            {
                return string.Empty;
            }

            string normalized = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string[] suffixes =
            [
                "出口货物明细单",
                "出口货物明细",
                "货物明细单",
                "明细单"
            ];

            foreach (string suffix in suffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                {
                    normalized = normalized[..^suffix.Length].Trim();
                    break;
                }
            }

            int companyIndex = normalized.IndexOf("公司", StringComparison.Ordinal);
            if (companyIndex >= 0)
            {
                normalized = normalized[..(companyIndex + "公司".Length)].Trim();
            }

            return normalized;
        }    }
}

