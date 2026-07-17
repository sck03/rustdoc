namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiInvoiceProfitAnalysisRequest(
        ApiInvoiceDetailDto Invoice);

    public sealed record ApiInvoiceProfitAnalysisResponse(
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
        string MarginText,
        string StoragePolicy);
}
