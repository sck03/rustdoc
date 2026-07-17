using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public enum CustomsCooEditorFieldKind
    {
        TextBox,
        MultilineTextBox,
        ComboBox,
        EditableComboBox
    }

    public sealed record CustomsCooEditorFieldDefinition(
        string Label,
        string PropertyName,
        CustomsCooEditorFieldKind FieldKind = CustomsCooEditorFieldKind.TextBox,
        int Height = 32,
        IReadOnlyList<SelectionOption<string>> Options = null);

    public static partial class CustomsCooEditorCatalog
    {
        public const string DefaultHeaderSectionKey = "证书基础";

        public static IReadOnlyDictionary<string, string> HeaderDefaultFallbacks { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ApplyType"] = "0",
                ["CertStatus"] = "0",
                ["CertType"] = "C",
                ["AplPromiseCode"] = "1"
            };

        public static IReadOnlyList<string> DefaultableHeaderFieldKeys { get; } =
        [
            "EtpsName",
            "ApplyType",
            "CertStatus",
            "CertNo",
            "CertType",
            "EntMgrNo",
            "CiqRegNo",
            "AplRegNo",
            "ApplName",
            "Applicant",
            "ApplTel",
            "OrgCode",
            "FetchPlace",
            "AplAdd",
            "InvDate",
            "InvNo",
            "AplDate",
            "DestCountry",
            "DestCountryCode",
            "DestCountryName",
            "Exporter",
            "Consignee",
            "GoodsSpecClause",
            "Mark",
            "LoadPort",
            "UnloadPort",
            "TransMeans",
            "TransName",
            "TransCountryCode",
            "TransCountryName",
            "TransPort",
            "DestPort",
            "TransDetails",
            "IntendExpDate",
            "TradeModeCode",
            "FobValue",
            "TotalAmt",
            "Note",
            "LcNo",
            "SpecInvTerms",
            "PriceTerms",
            "Curr",
            "Remark",
            "Producer",
            "ProducerSertFlag",
            "ExhibitFlag",
            "ThirdPartyInvFlag",
            "ExporterTel",
            "ExporterFax",
            "ExporterEmail",
            "ConsigneeTel",
            "ConsigneeFax",
            "ConsigneeEmail",
            "PredictFlag",
            "ExpDeclDate",
            "OriCountryCode",
            "OriCountry",
            "ChkValidDate",
            "EtpsConcEr",
            "EtpsTel",
            "EntryId",
            "PrcsAssembly",
            "OldCertNo",
            "ModReason",
            "ModColm",
            "OldSituDesc",
            "ModSituDesc",
            "OldDeclDate",
            "OldIssueDate",
            "AplPromiseCode"
        ];

        public static IReadOnlyList<string> ManualOverrideHeaderFieldKeys { get; } =
        [
            "EntMgrNo",
            "AplRegNo",
            "ApplName",
            "Applicant",
            "ApplTel",
            "OrgCode",
            "FetchPlace",
            "AplAdd",
            "TransName",
            "TransCountryCode",
            "TransCountryName",
            "TransPort",
            "TransDetails",
            "Note",
            "SpecInvTerms",
            "Remark",
            "Producer",
            "ProducerSertFlag",
            "ExhibitFlag",
            "ThirdPartyInvFlag",
            "ExporterTel",
            "ExporterFax",
            "ExporterEmail",
            "ConsigneeTel",
            "ConsigneeFax",
            "ConsigneeEmail",
            "PredictFlag",
            "ExpDeclDate",
            "ChkValidDate",
            "EtpsConcEr",
            "EtpsTel",
            "EntryId",
            "PrcsAssembly",
            "OldCertNo",
            "ModReason",
            "ModColm",
            "OldSituDesc",
            "ModSituDesc",
            "OldDeclDate",
            "OldIssueDate"
        ];

        public static IReadOnlyList<string> ManualOverrideGoodsFieldKeys { get; } =
        [
            "OriCriteria",
            "OriCriteriaRef",
            "ICompPrpr",
            "Producer",
            "ProducerTel",
            "ProducerFax",
            "ProducerEmail",
            "CiqRegNo",
            "PrdcEtpsName",
            "PrdcEtpsConcEr",
            "PrdcEtpsTel",
            "ProducerSertFlag",
            "OriCriteriaSub",
            "GoodsTaxRate"
        ];

        public static IReadOnlyDictionary<string, GroupScopedClearOption[]> ScopedClearOptionsByGroup { get; } =
            new Dictionary<string, GroupScopedClearOption[]>(StringComparer.Ordinal)
            {
                ["证书基础"] =
                [
                    new("certificate_type", "证书类型与状态", "只恢复申请类型、证书类别和证书类型。"),
                    new("enterprise_code", "企业主体信息", "只恢复企业名称、企业编号、出口商代码和录入企业代码。")
                ],
                ["申报与对象"] =
                [
                    new("declarant", "申报联系人", "只恢复申报员姓名、证件号、电话和申请日期。"),
                    new("organization_address", "机构与地址", "只恢复签证机构、领证机构、申请地址和企业联系人。"),
                    new("destination_parties", "发票对象与目的国", "只恢复发票日期、发票号、进口国、出口商、收货人及联系方式。")
                ],
                ["运输与贸易"] =
                [
                    new("goods_notes", "品名与备注", "只恢复商品/特别条款、唛头和申请书备注。"),
                    new("transport_route", "运输港口", "只恢复运输方式、船名航次、中转国和港口信息。"),
                    new("trade_terms", "贸易条款", "只恢复贸易方式、金额、信用证号、发票特殊条款、价格条款和币制。")
                ],
                ["补充与特殊项"] =
                [
                    new("certificate_extra", "证书补充", "只恢复证书备注、日期扩展、报关单号、更改证信息和企业承诺代码。"),
                    new("producer_third_party", "生产商与第三方发票", "只恢复生产商信息及第三方发票标志。"),
                    new("origin_country", "原产国信息", "只恢复原产国代码和名称。")
                ],
                ["明细项目"] =
                [
                    new("goods_name_pack", "品名与包装", "只恢复明细里的品名、包装和描述。"),
                    new("goods_quantity_weight", "数量重量", "只恢复数量、单位、价格和毛净重。"),
                    new("goods_origin_standard", "编码与原产标准", "只恢复 HS 编码、原产标准和原产国。"),
                    new("goods_producer", "生产商信息", "只恢复生产商名称和联系方式。")
                ],
                ["附件"] =
                [
                    new("attachment_identity", "资料标识", "只清附件的证书类型、企业代码和文档类型说明。"),
                    new("attachment_note_delay", "说明与提交", "只清附件说明和延迟提交标志。")
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScopedHeaderFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["certificate_type"] =
                [
                    "ApplyType",
                    "CertStatus",
                    "CertNo",
                    "CertType"
                ],
                ["enterprise_code"] =
                [
                    "EtpsName",
                    "EntMgrNo",
                    "CiqRegNo",
                    "AplRegNo"
                ],
                ["declarant"] =
                [
                    "ApplName",
                    "Applicant",
                    "ApplTel",
                    "AplDate"
                ],
                ["organization_address"] =
                [
                    "OrgCode",
                    "FetchPlace",
                    "AplAdd",
                    "EtpsConcEr",
                    "EtpsTel"
                ],
                ["destination_parties"] =
                [
                    "InvDate",
                    "InvNo",
                    "DestCountry",
                    "DestCountryCode",
                    "DestCountryName",
                    "Exporter",
                    "Consignee",
                    "ExporterTel",
                    "ExporterFax",
                    "ExporterEmail",
                    "ConsigneeTel",
                    "ConsigneeFax",
                    "ConsigneeEmail"
                ],
                ["goods_notes"] =
                [
                    "GoodsSpecClause",
                    "Mark",
                    "Note"
                ],
                ["transport_route"] =
                [
                    "LoadPort",
                    "UnloadPort",
                    "TransMeans",
                    "TransName",
                    "TransCountryCode",
                    "TransCountryName",
                    "TransPort",
                    "DestPort",
                    "TransDetails",
                    "IntendExpDate",
                    "PredictFlag",
                    "ExpDeclDate"
                ],
                ["trade_terms"] =
                [
                    "TradeModeCode",
                    "FobValue",
                    "TotalAmt",
                    "LcNo",
                    "SpecInvTerms",
                    "PriceTerms",
                    "Curr"
                ],
                ["certificate_extra"] =
                [
                    "Remark",
                    "ChkValidDate",
                    "EntryId",
                    "PrcsAssembly",
                    "OldCertNo",
                    "ModReason",
                    "ModColm",
                    "OldSituDesc",
                    "ModSituDesc",
                    "OldDeclDate",
                    "OldIssueDate",
                    "AplPromiseCode"
                ],
                ["producer_third_party"] =
                [
                    "Producer",
                    "ProducerSertFlag",
                    "ExhibitFlag",
                    "ThirdPartyInvFlag"
                ],
                ["origin_country"] =
                [
                    "OriCountryCode",
                    "OriCountry"
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScopedGoodsFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["goods_name_pack"] =
                [
                    "GoodsItemFlag",
                    "GoodsName",
                    "GoodsNameE",
                    "PackQty",
                    "PackUnit",
                    "PackType",
                    "GoodsDesc"
                ],
                ["goods_quantity_weight"] =
                [
                    "GoodsQty",
                    "GoodsQtyRef",
                    "GoodsUnitE",
                    "GoodsUnit",
                    "GoodsUnitRef",
                    "SecdGoodsQtyRef",
                    "SecdGoodsUnitRef",
                    "GrossWt",
                    "NetWt",
                    "WtUnit",
                    "InvPrice",
                    "InvValue",
                    "FobValue"
                ],
                ["goods_origin_standard"] =
                [
                    "HSCode",
                    "OriCriteria",
                    "OriCriteriaRef",
                    "OriCriteriaSub",
                    "GoodsOriginCountry",
                    "GoodsOriginCountryEn",
                    "InvNo"
                ],
                ["goods_producer"] =
                [
                    "ICompPrpr",
                    "Producer",
                    "ProducerTel",
                    "ProducerFax",
                    "ProducerEmail",
                    "CiqRegNo",
                    "PrdcEtpsName",
                    "PrdcEtpsConcEr",
                    "PrdcEtpsTel",
                    "ProducerSertFlag",
                    "GoodsTaxRate"
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScopedAttachmentStringFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["attachment_identity"] =
                [
                    "CertType",
                    "AplRegNo",
                    "CiqRegNo",
                    "FileType",
                    "DocType"
                ],
                ["attachment_note_delay"] = ["Description"]
            };

        public static IReadOnlySet<string> RequiredHeaderProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "ApplyType",
            "CertStatus",
            "CertNo",
            "CertType",
            "CiqRegNo",
            "AplRegNo",
            "EtpsName",
            "ApplName",
            "Applicant",
            "ApplTel",
            "OrgCode",
            "FetchPlace",
            "AplAdd",
            "AplDate",
            "DestCountry",
            "DestCountryCode",
            "DestCountryName",
            "Exporter",
            "Consignee",
            "Mark",
            "AplPromiseCode"
        };

        public static IReadOnlySet<string> HeaderRefreshProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "InvoiceNo",
            "ContractNo",
            "DocumentStatus",
            "WarningSummary",
            "LastGeneratedAtText",
            "SourceDiffSummary",
            "SourceDiffCount",
            "ManualLockedFieldCount",
            "DraftRevision"
        };

        public static IReadOnlySet<string> RequiredGoodsFieldProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "GoodsItemFlag",
            "HSCode",
            "GoodsName",
            "GoodsNameE",
            "PackQty",
            "PackUnit",
            "PackType",
            "GoodsQty",
            "GoodsUnitE",
            "GoodsUnit",
            "InvValue",
            "FobValue",
            "CiqRegNo",
            "PrdcEtpsName",
            "PrdcEtpsConcEr",
            "PrdcEtpsTel",
            "GoodsDesc"
        };

    }
}
