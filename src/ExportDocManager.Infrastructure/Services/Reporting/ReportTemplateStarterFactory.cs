using System.Net;
using System.Text.Json;

namespace ExportDocManager.Services.Reporting;

/// <summary>
/// Creates the smallest useful V3-only A4 starter.  The embedded schema is the
/// persisted design source; the accompanying HTML is its generated render form.
/// </summary>
internal static class ReportTemplateStarterFactory
{
    public const string ExportInvoiceStarterPreset = "export-invoice";
    public const string ExportPackingListStarterPreset = "export-packing-list";
    public const string InternalPaymentVoucherStarterPreset = "internal-payment-voucher";
    public const string InternalExpenseReimbursementStarterPreset = "internal-expense-reimbursement";

    public static string Create(ReportDocumentType reportType, string title, string? templateIdentifier = null)
    {
        string preset = DetermineStarterPreset(reportType, templateIdentifier, title);
        string heading = ResolveHeading(title, preset);
        return BuildV3Html(reportType, preset, heading);
    }

    public static string DetermineStarterPreset(
        ReportDocumentType reportType,
        string? templateIdentifier,
        string? title = null)
    {
        string identity = $"{templateIdentifier} {title}".ToLowerInvariant();
        if (reportType == ReportDocumentType.PaymentVoucher)
        {
            return identity.Contains("expense") || identity.Contains("reimbursement") || identity.Contains("报销")
                ? InternalExpenseReimbursementStarterPreset
                : InternalPaymentVoucherStarterPreset;
        }

        return identity.Contains("packing") || identity.Contains("装箱")
            ? ExportPackingListStarterPreset
            : ExportInvoiceStarterPreset;
    }

    private static string ResolveHeading(string title, string preset)
    {
        if (!string.IsNullOrWhiteSpace(title) && !LooksLikeTechnicalTemplateName(title.ToLowerInvariant()))
        {
            return title.Trim();
        }

        return preset switch
        {
            ExportPackingListStarterPreset => "PACKING LIST",
            InternalPaymentVoucherStarterPreset => "付款单（费用支付专用）",
            InternalExpenseReimbursementStarterPreset => "费用报销明细单",
            _ => "INVOICE"
        };
    }

