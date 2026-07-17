using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public enum AgentConsignmentEditorFieldKind
    {
        TextBox,
        MultilineTextBox,
        ComboBox,
        EditableComboBox
    }

    public sealed record AgentConsignmentEditorFieldDefinition(
        string Label,
        string PropertyName,
        AgentConsignmentEditorFieldKind FieldKind = AgentConsignmentEditorFieldKind.TextBox,
        int Height = 32,
        IReadOnlyList<SelectionOption<string>> Options = null,
        bool IsReadOnly = false);

    public static class AgentConsignmentEditorCatalog
    {
        public const string DefaultSectionKey = "基础标识";
        public const string DeclarationSectionKey = "申报要素";
        public const string DocumentSectionKey = "单证与费用";
        public const string ReceiptSectionKey = "回执回写信息";

        public static IReadOnlyList<string> EditableFieldKeys { get; } =
        [
            "CopCusCode",
            "Sign",
            "OperType",
            "GName",
            "CodeTS",
            "DeclTotal",
            "IEDate",
            "ListNo",
            "TradeMode",
            "OriCountry",
            "TradeCode",
            "AgentCode",
            "Curr",
            "QtyOrWeight",
            "PackingCondition",
            "OtherNote",
            "ConsignTele",
            "EntryId",
            "ReceiveDate",
            "PaperInfo",
            "OtherRecInfo",
            "DeclarePrice",
            "PromiseNote",
            "DeclTele"
        ];

        public static IReadOnlyList<string> ManualOverrideFieldKeys { get; } =
        [
            "Sign",
            "PackingCondition",
            "OtherNote",
            "EntryId",
            "PaperInfo",
            "OtherRecInfo",
            "DeclarePrice",
            "PromiseNote",
            "DeclTele"
        ];

        public static IReadOnlyDictionary<string, GroupScopedClearOption[]> ScopedClearOptionsByGroup { get; } =
            new Dictionary<string, GroupScopedClearOption[]>(StringComparer.Ordinal)
            {
                [DefaultSectionKey] =
                [
                    new("identity", "企业与操作", "只恢复企业内部编号和操作类型。"),
                    new("goods", "签名与货物", "只恢复数字签名、主要货物名称和 HS 编码。")
                ],
                [DeclarationSectionKey] =
                [
                    new("schedule_doc", "日期与单号", "只恢复进出口日期和提单号。"),
                    new("trade_code", "贸易与编码", "只恢复贸易方式、原产地、经营/申报单位和币制。"),
                    new("quantity_note", "数量与补充", "只恢复总价、数量重量、包装情况和其他要求。")
                ],
                [DocumentSectionKey] =
                [
                    new("contact", "联系电话", "只恢复委托方电话和被委托方电话。"),
                    new("receipt", "收件信息", "只恢复收到证件日期、收到单证情况和其他收件信息。"),
                    new("document_fee", "单证与费用", "只恢复报关单编号、报关收费和承诺说明。")
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScopedFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["identity"] = ["CopCusCode", "OperType"],
                ["goods"] = ["Sign", "GName", "CodeTS"],
                ["schedule_doc"] = ["IEDate", "ListNo"],
                ["trade_code"] = ["TradeMode", "OriCountry", "TradeCode", "AgentCode", "Curr"],
                ["quantity_note"] = ["DeclTotal", "QtyOrWeight", "PackingCondition", "OtherNote"],
                ["contact"] = ["ConsignTele", "DeclTele"],
                ["receipt"] = ["ReceiveDate", "PaperInfo", "OtherRecInfo"],
                ["document_fee"] = ["EntryId", "DeclarePrice", "PromiseNote"]
            };

        public static IReadOnlySet<string> RequiredFieldProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "CopCusCode",
            "GName",
            "CodeTS",
            "DeclTotal",
            "IEDate",
            "TradeMode",
            "OriCountry",
            "TradeCode",
            "AgentCode"
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
            "DraftRevision",
            "ConsignNo",
            "CounterpartyStatus"
        };

        public static IReadOnlySet<string> AutoRepairGroups { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            DefaultSectionKey,
            DeclarationSectionKey,
            DocumentSectionKey
        };

        public static IReadOnlyList<string> SectionOrder { get; } =
        [
            DefaultSectionKey,
            DeclarationSectionKey,
            DocumentSectionKey,
            ReceiptSectionKey
        ];

        private static readonly IReadOnlyDictionary<string, string> SectionDescriptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DefaultSectionKey] = "先检查企业内部编号、操作类型和货物名称；黄色底色表示官方必填项。",
                [DeclarationSectionKey] = "这里一般决定交接包能否顺利导入。建议重点复核日期、贸易方式、原产地和海关编码。",
                [DocumentSectionKey] = "收到证件日期、收件情况和收费可以后补；第一次使用时可先把关键申报字段填对。",
                [ReceiptSectionKey] = "这两项通常由单一窗口回执自动带回，不需要第一次手工填写。"
            };

        private static readonly IReadOnlyDictionary<string, string> PropertySectionKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CopCusCode"] = DefaultSectionKey,
                ["Sign"] = DefaultSectionKey,
                ["OperType"] = DefaultSectionKey,
                ["GName"] = DefaultSectionKey,
                ["CodeTS"] = DefaultSectionKey,
                ["DeclTotal"] = DeclarationSectionKey,
                ["IEDate"] = DeclarationSectionKey,
                ["ListNo"] = DeclarationSectionKey,
                ["TradeMode"] = DeclarationSectionKey,
                ["OriCountry"] = DeclarationSectionKey,
                ["TradeCode"] = DeclarationSectionKey,
                ["AgentCode"] = DeclarationSectionKey,
                ["Curr"] = DeclarationSectionKey,
                ["QtyOrWeight"] = DeclarationSectionKey,
                ["PackingCondition"] = DeclarationSectionKey,
                ["OtherNote"] = DeclarationSectionKey,
                ["ConsignTele"] = DocumentSectionKey,
                ["EntryId"] = DocumentSectionKey,
                ["ReceiveDate"] = DocumentSectionKey,
                ["PaperInfo"] = DocumentSectionKey,
                ["OtherRecInfo"] = DocumentSectionKey,
                ["DeclarePrice"] = DocumentSectionKey,
                ["PromiseNote"] = DocumentSectionKey,
                ["DeclTele"] = DocumentSectionKey,
                ["ConsignNo"] = ReceiptSectionKey,
                ["CounterpartyStatus"] = ReceiptSectionKey
            };

        private static readonly IReadOnlyDictionary<string, string> CueTexts =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CopCusCode"] = "10 位企业海关编码；通常与经营单位(委托方)海关10位编码一致",
                ["GName"] = "默认回填首项商品名称，可按需要覆盖",
                ["CodeTS"] = "10 位以内 HS 编码",
                ["DeclTotal"] = "货物总价，最多 4 位小数",
                ["IEDate"] = "格式: yyyyMMdd，例如 20260417",
                ["ListNo"] = "请填写真实提单号；当前不会再自动带发票号",
                ["TradeMode"] = "可直接选官方监管方式；一般贸易为 0110，来料加工为 0214",
                ["OriCountry"] = "可直接选官方国别地区代码；中国为 142，印尼为 112",
                ["TradeCode"] = "10 位经营单位(委托方)海关编码",
                ["AgentCode"] = "10 位申报单位(被委托方)海关编码",
                ["Curr"] = "3 位数字币制代码，例如 502",
                ["QtyOrWeight"] = "可填写总毛重或总数量",
                ["ConsignTele"] = "委托方联系电话",
                ["EntryId"] = "报关单编号，可留空后补",
                ["ReceiveDate"] = "格式: yyyyMMdd，例如 20260417",
                ["PaperInfo"] = "例如: 已收齐 / 待补充",
                ["DeclarePrice"] = "人民币金额，最多 2 位小数",
                ["DeclTele"] = "被委托方联系电话",
                ["OtherNote"] = "补充说明或特殊要求",
                ["OtherRecInfo"] = "填写其他收件情况",
                ["PromiseNote"] = "填写承诺说明或内部备注"
            };

        private static readonly IReadOnlyList<SelectionOption<string>> OperTypeOptions =
        [
            new("1", "1"),
            new("2", "2"),
            new("3", "3")
        ];

        public static IReadOnlyList<AgentConsignmentEditorFieldDefinition> GetSectionFields(string sectionKey)
        {
            string normalizedSectionKey = string.IsNullOrWhiteSpace(sectionKey)
                ? DefaultSectionKey
                : sectionKey.Trim();

            return normalizedSectionKey switch
            {
                DefaultSectionKey =>
                [
                    new("企业内部编号", "CopCusCode"),
                    new("数字签名", "Sign"),
                    new("操作类型", "OperType", AgentConsignmentEditorFieldKind.ComboBox, Options: OperTypeOptions),
                    new("主要货物名称", "GName"),
                    new("HS编码", "CodeTS")
                ],
                DeclarationSectionKey =>
                [
                    new("货物总价", "DeclTotal"),
                    new("进出口日期", "IEDate"),
                    new("提单号", "ListNo"),
                    new("贸易方式", "TradeMode", AgentConsignmentEditorFieldKind.EditableComboBox, Options: SingleWindowReferenceCatalogs.GetAcdTradeModeOptions()),
                    new("原产地/货源地", "OriCountry", AgentConsignmentEditorFieldKind.EditableComboBox, Options: SingleWindowReferenceCatalogs.GetAcdCountryOptions()),
                    new("经营单位(委托方)海关10位编码", "TradeCode"),
                    new("申报单位(被委托方)海关10位编码", "AgentCode"),
                    new("币制代码", "Curr"),
                    new("数量/重量", "QtyOrWeight"),
                    new("包装情况", "PackingCondition"),
                    new("其他要求", "OtherNote", AgentConsignmentEditorFieldKind.MultilineTextBox, 72)
                ],
                DocumentSectionKey =>
                [
                    new("委托方电话", "ConsignTele"),
                    new("报关单编号", "EntryId"),
                    new("收到证件日期", "ReceiveDate"),
                    new("收到单证情况", "PaperInfo"),
                    new("其他收件信息", "OtherRecInfo", AgentConsignmentEditorFieldKind.MultilineTextBox, 72),
                    new("报关收费", "DeclarePrice"),
                    new("承诺说明", "PromiseNote", AgentConsignmentEditorFieldKind.MultilineTextBox, 72),
                    new("被委托方电话", "DeclTele")
                ],
                ReceiptSectionKey =>
                [
                    new("委托编号", "ConsignNo", IsReadOnly: true),
                    new("对方状态", "CounterpartyStatus", IsReadOnly: true)
                ],
                _ => Array.Empty<AgentConsignmentEditorFieldDefinition>()
            };
        }

        public static string GetSectionDescription(string sectionKey)
        {
            return SectionDescriptions.TryGetValue(
                string.IsNullOrWhiteSpace(sectionKey) ? DefaultSectionKey : sectionKey.Trim(),
                out var description)
                ? description
                : "按当前分组维护对应字段。";
        }

        public static string ResolveSectionKey(string propertyName)
        {
            return TryGetPropertyGroup(propertyName, out var sectionKey)
                ? sectionKey
                : ReceiptSectionKey;
        }

        public static bool TryGetPropertyGroup(string propertyName, out string groupKey)
        {
            return PropertySectionKeys.TryGetValue(propertyName ?? string.Empty, out groupKey);
        }

        public static string GetCueText(string propertyName)
        {
            return CueTexts.TryGetValue(propertyName ?? string.Empty, out var cueText)
                ? cueText
                : string.Empty;
        }
    }
}

