using ExportDocManager.Models;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Application.Tests;

public sealed class DocumentFieldLabelSettingsTests
{
    [Fact]
    public void NamesAreNormalizedPerDomain_WithoutChangingTemplateBindings()
    {
        var labels = DocumentFieldLabelSettings.Normalize(new()
        {
            Invoice = new() { ["spare1"] = " 船名航次 ", ["spare2"] = "Cafe\u0301", ["spare3"] = " " },
            Item = new() { ["spare1"] = "材质规格", ["spare10"] = "客户货号" },
            Payment = new() { ["spare1"] = "船名航次" }
        });
        Assert.Equal("Café", labels.Invoice["spare2"]);
        Assert.False(labels.Invoice.ContainsKey("spare3"));
        var catalog = new ReportTemplateFieldCatalogService();
        var invoice = catalog.GetFieldCatalog(ReportDocumentType.ExportDocument, labels);
        var payment = catalog.GetFieldCatalog(ReportDocumentType.PaymentVoucher, labels);
        Assert.Contains(invoice.Fields, field => field.Label == "船名航次 (Invoice.Spare1)" && field.Value == "{{ Invoice.Spare1 }}");
        Assert.Contains(invoice.Fields, field => field.Label == "材质规格 (item.Spare1)" && field.Value == "{{ item.Spare1 }}");
        Assert.Contains(invoice.Fields, field => field.Label == "客户货号 (item.Spare10)" && field.Value == "{{ item.Spare10 }}");
        Assert.Contains(payment.Fields, field => field.Label == "船名航次 (Payment.Spare1)" && field.Value == "{{ Payment.Spare1 }}");
        Assert.Contains(catalog.GetFieldCatalog(ReportDocumentType.ExportDocument).Fields, field => field.Label == "备用 1 (Invoice.Spare1)");
    }

    [Theory]
    [InlineData("spare11", "越界")]
    [InlineData("Spare1", "错误字段")]
    [InlineData("spare1", "不可\n换行")]
    [InlineData("spare1", "不可\u202e隐藏方向")]
    [InlineData("spare1", "备用 2")]
    public void InvalidKeysOrAmbiguousLabelsAreRejected(string key, string label) =>
        Assert.Throws<ArgumentException>(() => DocumentFieldLabelSettings.Normalize(new() { Invoice = new() { [key] = label } }));

    [Fact]
    public void OverlongAndDuplicateLabelsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DocumentFieldLabelSettings.Normalize(new() { Item = new() { ["spare10"] = new string('名', 41) } }));
        Assert.Throws<ArgumentException>(() => DocumentFieldLabelSettings.Normalize(new() { Payment = new() { ["spare1"] = "PO", ["spare2"] = "po" } }));
    }
}
