namespace ExportDocManager.Utils
{
    /// <summary>
    /// Application-wide constants and predefined data.
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// Supported payment methods for payment vouchers.
        /// </summary>
        public static readonly string[] PaymentMethods = { "支票", "电汇", "预付" };

        /// <summary>
        /// Default payment method for new payment records.
        /// </summary>
        public const string DefaultPaymentMethod = "电汇";

        /// <summary>
        /// Common payment terms.
        /// </summary>
        public static readonly string[] PaymentTerms = { "T/T", "L/C", "D/P", "D/A", "O/A" };

        /// <summary>
        /// Common transport modes.
        /// </summary>
        public static readonly string[] TransportModes = { "BY SEA", "BY AIR", "BY TRAIN", "BY DHL", "BY FedEx" };

        /// <summary>
        /// Common currencies.
        /// </summary>
        public static readonly string[] Currencies = { "USD", "EUR", "CNY", "GBP", "JPY" };

        /// <summary>
        /// Customs supervision modes.
        /// </summary>
        public static readonly string[] SupervisionModes = { "一般贸易", "来料加工", "进料加工", "补偿贸易", "易货贸易", "寄售代销", "边境小额贸易", "加工贸易", "保税仓库", "出料加工" };

        /// <summary>
        /// Trade data types.
        /// </summary>
        public static readonly string[] TradeTypes = { "报关数据", "实际数据" };
    }
}
