using System.Globalization;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Application.Tests;

public sealed class InvoiceProfitAnalysisApplicationTests
{
    [Fact]
    public void Analyze_ShouldReturnStableDisplayTextAcrossServerCultures()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var service = new InvoiceProfitAnalysisService();

            InvoiceProfitAnalysisResult result = service.Analyze(new Invoice
            {
                Currency = "USD",
                TotalAmount = 1234.5m,
                ExchangeRate = 7.2m,
                TotalPurchaseAmount = 8000m,
                TotalTaxRefundAmount = 100m
            });

            Assert.Equal("USD 1,234.50", result.SalesTotalText);
            Assert.Equal("7.2000", result.ExchangeRateText);
            Assert.Contains('.', result.MarginText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
