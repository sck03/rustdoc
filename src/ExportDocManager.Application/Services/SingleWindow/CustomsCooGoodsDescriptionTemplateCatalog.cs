using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public readonly record struct CustomsCooGoodsDescriptionActionState(
        string ButtonText,
        string ToolTipText,
        bool IsEnabled);

    public static class CustomsCooGoodsDescriptionTemplateCatalog
    {
        private const string DefaultButtonText = "生成货物描述";

        public static CustomsCooGoodsDescriptionActionState ResolveActionState(string goodsItemFlag, string packType)
        {
            bool isNonGoods = CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag);
            bool isIrregularPack = CustomsCooPackTypeCatalog.IsIrregular(packType);

            return new CustomsCooGoodsDescriptionActionState(
                DefaultButtonText,
                ResolveToolTipText(isNonGoods, isIrregularPack),
                true);
        }

        public static string BuildTemplate(
            string packQty,
            string packUnit,
            string goodsNameEnglish,
            string goodsNameChinese = "")
        {
            string normalizedGoodsName = FirstNonEmpty(
                NormalizeUpperText(goodsNameEnglish),
                NormalizeUpperText(goodsNameChinese));
            if (string.IsNullOrWhiteSpace(normalizedGoodsName))
            {
                return string.Empty;
            }

            string packSummary = BuildPackingSummary(packQty, packUnit);
            if (string.IsNullOrWhiteSpace(packSummary))
            {
                return string.Empty;
            }

            return $"{packSummary} OF {normalizedGoodsName}".Trim();
        }

        public static string BuildPackingSummary(string packQty, string packUnit)
        {
            string normalizedPackQty = NormalizeText(packQty);
            string unitText = ResolveGoodsDescriptionPackUnit(packUnit, normalizedPackQty);
            if (string.IsNullOrWhiteSpace(normalizedPackQty) || string.IsNullOrWhiteSpace(unitText))
            {
                return string.Empty;
            }

            string quantityText = BuildGoodsDescriptionQuantityText(normalizedPackQty);
            return string.IsNullOrWhiteSpace(quantityText)
                ? $"{normalizedPackQty} {unitText}".Trim()
                : $"{quantityText} {unitText}".Trim();
        }

        public static string GetFailureMessage(string goodsItemFlag, string packType)
        {
            bool isNonGoods = CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag);
            bool isIrregularPack = CustomsCooPackTypeCatalog.IsIrregular(packType);

            if (isNonGoods || isIrregularPack)
            {
                return "请先确认当前货项已经填了包装件数、包装单位/形式(英)和英文性质/名称，再点“生成货物描述”；挂装、散装、裸装可使用 HANGING GARMENT、IN BULK、PCS IN NUDE 等官方英文口径。";
            }

            return "请先确认当前货项已经填了包装件数、包装单位(英)和英文品名，再点“生成货物描述”。";
        }

        private static string BuildGoodsDescriptionQuantityText(string packQty)
        {
            decimal quantity = NumberHelper.ParseDecimal(packQty);
            if (quantity <= 0)
            {
                return string.Empty;
            }

            if (quantity == decimal.Truncate(quantity) && quantity <= 999999999m)
            {
                long integerQuantity = (long)quantity;
                return $"{NumberHelper.ToEnglishWords(integerQuantity)} ({integerQuantity})";
            }

            return NormalizeText(packQty);
        }

        private static string ResolveGoodsDescriptionPackUnit(string packUnit, string packQty)
        {
            string normalizedUnit = SingleWindowUnitNormalizer.NormalizeEnglish(packUnit);
            decimal quantity = NumberHelper.ParseDecimal(packQty);
            bool singular = quantity == 1m;

            return normalizedUnit switch
            {
                "CTN" => singular ? "CARTON" : "CARTONS",
                "PCS" => singular ? "PIECE" : "PIECES",
                "SET" => singular ? "SET" : "SETS",
                "BOX" => singular ? "BOX" : "BOXES",
                "KGS" => singular ? "KILOGRAM" : "KILOGRAMS",
                _ => NormalizeUpperText(packUnit)
            };
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? [])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string NormalizeUpperText(string value) => NormalizeText(value).ToUpperInvariant();

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string ResolveToolTipText(bool isNonGoods, bool isIrregularPack)
        {
            if (isNonGoods || isIrregularPack)
            {
                return "按当前货项的包装件数、包装单位/形式和英文性质/名称生成官方货物描述，例如 ELEVEN (11) PCS IN NUDE OF T SHIRT；PACKING 总计应作为最后一项货物信息后的总计说明处理。";
            }

            return "按当前货项的包装件数、包装单位和英文品名生成标准货物描述，例如 EIGHTEEN (18) CARTONS OF MEN'S T-SHIRT。";
        }
    }
}
