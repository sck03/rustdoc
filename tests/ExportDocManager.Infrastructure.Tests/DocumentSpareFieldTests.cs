using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class DocumentSpareFieldTests
{
    [Fact]
    public async Task PaymentFields_PersistIndependentlyOfMatchingInvoice_AndRenderFromTheirOwnCatalog()
    {
        using var dbFactory = new SqliteTestDatabase();
        await using (var db = dbFactory.CreateDbContext())
        {
            db.Invoices.Add(new Invoice
            {
                InvoiceNo = "SAME-REF",
                Spare10 = "报关说明",
                Items = [
                new Item { StyleName = "夹克", Quantity = 10, Spare10 = "棉" },
                new Item { StyleName = "夹克", Quantity = 20, Spare10 = "涤纶" }]
            });
            await db.SaveChangesAsync();
        }
        var service = new PaymentService(dbFactory, TestAccessScope.Create());
        await service.SavePaymentAsync(new Payment
        {
            InvoiceNo = "SAME-REF",
            VoucherNo = " PAY-001 ",
            GoodsName = "夹克合计",
            Quantity = "30",
            QuantityUnit = "件",
            TradeMethod = "T/T",
            TaxRebateRate = "13%",
            Spare4 = "合同补充",
            Spare10 = "付款自定义"
        });
        await using var read = dbFactory.CreateDbContext();
        var saved = await read.Payments.SingleAsync();
        var invoice = await read.Invoices.Include(item => item.Items).SingleAsync();
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal("报关说明", invoice.Spare10);
        Assert.Equal("PAY-001", saved.VoucherNo);
        Assert.Equal("30", saved.Quantity);
        Assert.Equal("付款自定义", saved.Spare10);
        var catalog = new ReportTemplateFieldCatalogService().GetFieldCatalog(ReportDocumentType.PaymentVoucher);
        foreach (var field in new[] { "VoucherNo", "QuantityUnit", "TradeMethod", "TaxRebateRate", "Spare4", "Spare10" })
        {
            var binding = Assert.Single(catalog.Fields, item => item.Value == "{{ Payment." + field + " }}");
            Assert.Equal(typeof(Payment).GetProperty(field)!.GetValue(saved), ScribanReportTemplateRenderer.Render(binding.Value,
                ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(saved, null)));
        }
    }

    [Fact]
    public void AllTenSpareFields_SurviveInvoiceCloning_AndAreAvailableInBothReportDomains()
    {
        var invoice = new Invoice();
        var item = new Item();
        var fields = new ReportTemplateFieldCatalogService();
        for (int index = 1; index <= 10; index++)
        {
            string property = "Spare" + index;
            typeof(Invoice).GetProperty(property)!.SetValue(invoice, "抬头" + index);
            typeof(Item).GetProperty(property)!.SetValue(item, "明细" + index);
            Assert.Equal("抬头" + index, typeof(Invoice).GetProperty(property)!.GetValue(invoice.CloneHeader()));
            Assert.Equal("明细" + index, typeof(Item).GetProperty(property)!.GetValue(item.Clone()));
            foreach (string root in new[] { "Invoice", "item" })
                Assert.Contains(fields.GetFieldCatalog(ReportDocumentType.ExportDocument).Fields, field => field.Value == "{{ " + root + "." + property + " }}");
            Assert.Contains(fields.GetFieldCatalog(ReportDocumentType.PaymentVoucher).Fields, field => field.Value == "{{ Payment." + property + " }}");
        }
        item.UnitPrice = 2.5m;
        invoice.Items = [item];
        var price = Assert.Single(fields.GetFieldCatalog(ReportDocumentType.ExportDocument).Fields,
            field => field.Value.Contains("item.UnitPrice", StringComparison.Ordinal));
        Assert.Equal("2.50", ScribanReportTemplateRenderer.Render("{{ for item in Invoice.Items }}" + price.Value + "{{ end }}",
            ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(invoice, null, null, false)));
    }
}
