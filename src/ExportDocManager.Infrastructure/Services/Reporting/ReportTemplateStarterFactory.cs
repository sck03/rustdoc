using System.Net;

namespace ExportDocManager.Services.Reporting
{
    internal static class ReportTemplateStarterFactory
    {
        public const string ExportInvoiceStarterPreset = "export-invoice";
        public const string ExportPackingListStarterPreset = "export-packing-list";
        public const string InternalPaymentVoucherStarterPreset = "internal-payment-voucher";
        public const string InternalExpenseReimbursementStarterPreset = "internal-expense-reimbursement";

        public static string Create(ReportDocumentType reportType, string title, string templateIdentifier = null)
        {
            string starterPreset = DetermineStarterPreset(reportType, templateIdentifier, title);
            string resolvedTitle = ResolveHeading(title, starterPreset, templateIdentifier);
            return BuildTemplateHtml(starterPreset, resolvedTitle);
        }

        public static string DetermineStarterPreset(
            ReportDocumentType reportType,
            string templateIdentifier,
            string title = null)
        {
            string identity = $"{templateIdentifier} {title}".ToLowerInvariant();
            if (reportType == ReportDocumentType.PaymentVoucher)
            {
                if (identity.Contains("expense") ||
                    identity.Contains("reimbursement") ||
                    identity.Contains("报销"))
                {
                    return InternalExpenseReimbursementStarterPreset;
                }

                return InternalPaymentVoucherStarterPreset;
            }

            return identity.Contains("packing") || identity.Contains("装箱")
                ? ExportPackingListStarterPreset
                : ExportInvoiceStarterPreset;
        }

        private static string ResolveHeading(string title, string starterPreset, string templateIdentifier)
        {
            if (!string.IsNullOrWhiteSpace(title) && !LooksLikeTechnicalTemplateName(title.ToLowerInvariant()))
            {
                return title.Trim();
            }

            return starterPreset switch
            {
                ExportPackingListStarterPreset => "PACKING LIST",
                InternalPaymentVoucherStarterPreset => "付款单（费用支付专用）",
                InternalExpenseReimbursementStarterPreset => "费用报销明细单",
                _ => "INVOICE"
            };
        }

        private static bool LooksLikeTechnicalTemplateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return value.Contains("invoice") ||
                   value.Contains("packing") ||
                   value.Contains("payment") ||
                   value.Contains("voucher") ||
                   value.Contains("expense") ||
                   value.Contains("reimbursement") ||
                   value.Contains('_');
        }

        private static string BuildTemplateHtml(string starterPreset, string heading)
        {
            return starterPreset switch
            {
                ExportPackingListStarterPreset => BuildExportPackingListHtml(heading),
                InternalPaymentVoucherStarterPreset => BuildInternalPaymentVoucherHtml(heading),
                InternalExpenseReimbursementStarterPreset => BuildInternalExpenseReimbursementHtml(heading),
                _ => BuildExportInvoiceHtml(heading)
            };
        }

