using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Reporting
{
    public interface IInvoiceProfitAnalysisService
    {
        InvoiceProfitAnalysisResult Analyze(Invoice invoice);
    }

    public sealed class InvoiceProfitAnalysisService : IInvoiceProfitAnalysisService
    {
        public InvoiceProfitAnalysisResult Analyze(Invoice invoice)
        {
            if (invoice == null)
            {
                return InvoiceProfitAnalysisResult.Empty();
            }

            decimal salesTotal = invoice.TotalAmount;
            decimal rate = invoice.ExchangeRate ?? 0m;
            decimal salesRmb = salesTotal * rate;
            decimal purchaseCost = invoice.TotalPurchaseAmount;
            decimal taxRefund = invoice.TotalTaxRefundAmount;
            decimal grossProfit = 0m;

            if (rate > 0)
            {
                grossProfit = salesRmb - purchaseCost + taxRefund;
            }
            else if (IsRmbCurrency(invoice.Currency))
            {
                rate = 1m;
                salesRmb = salesTotal;
                grossProfit = salesRmb - purchaseCost + taxRefund;
            }

            decimal margin = salesRmb > 0 ? grossProfit / salesRmb : 0m;

            return new InvoiceProfitAnalysisResult(
                invoice.Currency ?? string.Empty,
                salesTotal,
                rate > 0 ? rate : null,
                salesRmb,
                purchaseCost,
                taxRefund,
                grossProfit,
                margin,
                $"{invoice.Currency} {salesTotal:N2}",
                rate > 0 ? rate.ToString("N4") : "未设置",
                $"¥ {salesRmb:N2}",
                $"- ¥ {purchaseCost:N2}",
                $"+ ¥ {taxRefund:N2}",
                $"¥ {grossProfit:N2}",
                $"{margin:P2}");
        }

        private static bool IsRmbCurrency(string currency)
        {
            return string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currency, "RMB", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record InvoiceProfitAnalysisResult(
        string Currency,
        decimal SalesTotal,
        decimal? ExchangeRate,
        decimal SalesRmb,
        decimal PurchaseCost,
        decimal TaxRefund,
        decimal GrossProfit,
        decimal Margin,
        string SalesTotalText,
        string ExchangeRateText,
        string SalesRmbText,
        string PurchaseCostText,
        string TaxRefundText,
        string GrossProfitText,
        string MarginText)
    {
        public static InvoiceProfitAnalysisResult Empty()
        {
            return new InvoiceProfitAnalysisResult(
                string.Empty,
                0m,
                null,
                0m,
                0m,
                0m,
                0m,
                0m,
                "0.00",
                "未设置",
                "¥ 0.00",
                "- ¥ 0.00",
                "+ ¥ 0.00",
                "¥ 0.00",
                "0.00%");
        }
    }
}
