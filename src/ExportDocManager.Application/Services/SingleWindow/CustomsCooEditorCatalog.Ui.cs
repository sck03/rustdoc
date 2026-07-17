using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class CustomsCooEditorCatalog
    {
        public static IReadOnlyList<string> HeaderSectionOrder { get; } =
        [
            "证书基础",
            "申报与对象",
            "运输与贸易",
            "补充与特殊项",
            "更改与重发"
        ];

        public static IReadOnlyList<CustomsCooEditorFieldDefinition> RequiredGoodsDetailFields =>
        [
            new("货项标志", "GoodsItemFlag", CustomsCooEditorFieldKind.ComboBox, Options: GoodsItemFlagOptions),
            new("HS编码", "HSCode"),
            new("中文名", "GoodsName"),
            new("英文名", "GoodsNameE"),
            new("包装件数", "PackQty"),
            new("包装单位(英)", "PackUnit", CustomsCooEditorFieldKind.EditableComboBox, Options: CustomsCooPackUnitCatalog.CommonOptions),
            new("包装类型", "PackType", CustomsCooEditorFieldKind.ComboBox, Options: PackTypeOptions),
            new("标准数量", "GoodsQty"),
            new("单位(英)", "GoodsUnitE"),
            new("单位(中)", "GoodsUnit"),
            new("发票金额", "InvValue"),
            new("FOB值", "FobValue")
        ];

        public static IReadOnlyList<CustomsCooEditorFieldDefinition> SupplementalGoodsDetailFields =>
        [
            new("辅助数量", "GoodsQtyRef"),
            new("辅助单位", "GoodsUnitRef"),
            new("第二辅助数", "SecdGoodsQtyRef"),
            new("第二辅助单位", "SecdGoodsUnitRef"),
            new("毛重", "GrossWt"),
            new("净重", "NetWt"),
            new("重量单位", "WtUnit"),
            new("发票单价", "InvPrice"),
            new("进口成份比例", "ICompPrpr")
        ];

        public static IReadOnlyList<CustomsCooEditorFieldDefinition> OriginAndEnterpriseGoodsDetailFields =>
        [
            new("原产标准", "OriCriteria", CustomsCooEditorFieldKind.EditableComboBox, Options: EmptySelectionOptions),
            new("生产企业代码", "CiqRegNo"),
            new("生产企业名称", "PrdcEtpsName"),
            new("生产企业联系人", "PrdcEtpsConcEr"),
            new("生产企业联系电话", "PrdcEtpsTel"),
            new("货物描述", "GoodsDesc", CustomsCooEditorFieldKind.MultilineTextBox, 64)
        ];

        public static IReadOnlyList<CustomsCooEditorFieldDefinition> AgreementAndProducerGoodsDetailFields =>
        [
            new("子标准", "OriCriteriaSub", CustomsCooEditorFieldKind.EditableComboBox, Options: EmptySelectionOptions),
            new("原产标准辅助项", "OriCriteriaRef"),
            new("协定原产国代码", "GoodsOriginCountry"),
            new("协定原产国英文", "GoodsOriginCountryEn"),
            new("明细发票号", "InvNo"),
            new("最高税率标志", "GoodsTaxRate", CustomsCooEditorFieldKind.ComboBox, Options: GoodsTaxRateOptions),
            new("生产商描述", "Producer", CustomsCooEditorFieldKind.MultilineTextBox, 64),
            new("生产商电话", "ProducerTel"),
            new("生产商传真", "ProducerFax"),
            new("生产商邮箱", "ProducerEmail"),
            new("生产商保密", "ProducerSertFlag", CustomsCooEditorFieldKind.ComboBox, Options: ProducerSecretOptions)
        ];

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

        public static IReadOnlyList<SelectionOption<string>> EmptySelectionOptions { get; } =
        [
            new(string.Empty, string.Empty)
        ];

        private static readonly IReadOnlyDictionary<string, string> HeaderSectionDescriptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["证书基础"] = "先确认申请类型、证书类别、证书类型和企业编号；黄色底色表示当前单据中的必填项。",
                ["申报与对象"] = "这里通常维护申报员、签证/领证机构、机构所在地、出口商、收货人与目的国信息。签证机构选定后，领证机构和申请地址会按常用口径联动带出。",
                ["运输与贸易"] = "启运港、目的港、运输方式、金额、价格条款和币制是高频检查项，导出前建议重点复核。",
                ["补充与特殊项"] = "生产商、第三方发票、原产国和承诺代码等字段，建议按证书类型按需填写。",
                ["更改与重发"] = "当证书类别为更改证、重发证或更改重发证时，这组字段要按原证信息填写。"
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<CustomsCooEditorFieldDefinition>> HeaderSectionFields =
            new Dictionary<string, IReadOnlyList<CustomsCooEditorFieldDefinition>>(StringComparer.Ordinal)
            {
                ["证书基础"] =
                [
                    new("申请类型", "ApplyType", CustomsCooEditorFieldKind.ComboBox, Options: ApplyTypeOptions),
                    new("证书类别", "CertStatus", CustomsCooEditorFieldKind.ComboBox, Options: CertStatusOptions),
                    new("原产地证编号", "CertNo"),
                    new("证书类型", "CertType", CustomsCooEditorFieldKind.ComboBox, Options: CertTypeOptions),
                    new("企业名称(中文)", "EtpsName"),
                    new("企业编号", "EntMgrNo"),
                    new("出口商代码", "CiqRegNo"),
                    new("录入企业代码", "AplRegNo")
                ],
                ["申报与对象"] =
                [
                    new("申报员姓名", "ApplName"),
                    new("申报员身份证号", "Applicant"),
                    new("申报员电话", "ApplTel"),
                    new("签证机构代码(4位)", "OrgCode", CustomsCooEditorFieldKind.EditableComboBox, Options: CustomsCooIssuingAuthorityCatalog.GetOptions()),
                    new("领证机构代码(4位)", "FetchPlace", CustomsCooEditorFieldKind.EditableComboBox, Options: CustomsCooIssuingAuthorityCatalog.GetOptions()),
                    new("申请地址(机构所在地)", "AplAdd"),
                    new("发票日期", "InvDate"),
                    new("发票号", "InvNo"),
                    new("申请日期", "AplDate"),
                    new("进口国/地区英文", "DestCountry"),
                    new("进口国代码", "DestCountryCode"),
                    new("进口国中文名", "DestCountryName"),
                    new("出口商", "Exporter", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("收货人", "Consignee", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("出口商电话", "ExporterTel"),
                    new("出口商传真", "ExporterFax"),
                    new("出口商邮箱", "ExporterEmail"),
                    new("进口商电话", "ConsigneeTel"),
                    new("进口商传真", "ConsigneeFax"),
                    new("进口商邮箱", "ConsigneeEmail"),
                    new("企业联系人", "EtpsConcEr"),
                    new("企业联系电话", "EtpsTel")
                ],
                ["运输与贸易"] =
                [
                    new("特殊条款（商品描述）", "GoodsSpecClause", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("唛头", "Mark", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("启运港", "LoadPort"),
                    new("卸货港", "UnloadPort"),
                    new("运输方式", "TransMeans"),
                    new("船名/航次", "TransName"),
                    new("中转国代码", "TransCountryCode"),
                    new("中转国名称", "TransCountryName"),
                    new("转运港", "TransPort"),
                    new("目的港", "DestPort"),
                    new("运输细节", "TransDetails", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("出运日期", "IntendExpDate"),
                    new("预计离港标志", "PredictFlag", CustomsCooEditorFieldKind.ComboBox, Options: PredictFlagOptions),
                    new("出口报关日期", "ExpDeclDate"),
                    new("贸易方式代码", "TradeModeCode", CustomsCooEditorFieldKind.ComboBox, Options: CooTradeModeOptions),
                    new("FOB值", "FobValue"),
                    new("总金额", "TotalAmt"),
                    new("申请书备注", "Note", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("合同号", "ContractNo"),
                    new("信用证号", "LcNo"),
                    new("发票特殊条款", "SpecInvTerms", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("价格条款", "PriceTerms"),
                    new("币制", "Curr", CustomsCooEditorFieldKind.ComboBox, Options: CurrencyOptions)
                ],
                ["补充与特殊项"] =
                [
                    new("证书备注", "Remark", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("证书货物生产商描述", "Producer", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("生产商保密", "ProducerSertFlag", CustomsCooEditorFieldKind.ComboBox, Options: ProducerSecretOptions),
                    new("是否展览证书", "ExhibitFlag", CustomsCooEditorFieldKind.ComboBox, Options: ExhibitFlagOptions),
                    new("第三方发票标志", "ThirdPartyInvFlag", CustomsCooEditorFieldKind.ComboBox, Options: ThirdPartyInvoiceOptions),
                    new("原产国代码", "OriCountryCode"),
                    new("原产国名称", "OriCountry"),
                    new("签发有效日期", "ChkValidDate"),
                    new("加工装配工序", "PrcsAssembly", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("报关单号", "EntryId"),
                    new("企业承诺代码", "AplPromiseCode", CustomsCooEditorFieldKind.ComboBox, Options: PromiseOptions)
                ],
                ["更改与重发"] =
                [
                    new("原证书号", "OldCertNo"),
                    new("更改/重发原因", "ModReason", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("更改栏目", "ModColm"),
                    new("原有情况描述", "OldSituDesc", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("更改情况描述", "ModSituDesc", CustomsCooEditorFieldKind.MultilineTextBox, 72),
                    new("原证申请日期", "OldDeclDate"),
                    new("原证签发日期", "OldIssueDate")
                ]
            };

        private static readonly Lazy<IReadOnlyDictionary<string, string>> HeaderFieldSectionLookup =
            new(() =>
            {
                var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var section in HeaderSectionFields)
                {
                    foreach (var field in section.Value)
                    {
                        lookup[field.PropertyName] = section.Key;
                    }
                }

                return lookup;
            });

        public static IReadOnlyList<CustomsCooEditorFieldDefinition> GetHeaderSectionFields(string sectionKey)
        {
            return HeaderSectionFields.TryGetValue(
                string.IsNullOrWhiteSpace(sectionKey) ? DefaultHeaderSectionKey : sectionKey.Trim(),
                out var fields)
                ? fields
                : Array.Empty<CustomsCooEditorFieldDefinition>();
        }

        public static string GetHeaderSectionDescription(string sectionKey)
        {
            return HeaderSectionDescriptions.TryGetValue(
                string.IsNullOrWhiteSpace(sectionKey) ? DefaultHeaderSectionKey : sectionKey.Trim(),
                out var description)
                ? description
                : "按当前分组维护对应字段。";
        }

        public static string ResolveHeaderSectionKey(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return DefaultHeaderSectionKey;
            }

            return HeaderFieldSectionLookup.Value.TryGetValue(propertyName, out var sectionKey)
                ? sectionKey
                : DefaultHeaderSectionKey;
        }
    }
}
