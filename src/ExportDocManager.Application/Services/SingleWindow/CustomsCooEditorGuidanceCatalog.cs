
namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooEditorGuidanceCatalog
    {
        public static string GetHeaderCueText(string propertyName)
        {
            return propertyName switch
            {
                "EntMgrNo" => "企业编号，按海关原产地证口径填写",
                "CiqRegNo" => "出口商代码/备案号",
                "AplRegNo" => "录入企业代码",
                "ApplTel" => "申报员联系电话",
                "OrgCode" => "常用 4 位关区代码，例如 3101",
                "FetchPlace" => "常用 4 位关区代码，例如 3101",
                "AplAdd" => "机构所在地英文，例如 NINGBO, CHINA",
                "InvDate" => "格式 YYYY-MM-DD，例如 2026-04-29",
                "AplDate" => "格式 YYYY-MM-DD，例如 2026-04-29",
                "IntendExpDate" => "格式 YYYY-MM-DD，例如 2026-04-29",
                "ExpDeclDate" => "格式 YYYY-MM-DD，例如 2026-04-29",
                "ChkValidDate" => "格式 YYYY-MM-DD，例如 2026-04-29",
                "DestCountryCode" => "进口国代码，例如 360",
                "TransCountryCode" => "中转国代码，例如 702",
                "FobValue" => "整数不超过 19 位，小数不超过 5 位",
                "TotalAmt" => "整数不超过 19 位，小数不超过 5 位",
                "PriceTerms" => "价格条款，例如 FOB / CIF",
                "Curr" => "币制，例如 USD / CNY / EUR",
                "OriCountryCode" => "原产国代码，例如 156",
                "EntryId" => "报关单号，可多值用 ; 分隔",
                "AplPromiseCode" => "企业承诺代码，默认 1",
                "CertNo" => "可只填后 4 位流水号；前缀会按证型 + 年份 + 企业组织机构代码自动补齐",
                "GoodsSpecClause" => "特殊条款（商品描述），可按官方口径填写补充说明",
                "Mark" => "未填写时通常按 N/M 处理",
                "Note" => "申请书备注",
                "SpecInvTerms" => "发票特殊条款，RCEP 等证书可能会用到",
                "Remark" => "证书备注信息，仅部分协定证书使用",
                "Producer" => "证书货物生产商描述；部分协定证书使用，中厄第三方发票时必填",
                "OldCertNo" => "更改证/重发证时填写原证书号",
                "ModReason" => "更改证或重发证的原因说明",
                _ => string.Empty
            };
        }

        public static string GetHeaderToolTip(string propertyName)
        {
            return propertyName switch
            {
                "TransDetails" => "运输细节会优先按启运港、卸货港、转运港和运输方式生成，例如 FROM NINGBO TO JAKARTA BY SEA。当前仅一般原产地证和普惠制原产地证按录入口径标黄提示。",
                "TradeModeCode" => "这里使用海关原产地证贸易方式代码，常见一般贸易为 1；不要填写报关代理委托中的 0110。",
                _ => string.Empty
            };
        }

        public static string GetGoodsDetailCueText(
            string propertyName,
            string certType,
            string originCriteria,
            string goodsItemFlag = "",
            string packType = "")
        {
            return propertyName switch
            {
                "OriCriteria" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaCueText(certType),
                "OriCriteriaSub" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaSubCueText(certType, originCriteria),
                "OriCriteriaRef" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaRefCueText(certType, originCriteria),
                "GoodsItemFlag" => "N=货物项，Y=非货物项",
                "GoodsName" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "非货物项模式下改为只读参考"
                    : string.Empty,
                "GoodsNameE" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "非货物项模式下请填写英文性质/名称"
                    : string.Empty,
                "PackQty" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "生成货物描述时会直接使用这里的数量"
                    : CustomsCooPackTypeCatalog.IsIrregular(packType)
                        ? "非常规包装时请按实际包装数量填写"
                        : string.Empty,
                "PackUnit" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "混装可用 PACKAGES / HANGING GARMENT / IN BULK / PCS IN NUDE"
                    : string.Empty,
                "PackType" => "普通纸箱/箱装保持 1",
                "CiqRegNo" => "长度<=10，例如 91330200A1",
                "PrdcEtpsName" => "长度<=400，例如 宁波某某服饰有限公司",
                "PrdcEtpsConcEr" => "长度<=20，例如 王小明",
                "PrdcEtpsTel" => "长度<=20，例如 0574-12345678",
                "GoodsDesc" => GetGoodsDescriptionCueText(goodsItemFlag, packType),
                "GoodsOriginCountry" => "仅协定证书使用，例如 156 / 360",
                "GoodsOriginCountryEn" => "仅协定证书使用，例如 CHINA / INDONESIA",
                "InvNo" => "仅协定证书使用，通常回填当前发票号",
                "GoodsTaxRate" => "RCEP 最高税率标志",
                _ => string.Empty
            };
        }

        public static string GetGoodsDetailToolTip(
            string propertyName,
            string certType,
            string originCriteria,
            string goodsItemFlag = "",
            string packType = "")
        {
            return propertyName switch
            {
                "OriCriteria" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaHelpText(certType) + " 这项在同票多行常常一致，可用右上按钮复制到后续项。",
                "OriCriteriaSub" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaSubHelpText(certType, originCriteria),
                "OriCriteriaRef" => CustomsCooOriginCriteriaCatalog.GetOriginCriteriaRefHelpText(certType, originCriteria),
                "GoodsItemFlag" => "货项标志只控制当前行是否按“非货物项”口径提示。切到 Y 时，系统会提醒你把条目性质写清楚，但不会额外生成新的官方字段。",
                "GoodsName" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "当前为非货物项时，这里按官方网页口径改成只读参考，避免继续把普通商品中文名当成真正要申报的混装末项内容。"
                    : string.Empty,
                "GoodsNameE" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "当前为非货物项时，这里仍需按官方网页口径填写英文性质/名称，例如 MIXED GARMENTS、PACKING MATERIALS 等；不要继续沿用普通货物项的自动句式。"
                    : string.Empty,
                "PackQty" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "当前为非货物项/混装行时，生成货物描述会直接使用这里的数量。"
                    : CustomsCooPackTypeCatalog.IsIrregular(packType)
                        ? "当前为非常规包装时，请按实际包装数量填写。"
                        : string.Empty,
                "PackUnit" => CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag)
                    ? "混装末项建议直接按官方英文口径填写包装单位/形式：多种包装可写 PACKAGES，挂装写 HANGING GARMENT，散装写 IN BULK，裸装写 PCS IN NUDE / SETS IN NUDE / UNITS IN NUDE。"
                    : string.Empty,
                "PackType" => "官方 PackType 字段，表示包装是否为常规包装；不是 CTN/BOX 这类包装单位。普通出口纸箱通常选 1：常规包装，只有特殊或非常规包装才选 2。",
                "CiqRegNo" => "生产企业组织机构代码，官方表体必填，最大长度 10。",
                "PrdcEtpsName" => "生产企业名称，官方表体必填，最大长度 400。",
                "PrdcEtpsConcEr" => "生产企业联系人，建议填制单时能直接联系到的负责人，最大长度 20。",
                "PrdcEtpsTel" => "生产企业联系电话，官方表体必填，最大长度 20。",
                "GoodsDesc" => GetGoodsDescriptionToolTip(goodsItemFlag, packType),
                "GoodsOriginCountry" => "协定原产国代码，仅 RCEP 等协定证型使用；如果来源值不能识别成正式国家，系统会留空待你确认。",
                "GoodsOriginCountryEn" => "协定原产国英文，仅 RCEP 等协定证型使用；系统不会再把“宁波其他”这类内部口径误带到这里。",
                "InvNo" => "明细发票号，仅协定证书使用。",
                "GoodsTaxRate" => "最高税率标志，仅部分协定证书使用。",
                _ => string.Empty
            };
        }

        public static string GetGoodsDetailLabelText(
            string propertyName,
            string defaultLabelText,
            string goodsItemFlag,
            string packType)
        {
            if (string.Equals(propertyName, "GoodsName", StringComparison.Ordinal) &&
                CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag))
            {
                return "中文性质/名称(只读)";
            }

            if (string.Equals(propertyName, "GoodsNameE", StringComparison.Ordinal) &&
                CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag))
            {
                return "英文性质/名称";
            }

            if (string.Equals(propertyName, "PackUnit", StringComparison.Ordinal) &&
                (CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag) ||
                 CustomsCooPackTypeCatalog.IsIrregular(packType)))
            {
                return "包装单位/形式(英)";
            }

            if (!string.Equals(propertyName, "GoodsDesc", StringComparison.Ordinal))
            {
                return defaultLabelText ?? string.Empty;
            }

            if (CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag))
            {
                return "货物描述/非货物项说明";
            }

            return CustomsCooPackTypeCatalog.IsIrregular(packType)
                ? "货物描述/非常规包装说明"
                : defaultLabelText ?? string.Empty;
        }

        public static string GetGoodsDetailContextSummary(string goodsItemFlag, string packType)
        {
            bool isNonGoods = CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag);
            bool isIrregularPack = CustomsCooPackTypeCatalog.IsIrregular(packType);

            if (isNonGoods && isIrregularPack)
            {
                return "当前货项按“非货物项 + 非常规包装”处理：普通货物字段会禁用；请确认包装件数、包装单位/形式和英文性质/名称，再生成官方货物描述。";
            }

            if (isNonGoods)
            {
                return "当前货项为非货物项：中文名和标准数量单位等普通货物字段会锁定；重点填写英文性质/名称、包装件数和包装单位/形式，并在“货物描述/非货物项说明”里生成或补充货物描述。";
            }

            if (isIrregularPack)
            {
                return "当前货项为非常规包装：PackType 只表示“非常规包装”代码，具体包装形式建议直接写在“包装单位/形式(英)”里；系统仍按包装数量、包装形式和英文品名生成货物描述。";
            }

            return "当前货项按普通货物项 / 常规包装处理；货物描述可先用系统生成句式，再按需要手工微调。";
        }

        private static string GetGoodsDescriptionCueText(string goodsItemFlag, string packType)
        {
            if (CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag))
            {
                return "可生成如 ELEVEN (11) PCS IN NUDE OF T SHIRT 的货物描述";
            }

            if (CustomsCooPackTypeCatalog.IsIrregular(packType))
            {
                return "可用 HANGING GARMENT / IN BULK / PCS IN NUDE 等包装形式生成";
            }

            return "可点“生成货物描述”，例如 EIGHTEEN (18) CARTONS OF MEN'S T-SHIRT";
        }

        private static string GetGoodsDescriptionToolTip(string goodsItemFlag, string packType)
        {
            if (CustomsCooGoodsItemFlagCatalog.IsNonGoods(goodsItemFlag))
            {
                return "当前行为非货物项时，系统会禁用无关的普通货物字段；“生成货物描述”会使用包装数量、包装单位/形式和英文性质/名称，不再生成 PACKING 总计。";
            }

            if (CustomsCooPackTypeCatalog.IsIrregular(packType))
            {
                return "当前为非常规包装但仍是正常货物项时，PackType 只写代码 2；实际包装形式写在包装单位/形式里，例如 HANGING GARMENT、IN BULK、PCS IN NUDE。";
            }

            return "货物描述建议使用英文标准句式。系统可按包装件数、包装单位和英文品名自动生成，你也可以再手工微调。";
        }
    }
}

