using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core;

public sealed record InvoiceReviewIssue(string Field, int? RowNumber, string Message);
public sealed record InvoiceReviewResult(bool Ready, IReadOnlyList<InvoiceReviewIssue> Issues);

/// <summary>One readiness policy for the review UI and the verified-state transition.</summary>
public static class InvoiceReviewPolicy
{
    public static InvoiceReviewResult Evaluate(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var issues = new List<InvoiceReviewIssue>();
        void Required(string? value, string field, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) issues.Add(new(field, null, $"请填写{label}。"));
        }
        Required(invoice.CustomerNameEN, "customerNameEN", "客户英文名称");
        Required(invoice.ExporterNameEN, "exporterNameEN", "出口商英文名称");
        if (invoice.NotifyPartyMode == NotifyPartyMode.Separate) Required(invoice.NotifyPartyName, "notifyPartyName", "独立通知人名称");
        if (invoice.TotalAmount != 0) Required(invoice.Currency, "currency", "币种");
        if (invoice.Items is not { Count: > 0 }) issues.Add(new("items", null, "至少需要一行商品明细。"));
        else
        {
            for (int index = 0; index < invoice.Items.Count; index++)
            {
                var item = invoice.Items[index];
                int row = index + 1;
                if (string.IsNullOrWhiteSpace(item.StyleName) && string.IsNullOrWhiteSpace(item.StyleNameCN))
                    issues.Add(new("styleName", row, "请填写英文或中文品名。"));
                if (item.Quantity <= 0) issues.Add(new("quantity", row, "数量必须大于 0。"));
                if (item.GWTotal > 0 && item.NWTotal > item.GWTotal)
                    issues.Add(new("nwTotal", row, "净重不能大于毛重。"));
            }
        }
        return new InvoiceReviewResult(issues.Count == 0, issues);
    }
}
