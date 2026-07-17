using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiExcelDtoFactory
    {
        public static ApiExcelImportPreviewResponse FromImportResult(string sourcePath, ImportResult result)
        {
            result ??= new ImportResult();

            return new ApiExcelImportPreviewResponse(
                sourcePath,
                result.Success,
                result.Invoice == null ? null : ApiInvoiceDtoFactory.FromInvoiceDetail(result.Invoice),
                FromCustomer(result.Customer, result.Invoice),
                FromExporter(result.Exporter),
                FromAnalysisReport(result.AnalysisReport),
                result.Errors ?? new List<string>(),
                "Excel 导入只读取用户显式选择或输入的源文件路径，解析结果随响应返回；不会写入数据库，也不会创建系统 C 盘默认落点。内置导入模板仍随程序放在 Resources/ExcelTemplates/ 下。");
        }

        private static ApiExcelImportAnalysisReportDto FromAnalysisReport(ExcelImportAnalysisReport report)
        {
            return report == null
                ? null
                : new ApiExcelImportAnalysisReportDto(
                    report.SchemaVersion ?? string.Empty,
                    report.AnalyzerId ?? string.Empty,
                    report.SelectedWorksheetName ?? string.Empty,
                    report.Confidence,
                    report.Sheets?.Select(sheet => new ApiExcelImportSheetAnalysisDto(
                        sheet.Name ?? string.Empty,
                        sheet.UsedRowCount,
                        sheet.UsedColumnCount,
                        sheet.FieldCandidateCount,
                        sheet.HasItemTable,
                        sheet.Confidence)).ToList() ?? new List<ApiExcelImportSheetAnalysisDto>(),
                    report.Fields?.Select(field => new ApiExcelImportFieldAnalysisDto(
                        field.FieldKey ?? string.Empty,
                        field.DisplayName ?? string.Empty,
                        field.Value ?? string.Empty,
                        field.WorksheetName ?? string.Empty,
                        field.Row,
                        field.Column,
                        field.Confidence,
                        field.Source ?? string.Empty)).ToList() ?? new List<ApiExcelImportFieldAnalysisDto>(),
                    report.ItemTable == null
                        ? null
                        : new ApiExcelImportItemTableAnalysisDto(
                            report.ItemTable.WorksheetName ?? string.Empty,
                            report.ItemTable.HeaderRow,
                            report.ItemTable.HeaderDepth,
                            report.ItemTable.DataStartRow,
                            report.ItemTable.Confidence,
                            FromItemColumns(report.ItemTable.Columns)),
                    report.Issues?.Select(issue => new ApiExcelImportAnalysisIssueDto(
                        issue.Severity ?? string.Empty,
                        issue.Code ?? string.Empty,
                        issue.Message ?? string.Empty,
                        issue.FieldKey ?? string.Empty)).ToList() ?? new List<ApiExcelImportAnalysisIssueDto>());
        }

        private static ApiExcelImportItemColumnAnalysisDto FromItemColumns(ExcelImportItemColumnAnalysis columns)
        {
            columns ??= new ExcelImportItemColumnAnalysis();
            return new ApiExcelImportItemColumnAnalysisDto(
                columns.PoNumberCol,
                columns.StyleNoCol,
                columns.StyleNameCol,
                columns.FabricCompositionCol,
                columns.StyleNameCNCol,
                columns.BrandCol,
                columns.HSCodeCol,
                columns.OriginCol,
                columns.QuantityCol,
                columns.UnitENCol,
                columns.UnitCNCol,
                columns.CartonsCol,
                columns.CtnUnitENCol,
                columns.LengthCol,
                columns.WidthCol,
                columns.HeightCol,
                columns.DimensionCol,
                columns.VolumeCol,
                columns.GWPerCtnCol,
                columns.GWTotalCol,
                columns.NWPerCtnCol,
                columns.NWTotalCol,
                columns.UnitPriceCol,
                columns.TotalPriceCol);
        }

        private static ApiImportedCustomerDto FromCustomer(Customer customer, Invoice invoice)
        {
            string fallbackName = invoice?.CustomerNameEN ?? string.Empty;
            string fallbackAddress = invoice?.CustomerAddressEN ?? string.Empty;
            string fallbackNotifyName = invoice?.NotifyPartyName ?? string.Empty;
            string fallbackNotifyAddress = invoice?.NotifyPartyAddress ?? string.Empty;

            if (customer == null && string.IsNullOrWhiteSpace(fallbackName))
            {
                return null;
            }

            return new ApiImportedCustomerDto
            {
                Id = customer?.Id ?? 0,
                CustomerNameEN = FirstNonBlank(customer?.CustomerNameEN, fallbackName),
                DisplayName = FirstNonBlank(customer?.DisplayName, fallbackName),
                NotifyPartyName = FirstNonBlank(customer?.NotifyPartyName, fallbackNotifyName),
                AddressEN = FirstNonBlank(customer?.AddressEN, fallbackAddress),
                NotifyPartyAddress = FirstNonBlank(customer?.NotifyPartyAddress, fallbackNotifyAddress),
                ContactPerson = customer?.ContactPerson ?? string.Empty,
                Phone = customer?.Phone ?? string.Empty,
                Email = customer?.Email ?? string.Empty,
                TaxId = customer?.TaxId ?? string.Empty,
                Notes = customer?.Notes ?? string.Empty
            };
        }

        private static string FirstNonBlank(string value, string fallback)
        {
            return !string.IsNullOrWhiteSpace(value) ? value : fallback ?? string.Empty;
        }

        private static ApiImportedExporterDto FromExporter(Exporter exporter)
        {
            return exporter == null
                ? null
                : new ApiImportedExporterDto
                {
                    Id = exporter.Id,
                    ExporterNameEN = exporter.ExporterNameEN ?? string.Empty,
                    ExporterNameCN = exporter.ExporterNameCN ?? string.Empty,
                    AddressEN = exporter.AddressEN ?? string.Empty,
                    AddressCN = exporter.AddressCN ?? string.Empty,
                    ContactPerson = exporter.ContactPerson ?? string.Empty,
                    CreditCode = exporter.CreditCode ?? string.Empty,
                    CustomsCode = exporter.CustomsCode ?? string.Empty,
                    Phone = exporter.Phone ?? string.Empty,
                    BankName = exporter.BankName ?? string.Empty,
                    BankAccount = exporter.BankAccount ?? string.Empty,
                    SwiftCode = exporter.SwiftCode ?? string.Empty,
                    Notes = exporter.Notes ?? string.Empty,
                    DocSealPath = exporter.DocSealPath ?? string.Empty,
                    CustomsSealPath = exporter.CustomsSealPath ?? string.Empty
                };
        }
    }
}