    private static bool LooksLikeTechnicalTemplateName(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("invoice") ||
        value.Contains("packing") ||
        value.Contains("payment") ||
        value.Contains("voucher") ||
        value.Contains("expense") ||
        value.Contains("reimbursement") ||
        value.Contains('_');

    private static string BuildV3Html(ReportDocumentType reportType, string preset, string heading)
    {
        bool isPayment = reportType == ReportDocumentType.PaymentVoucher;
        string organizationField = isPayment ? "Payment.PayerName" : "Exporter.ExporterNameEN";
        string counterpartyField = isPayment ? "Payment.PayeeName" : "Customer.CustomerNameEN";
        string referenceField = isPayment ? "Payment.InvoiceNo" : "Invoice.InvoiceNo";
        string amountField = isPayment ? "Payment.CNYAmount" : "Invoice.TotalAmount";
        string schema = JsonSerializer.Serialize(new
        {
            version = 3,
            reportType = reportType.ToString(),
            page = new
            {
                size = "A4",
                orientation = "Portrait",
                widthHundredthMm = 21000,
                heightHundredthMm = 29700,
                marginTopHundredthMm = 800,
                marginRightHundredthMm = 1000,
                marginBottomHundredthMm = 800,
                marginLeftHundredthMm = 1000,
                fontFamily = "Arial, Noto Sans CJK SC, Microsoft YaHei",
                fontSizePt = 9
            },
            grid = new { enabled = true, sizeHundredthMm = 500, snap = true },
            layers = new object[]
            {
                Layer("header", "页眉", "Header", true, true, false, 2600, new object[]
                {
                    Text("title", heading, 1000, 900, 19000, 1000, 16, true, "Center"),
                    Field("organization", organizationField, 1000, 2100, 19000, 700, 10, true, "Center")
                }),
                Layer("body", "主体", "Body", false, false, false, 0, new object[]
                {
                    Field("counterparty", counterpartyField, 1000, 3600, 9000, 700, 9, false, "Left", "往来方"),
                    Field("reference", referenceField, 11000, 3600, 8000, 700, 9, false, "Right", "单据号"),
                    Field("amount", amountField, 11000, 4600, 8000, 700, 9, true, "Right", isPayment ? "金额" : "总金额")
                }),
                Layer("footer", "页脚", "Footer", true, true, true, 800, Array.Empty<object>()),
                Layer("overlay", "覆盖层", "Overlay", false, false, false, 0, Array.Empty<object>())
            }
        });

        string encodedHeading = WebUtility.HtmlEncode(heading);
        const string template = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    @page { size: A4 portrait; margin: 0; }
    html, body { margin: 0; padding: 0; }
    body { font-family: Arial, 'Noto Sans CJK SC', 'Microsoft YaHei', sans-serif; color: #1f2933; }
    .edm-v3-page { position: relative; width: 210mm; min-height: 297mm; margin: 0 auto; box-sizing: border-box; }
    .edm-v3-title { position: absolute; top: 9mm; left: 10mm; width: 190mm; font-size: 16pt; font-weight: 700; text-align: center; }
    .edm-v3-organization { position: absolute; top: 21mm; left: 10mm; width: 190mm; font-size: 10pt; font-weight: 700; text-align: center; }
    .edm-v3-row { position: absolute; top: 36mm; left: 10mm; right: 10mm; display: flex; justify-content: space-between; gap: 8mm; }
    .edm-v3-amount { position: absolute; top: 46mm; right: 10mm; width: 80mm; font-weight: 700; text-align: right; }
  </style>
</head>
<body>
<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA
__SCHEMA__
-->
  <main class="edm-v3-page">
    <div class="edm-v3-title">__HEADING__</div>
    <div class="edm-v3-organization">{{ __ORGANIZATION__ }}</div>
    <div class="edm-v3-row"><span>{{ __COUNTERPARTY__ }}</span><span>{{ __REFERENCE__ }}</span></div>
    <div class="edm-v3-amount">{{ __AMOUNT__ }}</div>
  </main>
</body>
</html>
""";
        return template
            .Replace("__SCHEMA__", schema, StringComparison.Ordinal)
            .Replace("__HEADING__", encodedHeading, StringComparison.Ordinal)
            .Replace("__ORGANIZATION__", organizationField, StringComparison.Ordinal)
            .Replace("__COUNTERPARTY__", counterpartyField, StringComparison.Ordinal)
            .Replace("__REFERENCE__", referenceField, StringComparison.Ordinal)
            .Replace("__AMOUNT__", amountField, StringComparison.Ordinal);
    }

    private static object Layer(
        string id,
        string name,
        string role,
        bool repeatOnEveryPage,
        bool keepTogether,
        bool pinToPageBottom,
        int minHeightHundredthMm,
        object[] elements)
    {
        return new
        {
            id,
            name,
            role,
            print = new { repeatOnEveryPage, keepTogether, pinToPageBottom, minHeightHundredthMm },
            visible = true,
            locked = false,
            elements
        };
    }

    private static object Text(string id, string text, int x, int y, int width, int height, int fontSize, bool bold, string align)
    {
        return new
        {
            id,
            type = "Text",
            text,
            xHundredthMm = x,
            yHundredthMm = y,
            widthHundredthMm = width,
            heightHundredthMm = height,
            rotationDeg = 0,
            zIndex = 0,
            visible = true,
            locked = false,
            outputEnabled = true,
            style = new { fontSizePt = fontSize, bold, align }
        };
    }

    private static object Field(
        string id,
        string fieldPath,
        int x,
        int y,
        int width,
        int height,
        int fontSize,
        bool bold,
        string align,
        string? label = null)
    {
        return new
        {
            id,
            type = "Field",
            fieldPath,
            label,
            xHundredthMm = x,
            yHundredthMm = y,
            widthHundredthMm = width,
            heightHundredthMm = height,
            rotationDeg = 0,
            zIndex = 0,
            visible = true,
            locked = false,
            outputEnabled = true,
            style = new { fontSizePt = fontSize, bold, align }
        };
    }
}
