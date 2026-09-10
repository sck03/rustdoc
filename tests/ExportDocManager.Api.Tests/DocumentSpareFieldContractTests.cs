using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Tests;

public sealed class DocumentSpareFieldContractTests
{
    [Fact]
    public void DocumentDtos_PreserveAllTenSpareFieldsAcrossReadAndSave()
    {
        var invoice = new Invoice { InvoiceNo = "ROUNDTRIP", Items = [new Item()] };
        var payment = new Payment { VoucherNo = "PAY-1", QuantityUnit = "件", TradeMethod = "L/C", TaxRebateRate = "13%" };
        foreach (object record in new object[] { invoice, invoice.Items[0], payment })
            for (int index = 1; index <= 10; index++) record.GetType().GetProperty("Spare" + index)!.SetValue(record, record.GetType().Name + index);
        var invoiceDto = ApiInvoiceDtoFactory.FromInvoiceDetail(invoice);
        var savedInvoice = ApiInvoiceDtoFactory.ToInvoiceForSave(invoiceDto);
        var savedPayment = ApiPaymentDtoFactory.ToPaymentForSave(ApiPaymentDtoFactory.FromPayment(payment));
        foreach (var pair in new[] { (Before: (object)invoice, After: (object)savedInvoice), (invoice.Items[0], savedInvoice.Items![0]), (payment, savedPayment) })
            for (int index = 1; index <= 10; index++)
                Assert.Equal(pair.Before.GetType().GetProperty("Spare" + index)!.GetValue(pair.Before), pair.After.GetType().GetProperty("Spare" + index)!.GetValue(pair.After));
        Assert.Equal(payment.VoucherNo, savedPayment.VoucherNo);
        Assert.Equal(payment.QuantityUnit, savedPayment.QuantityUnit);
        Assert.Equal(payment.TradeMethod, savedPayment.TradeMethod);
        Assert.Equal(payment.TaxRebateRate, savedPayment.TaxRebateRate);
    }
}
