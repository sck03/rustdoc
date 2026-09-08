using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;

namespace ExportDocManager.Application.Tests;

public sealed class InvoiceReviewPolicyTests
{
    [Fact]
    public void Review_ShouldDescribeIncompleteDraftsWithoutChangingTheirValues()
    {
        var invoice = new Invoice
        {
            NotifyPartyMode = NotifyPartyMode.Separate,
            Items = [new Item { StyleNo = "CUSTOM-STYLE", Quantity = 0, TotalPrice = 37m }]
        };
        var review = InvoiceReviewPolicy.Evaluate(invoice);
        Assert.False(review.Ready);
        Assert.Contains(review.Issues, issue => issue.Field == "notifyPartyName" && issue.RowNumber == null);
        Assert.Contains(review.Issues, issue => issue.Field == "quantity" && issue.RowNumber == 1);
        Assert.Contains(review.Issues, issue => issue.Field == "styleName" && issue.RowNumber == 1);
        Assert.Equal(37m, invoice.Items[0].TotalPrice);
    }

    [Theory]
    [InlineData("六角螺栓", "")]
    [InlineData("", "CUSTOMER NEW DRESS")]
    public void Review_ShouldAcceptStandardAndOneOffProductsWithoutMasterDataLinks(string chinese, string english)
    {
        var invoice = new Invoice
        {
            CustomerNameEN = "Customer",
            ExporterNameEN = "Exporter",
            Items = [new Item { StyleNameCN = chinese, StyleName = english, Quantity = 100 }]
        };
        Assert.True(InvoiceReviewPolicy.Evaluate(invoice).Ready);
        invoice.Items[0].GWTotal = 10;
        invoice.Items[0].NWTotal = 11;
        Assert.Contains(InvoiceReviewPolicy.Evaluate(invoice).Issues, issue => issue.Field == "nwTotal");
    }
}