        private static string BuildExportInvoiceHtml(string heading)
        {
            string encodedHeading = WebUtility.HtmlEncode(heading);
            return $$$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <style>
        @page { size: A4 portrait; margin: 15mm; }
        body { font-family: Arial, "Microsoft YaHei", sans-serif; font-size: 12px; margin: 0; }
        .report-container { width: 100%; max-width: 980px; margin: 0 auto; }
        .company { text-align: center; font-weight: bold; font-size: 24px; margin-top: 6px; }
        .company-subtitle { text-align: center; font-size: 13px; margin: 2px 0 12px; }
        .document-title { text-align: center; font-weight: bold; font-size: 22px; margin: 0 0 10px; }
        .top-grid { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
        .top-grid td { padding: 4px 6px; vertical-align: top; }
        .detail-table { width: 100%; border-collapse: collapse; table-layout: fixed; }
        .detail-table th, .detail-table td { border: 1px solid #222; padding: 8px 10px; vertical-align: top; }
        .detail-table th { text-align: left; font-size: 16px; }
        .marks-col { width: 24%; }
        .description-col { width: 56%; }
        .amount-col { width: 20%; }
        .marks-cell { white-space: pre-line; }
        .total-row td { font-weight: bold; }
        .amount-cell { text-align: right; white-space: nowrap; }
    </style>
</head>
<body>
    <div class="report-container">
        <div class="company">{{ Exporter.ExporterNameEN | string.upcase }}</div>
        <div class="company-subtitle">{{ Exporter.AddressEN }}</div>
        <div class="document-title">{{{ encodedHeading }}}</div>

        <table class="top-grid">
            <tr>
                <td style="width:46%;">TO: {{ Customer.CustomerNameEN }}</td>
                <td style="width:24%;"></td>
                <td style="width:30%; text-align:right;">Invoice No.: {{ Invoice.InvoiceNo }}</td>
            </tr>
            <tr>
                <td>{{ Customer.AddressEN }}</td>
                <td></td>
                <td style="text-align:right;">Contract No.: {{ Invoice.ContractNo }}</td>
            </tr>
            <tr>
                <td>From: {{ Invoice.PortOfLoading }}</td>
                <td>To: {{ Invoice.DestinationCountry }}</td>
                <td style="text-align:right;">Date: {{ Invoice.InvoiceDate | date.to_string '%d %b %Y' }}</td>
            </tr>
            <tr>
                <td>Payment Terms: {{ Invoice.PaymentTerms }}</td>
                <td colspan="2">Issued by: {{ Exporter.ExporterNameEN }}</td>
            </tr>
        </table>

        <table class="detail-table">
            <thead>
                <tr>
                    <th class="marks-col">唛头 / Marks</th>
                    <th class="description-col">货品名称 / Quantities and Descriptions</th>
                    <th class="amount-col">总值 / Amount</th>
                </tr>
            </thead>
            <tbody>
                {{ for item in items }}
                {{ if for.first }}
                <tr>
                    <td rowspan="{{ items.size + 1 }}" class="marks-cell">{{ Invoice.ShippingMarks }}</td>
                    <td></td>
                    <td class="amount-cell">{{ Invoice.TradeTerms }} {{ Invoice.PortOfLoading }}</td>
                </tr>
                {{ end }}
                <tr>
                    <td>
                        <div>{{ item.StyleName }}</div>
                        <div>{{ item.PoNumber }}</div>
                        <div>{{ item.StyleNo }} {{ item.Cartons }}{{ item.CtnUnitEN }} {{ item.Quantity }}{{ item.UnitEN }}</div>
                    </td>
                    <td class="amount-cell">
                        <div>{{ Invoice.Currency }}{{ item.TotalPrice }}</div>
                    </td>
                </tr>
                {{ end }}
                <tr class="total-row">
                    <td colspan="2">TOTAL:</td>
                    <td class="amount-cell">{{ Invoice.Currency }}{{ Invoice.TotalAmount }}</td>
                </tr>
            </tbody>
        </table>
    </div>
</body>
</html>
""";
        }

        private static string BuildExportPackingListHtml(string heading)
        {
            string encodedHeading = WebUtility.HtmlEncode(heading);
            return $$$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <style>
        @page { size: A4 portrait; margin: 15mm; }
        body { font-family: Arial, "Microsoft YaHei", sans-serif; font-size: 12px; margin: 0; }
        .report-container { width: 100%; max-width: 1080px; margin: 0 auto; }
        .company { text-align: center; font-weight: bold; font-size: 24px; margin-top: 6px; }
        .company-subtitle { text-align: center; font-size: 13px; margin: 2px 0 12px; }
        .document-title { text-align: center; font-weight: bold; font-size: 22px; margin: 0 0 10px; }
        .top-grid { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
        .top-grid td { padding: 4px 6px; vertical-align: top; }
        .detail-table { width: 100%; border-collapse: collapse; table-layout: fixed; }
        .detail-table th, .detail-table td { border: 1px solid #222; padding: 8px 10px; vertical-align: top; }
        .detail-table th { text-align: left; font-size: 16px; }
        .marks-cell { white-space: pre-line; }
        .total-row td { font-weight: bold; }
        .number-cell { text-align: right; white-space: nowrap; }
    </style>
</head>
<body>
    <div class="report-container">
        <div class="company">{{ Exporter.ExporterNameEN | string.upcase }}</div>
        <div class="company-subtitle">{{ Exporter.AddressEN }}</div>
        <div class="document-title">{{{ encodedHeading }}}</div>

        <table class="top-grid">
            <tr>
                <td style="width:46%;">TO: {{ Customer.CustomerNameEN }}</td>
                <td style="width:24%;"></td>
                <td style="width:30%; text-align:right;">Invoice No.: {{ Invoice.InvoiceNo }}</td>
            </tr>
            <tr>
                <td>{{ Customer.AddressEN }}</td>
                <td></td>
                <td style="text-align:right;">Contract No.: {{ Invoice.ContractNo }}</td>
            </tr>
            <tr>
                <td></td>
                <td></td>
                <td style="text-align:right;">Date: {{ Invoice.InvoiceDate | date.to_string '%d %b %Y' }}</td>
            </tr>
        </table>

        <table class="detail-table">
            <thead>
                <tr>
                    <th>唛头 / Marks</th>
                    <th>货品名称 / Quantities and Descriptions</th>
                    <th>包装 / Package</th>
                    <th>数量 / Quantity</th>
                    <th>毛重 / G.W.</th>
                    <th>净重 / N.W.</th>
                    <th>体积 / Meas.</th>
                </tr>
            </thead>
            <tbody>
                {{ for item in items }}
                {{ if for.first }}
                <tr>
                    <td rowspan="{{ items.size }}" class="marks-cell">{{ Invoice.ShippingMarks }}</td>
                    <td>
                        <div>{{ item.StyleName }}</div>
                        <div>{{ item.PoNumber }}</div>
                        <div>{{ item.StyleNo }}</div>
                    </td>
                    <td>{{ item.Cartons }}{{ item.CtnUnitEN }}</td>
                    <td>{{ item.Quantity }}{{ item.UnitEN }}</td>
                    <td class="number-cell">{{ item.GWTotal }}KGS</td>
                    <td class="number-cell">{{ item.NWTotal }}KGS</td>
                    <td class="number-cell">{{ item.Volume }}CBM</td>
                </tr>
                {{ else }}
                <tr>
                    <td>
                        <div>{{ item.StyleName }}</div>
                        <div>{{ item.PoNumber }}</div>
                        <div>{{ item.StyleNo }}</div>
                    </td>
                    <td>{{ item.Cartons }}{{ item.CtnUnitEN }}</td>
                    <td>{{ item.Quantity }}{{ item.UnitEN }}</td>
                    <td class="number-cell">{{ item.GWTotal }}KGS</td>
                    <td class="number-cell">{{ item.NWTotal }}KGS</td>
                    <td class="number-cell">{{ item.Volume }}CBM</td>
                </tr>
                {{ end }}
                {{ end }}
                <tr class="total-row">
                    <td colspan="2">TOTAL:</td>
                    <td class="number-cell">{{ Invoice.TotalCartons }}</td>
                    <td class="number-cell">{{ Invoice.TotalQuantity }}</td>
                    <td class="number-cell">{{ Invoice.TotalGrossWeight }}KGS</td>
                    <td class="number-cell">{{ Invoice.TotalNetWeight }}KGS</td>
                    <td class="number-cell">{{ Invoice.TotalVolume }}CBM</td>
                </tr>
            </tbody>
        </table>
    </div>
</body>
</html>
""";
        }

        private static string BuildInternalPaymentVoucherHtml(string heading)
        {
            string encodedHeading = WebUtility.HtmlEncode(heading);
            return $$$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <style>
        @page { size: A4 portrait; margin: 15mm; }
        body { font-family: "Microsoft YaHei", SimSun, sans-serif; font-size: 14px; margin: 0; }
        .report-container { width: 100%; max-width: 980px; margin: 0 auto; }
        .company { text-align: center; font-size: 28px; font-weight: bold; margin: 8px 0 6px; }
        .document-title { text-align: center; font-size: 26px; letter-spacing: 2px; margin: 0 0 18px; }
        table { width: 100%; border-collapse: collapse; table-layout: fixed; }
        td { border: 1px solid #222; padding: 10px 8px; vertical-align: middle; }
        .plain-row td { border: none; padding: 4px 8px 10px; }
        .section-title { width: 12%; text-align: center; font-size: 18px; }
        .number { text-align: right; white-space: nowrap; font-weight: bold; }
        .signature-row td { border: none; padding-top: 18px; }
    </style>
</head>
<body>
    <div class="report-container">
        <div class="company">{{ Exporter.ExporterNameCN }}</div>
        <div class="document-title">{{{ encodedHeading }}}</div>

        <table>
            <tr class="plain-row">
                <td style="width:10%;">部门</td>
                <td style="width:18%;">{{ Payment.Department }}</td>
                <td style="width:24%;"></td>
                <td style="width:18%;">业务参考号</td>
                <td style="width:30%;">{{ Payment.InvoiceNo }}</td>
            </tr>
            <tr>
                <td class="section-title" rowspan="4">用<br/>款<br/>事<br/>项</td>
                <td>项目</td>
                <td colspan="2">{{ Payment.Project }}</td>
                <td colspan="2">出货日期 {{ Payment.ShipmentDate | date.to_string '%Y年%m月%d日' }}</td>
            </tr>
            <tr>
                <td>美元</td>
                <td>{{ Payment.USDAmount }}</td>
                <td>人民币（大写）</td>
                <td>{{ cny_amount_upper }}</td>
                <td class="number">￥{{ Payment.CNYAmount }}</td>
            </tr>
            <tr>
                <td colspan="2">支付单位 / 收款人 {{ Payment.PayeeName }}</td>
                <td colspan="3">支付方式 {{ Payment.PaymentMethod }}</td>
            </tr>
            <tr>
                <td colspan="2">开户行 {{ Payment.BankName }}</td>
                <td colspan="3">账号 {{ Payment.AccountNo }}</td>
            </tr>
            <tr>
                <td colspan="6">备注 {{ Payment.Notes }}</td>
            </tr>
            <tr class="signature-row">
                <td colspan="2">业务经理签字</td>
                <td colspan="2" style="text-align:center;">审批</td>
                <td colspan="2" style="text-align:center;">复核</td>
            </tr>
        </table>
    </div>
</body>
</html>
""";
        }

        private static string BuildInternalExpenseReimbursementHtml(string heading)
        {
            string encodedHeading = WebUtility.HtmlEncode(heading);
            return $$$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <style>
        @page { size: A4 portrait; margin: 15mm; }
        body { font-family: "Microsoft YaHei", SimSun, sans-serif; font-size: 14px; margin: 0; }
        .report-container { width: 100%; max-width: 1040px; margin: 0 auto; }
        .company { text-align: center; font-size: 28px; font-weight: bold; margin: 8px 0 6px; }
        .document-title { text-align: center; font-size: 24px; letter-spacing: 6px; margin: 0 0 16px; }
        table { width: 100%; border-collapse: collapse; table-layout: fixed; }
        td { border: 1px solid #222; padding: 10px 8px; vertical-align: middle; text-align: center; }
        .plain-row td { border: none; padding: 4px 8px 10px; text-align: left; }
        .left-label { text-align: left; }
        .amount-cell { text-align: right; white-space: nowrap; font-weight: bold; }
        .signature-row td { border: none; padding-top: 20px; text-align: center; }
    </style>
</head>
<body>
    <div class="report-container">
        <div class="company">{{ Exporter.ExporterNameCN }}</div>
        <div class="document-title">{{{ encodedHeading }}}</div>

        <table>
            <tr class="plain-row">
                <td style="width:14%;">业务科别</td>
                <td style="width:26%;">{{ Payment.Department }}</td>
                <td style="width:24%;"></td>
                <td style="width:18%;">{{ Payment.PaymentDate | date.to_string '%Y年%m月%d日' }}</td>
                <td style="width:18%;"></td>
            </tr>
            <tr>
                <td>项目</td>
                <td>差旅费</td>
                <td>业务招待费</td>
                <td>电话费</td>
                <td>办公费</td>
            </tr>
            <tr>
                <td>金额</td>
                <td>{{ Payment.TravelExpense }}</td>
                <td>{{ Payment.BusinessEntertainmentExpense }}</td>
                <td>{{ Payment.TelephoneExpense }}</td>
                <td>{{ Payment.OfficeExpense }}</td>
            </tr>
            <tr>
                <td>项目</td>
                <td>修理费</td>
                <td>运杂费</td>
                <td>检验费</td>
                <td>其他</td>
            </tr>
            <tr>
                <td>金额</td>
                <td>{{ Payment.RepairExpense }}</td>
                <td>{{ Payment.FreightMiscExpense }}</td>
                <td>{{ Payment.InspectionExpense }}</td>
                <td>{{ Payment.OtherExpense }}</td>
            </tr>
            <tr>
                <td class="left-label">备注</td>
                <td colspan="4" class="left-label">{{ Payment.Notes }}</td>
            </tr>
            <tr>
                <td class="left-label">报销净额</td>
                <td colspan="2" class="left-label">{{ cny_amount_upper }}</td>
                <td>小计</td>
                <td class="amount-cell">￥{{ Payment.CNYAmount }}</td>
            </tr>
            <tr class="signature-row">
                <td>报销人</td>
                <td colspan="2">主管签字</td>
                <td colspan="2">审批签字</td>
            </tr>
        </table>
    </div>
</body>
</html>
""";
        }
    }
}
