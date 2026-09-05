using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplateService
    {
        private string RenderInvoicePreview(string templateContent, bool withSeal)
        {
            var invoice = BuildSampleInvoice();
            var customer = new Customer
            {
                CustomerNameEN = "SAMPLE CUSTOMER LTD.",
                AddressEN = "88 Sample Road, Hamburg, Germany",
                ContactPerson = "M. Buyer",
                Email = "buyer@example.com",
                Phone = "+49 40 0000 0000"
            };
            var exporter = BuildSampleExporter();
            var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(
                invoice,
                customer,
                exporter,
                withSeal,
                logger: _logger);
            return ScribanReportTemplateRenderer.Render(templateContent, globals);
        }

        private static string RenderPaymentVoucherPreview(string templateContent)
        {
            var sampleDate = new DateOnly(2026, 6, 15);
            var payee = new Payee
            {
                Name = "Sample Payee",
                BankName = "Sample Bank",
                RMBAccount = "6222 0000 1111 2222",
                Notes = "Sample beneficiary"
            };
            var payment = new Payment
            {
                Id = 1001,
                InvoiceNo = "PREVIEW-INTERNAL-001",
                PaymentDate = sampleDate,
                ShipmentDate = sampleDate.AddDays(-3),
                PayerName = "示例付款单位",
                PayeeName = payee.Name,
                PaymentMethod = "Bank Transfer",
                USDAmount = 100m,
                CNYAmount = 720m,
                Notes = "Sample payment voucher.",
                BankName = payee.BankName,
                AccountNo = payee.RMBAccount
            };

            var globals = ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(payment, payee);
            return ScribanReportTemplateRenderer.Render(templateContent, globals);
        }

        private static Invoice BuildSampleInvoice()
        {
            var sampleDate = new DateOnly(2026, 6, 15);
            var invoice = new Invoice
            {
                InvoiceNo = "PREVIEW-EXPORT-001",
                ContractNo = "CN-2026-001",
                InvoiceDate = sampleDate,
                ShipmentDate = sampleDate.AddDays(10),
                CustomerNameEN = "SAMPLE CUSTOMER LTD.",
                ExporterNameEN = "SAMPLE EXPORTER CO., LTD.",
                PortOfLoading = "NINGBO",
                PortOfDestination = "HAMBURG",
                DestinationCountry = "GERMANY",
                Currency = "USD",
                PaymentTerms = "T/T",
                TradeTerms = "FOB"
            };
            invoice.Items =
            [
                new Item
                {
                    StyleNo = "SKU-001",
                    StyleName = "Sample Jacket",
                    StyleNameCN = "样例夹克",
                    Quantity = 120,
                    UnitEN = "PCS",
                    UnitCN = "件",
                    Cartons = 10,
                    CtnUnitEN = "CTNS",
                    UnitPrice = 12.5m,
                    TotalPrice = 1500m,
                    GWPerCtn = 18m,
                    NWPerCtn = 16m,
                    GWTotal = 180m,
                    NWTotal = 160m,
                    Volume = 2.4m
                }
            ];
            invoice.CalculateTotals();
            return invoice;
        }

        private static Exporter BuildSampleExporter()
        {
            return new Exporter
            {
                ExporterNameEN = "SAMPLE EXPORTER CO., LTD.",
                ExporterNameCN = "样例出口公司",
                AddressEN = "99 Export Avenue, Ningbo, China",
                AddressCN = "宁波市样例出口路 99 号",
                ContactPerson = "Export Team",
                Phone = "+86 574 0000 0000",
                BankName = "Sample Bank Ningbo Branch",
                BankAccount = "1234567890",
                SwiftCode = "SAMPLECNXXX"
            };
        }

    }
}
