namespace ExportDocManager.Models
{
    public class ExchangeRateInfo
    {
        public string CurrencyName { get; set; } // 货币名称
        public decimal? BuyingRate { get; set; }  // 现汇买入价
        public decimal? CashBuyingRate { get; set; } // 现钞买入价
        public decimal? SellingRate { get; set; } // 现汇卖出价
        public decimal? CashSellingRate { get; set; } // 现钞卖出价
        public decimal? MiddleRate { get; set; } // 中行折算价
        public string PublishTime { get; set; } // 发布时间
    }
}
