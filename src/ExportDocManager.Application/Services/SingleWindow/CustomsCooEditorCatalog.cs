using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooEditorCatalog
    {
        public static IReadOnlyList<SelectionOption<string>> ApplyTypeOptions { get; } =
        [
            new("0", "0：暂存"),
            new("1", "1：申报")
        ];

        public static IReadOnlyList<SelectionOption<string>> CertStatusOptions { get; } =
        [
            new("0", "0：新证"),
            new("1", "1：更改证"),
            new("2", "2：重发证"),
            new("3", "3：更改重发证")
        ];

        public static IReadOnlyList<SelectionOption<string>> CertTypeOptions { get; } =
        [
            new("C", "C：一般原产地证"),
            new("G", "G：普惠制原产地证"),
            new("B", "B：亚太证书"),
            new("E", "E：东盟证书"),
            new("F", "F：中国-智利证书"),
            new("M", "M：欧盟蘑菇证书"),
            new("P", "P：中巴证书"),
            new("T", "T：烟草证书"),
            new("N", "N：新西兰证书"),
            new("X", "X：新加坡证书"),
            new("R", "R：中国-秘鲁证书"),
            new("H", "H：海峡两岸证书"),
            new("L", "L：中国-哥斯达黎加证书"),
            new("I", "I：中国-冰岛证书"),
            new("S", "S：中国-瑞士证书"),
            new("PR", "PR：加工装配证书"),
            new("TR", "TR：转口证书"),
            new("A", "A：中国-澳大利亚证书"),
            new("K", "K：中国-韩国证书"),
            new("AD", "AD：输往墨西哥瓷砖价格承诺证书"),
            new("AP", "AP：输往巴基斯坦瓷砖价格承诺证书"),
            new("GE", "GE：中国-格鲁吉亚证书"),
            new("MU", "MU：中国-毛里求斯证书"),
            new("CA", "CA：中国-柬埔寨证书"),
            new("RC", "RC：RCEP证书"),
            new("NI", "NI：中国-尼加拉瓜证书"),
            new("EC", "EC：中国-厄瓜多尔证书"),
            new("SE", "SE：中国-塞尔维亚证书"),
            new("HD", "HD：中国-洪都拉斯证书"),
            new("MV", "MV：中国-马尔代夫证书"),
            new("CG", "CG：中国-刚果证书")
        ];

        public static IReadOnlyList<SelectionOption<string>> ProducerSecretOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("N", "N：否"),
            new("Y", "Y：是")
        ];

        public static IReadOnlyList<SelectionOption<string>> ExhibitFlagOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("0", "0：非展览"),
            new("1", "1：展览")
        ];

        public static IReadOnlyList<SelectionOption<string>> ThirdPartyInvoiceOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("0", "0：非第三方发票"),
            new("1", "1：第三方发票")
        ];

        public static IReadOnlyList<SelectionOption<string>> PredictFlagOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("0", "0：非预计离港"),
            new("1", "1：预计离港")
        ];

        public static IReadOnlyList<SelectionOption<string>> PromiseOptions { get; } =
        [
            new("1", "1：申请企业承诺")
        ];

        public static IReadOnlyList<SelectionOption<string>> CurrencyOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("CNY", "CNY：人民币"),
            new("USD", "USD：美元"),
            new("EUR", "EUR：欧元"),
            new("HKD", "HKD：港币"),
            new("GBP", "GBP：英镑"),
            new("JPY", "JPY：日元"),
            new("KRW", "KRW：韩元"),
            new("CAD", "CAD：加元"),
            new("AUD", "AUD：澳元"),
            new("CHF", "CHF：瑞郎"),
            new("SGD", "SGD：新加坡元")
        ];

        public static IReadOnlyList<SelectionOption<string>> CooTradeModeOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("0", "0：其他贸易方式"),
            new("1", "1：一般贸易"),
            new("2", "2：来料加工"),
            new("3", "3：进料加工"),
            new("4", "4：外商投资"),
            new("5", "5：易货贸易"),
            new("6", "6：补偿贸易"),
            new("7", "7：边境贸易"),
            new("8", "8：展卖贸易"),
            new("9", "9：零售贸易"),
            new("10", "10：无偿援助"),
            new("33", "33：边民互市"),
            new("34", "34：小额贸易")
        ];

        public static IReadOnlyList<SelectionOption<string>> GoodsItemFlagOptions { get; } =
        [
            new(CustomsCooGoodsItemFlagCatalog.GoodsCode, "N：货物项"),
            new(CustomsCooGoodsItemFlagCatalog.NonGoodsCode, "Y：非货物项")
        ];

        public static IReadOnlyList<SelectionOption<string>> PackTypeOptions { get; } =
        [
            new(CustomsCooPackTypeCatalog.RegularCode, "1：常规包装"),
            new(CustomsCooPackTypeCatalog.IrregularCode, "2：非常规包装")
        ];

        public static IReadOnlyList<SelectionOption<string>> GoodsTaxRateOptions { get; } =
        [
            new(string.Empty, string.Empty),
            new("0", "0：非最高税率"),
            new("1", "1：相关缔约方最高税率"),
            new("2", "2：全部缔约方最高税率")
        ];
    }
}
