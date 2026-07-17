using System.Text.RegularExpressions;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowExportReviewHelper
    {
        private static readonly Regex FieldCodeRegex = new(@"\(([A-Za-z][A-Za-z0-9]*)(?:[)=])", RegexOptions.Compiled);
        private static readonly Regex GoodsLineRegex = new(@"\(GNo=(\d+)\)", RegexOptions.Compiled);
        private static readonly (string Fragment, string PropertyKey)[] CustomsCooMessageFieldHints =
        [
            ("出口商代码", "CiqRegNo"),
            ("录入企业代码", "AplRegNo"),
            ("企业名称", "EtpsName"),
            ("出口商", "Exporter"),
            ("收货人", "Consignee"),
            ("唛头", "Mark"),
            ("进口国/地区编码", "DestCountryCode"),
            ("进口国/地区中文名", "DestCountryName"),
            ("进口国/地区", "DestCountry"),
            ("申报员姓名", "ApplName"),
            ("申报员身份证号", "Applicant"),
            ("申报员联系电话", "ApplTel"),
            ("签证机构代码", "OrgCode"),
            ("领证机构代码", "FetchPlace"),
            ("申请地址", "AplAdd"),
            ("申请企业承诺代码", "AplPromiseCode"),
            ("币制", "Curr"),
            ("发票日期", "InvDate"),
            ("申请日期", "AplDate"),
            ("出运日期", "IntendExpDate"),
            ("出口报关日期", "ExpDeclDate"),
            ("签发有效日期", "ChkValidDate"),
            ("原证申请日期", "OldDeclDate"),
            ("原证签发日期", "OldIssueDate"),
            ("装货港", "LoadPort"),
            ("卸货港", "UnloadPort"),
            ("运输方式", "TransMeans"),
            ("运输工具", "TransName"),
            ("价格条款", "PriceTerms"),
            ("发票金额", "TotalAmt"),
            ("FOB值", "FobValue"),
            ("原产国代码", "OriCountryCode"),
            ("原产国英文", "OriCountry")
        ];
        private static readonly (string Fragment, string PropertyKey)[] CustomsCooGoodsMessageFieldHints =
        [
            ("商品HS编码", "HSCode"),
            ("货物名称中文", "GoodsName"),
            ("货物名称英文", "GoodsNameE"),
            ("包装件数", "PackQty"),
            ("包装单位英文", "PackUnit"),
            ("包装类型", "PackType"),
            ("标准货物数量", "GoodsQty"),
            ("标准货物单位英文", "GoodsUnitE"),
            ("标准货物单位中文", "GoodsUnit"),
            ("辅助数量", "GoodsQtyRef"),
            ("辅助单位", "GoodsUnitRef"),
            ("第二辅助数量", "SecdGoodsQtyRef"),
            ("第二辅助单位", "SecdGoodsUnitRef"),
            ("毛重", "GrossWt"),
            ("净重", "NetWt"),
            ("发票单价", "InvPrice"),
            ("发票金额", "InvValue"),
            ("FOB值", "FobValue"),
            ("货物描述", "GoodsDesc"),
            ("RCEP 进口成份比例", "ICompPrpr"),
            ("进口成份比例", "ICompPrpr"),
            ("原产标准子项", "OriCriteriaSub"),
            ("FORM E 子标准", "OriCriteriaSub"),
            ("原产标准辅助项/进口成分比例", "OriCriteriaRef"),
            ("普惠制 W 对应 HS 前4位", "OriCriteriaRef"),
            ("普惠制 Y 对应进口成分比例", "OriCriteriaRef"),
            ("原产标准", "OriCriteria"),
            ("是否生产商保密", "ProducerSertFlag"),
            ("最高税率标志", "GoodsTaxRate"),
            ("生产企业组织机构代码", "CiqRegNo"),
            ("生产企业名称", "PrdcEtpsName"),
            ("生产企业联系人", "PrdcEtpsConcEr"),
            ("生产企业联系电话", "PrdcEtpsTel"),
            ("协定原产国代码", "GoodsOriginCountry"),
            ("协定原产国英文", "GoodsOriginCountryEn"),
            ("发票号", "InvNo")
        ];
        private static readonly (string Fragment, string PropertyKey)[] AgentConsignmentMessageFieldHints =
        [
            ("企业内部编号/经营单位海关10位编码", "CopCusCode"),
            ("主要货物名称", "GName"),
            ("HS编码", "CodeTS"),
            ("货物总价", "DeclTotal"),
            ("进出口日期", "IEDate"),
            ("表头编号", "ListNo"),
            ("贸易方式代码", "TradeMode"),
            ("原产地/货源地", "OriCountry"),
            ("委托方海关编码", "TradeCode"),
            ("申报单位(被委托方)海关10位编码", "AgentCode"),
            ("币制代码", "Curr"),
            ("件数或重量", "QtyOrWeight"),
            ("包装情况", "PackingCondition"),
            ("备注说明", "OtherNote"),
            ("委托方联系电话", "ConsignTele"),
            ("报关单号", "EntryId"),
            ("收到证件日期", "ReceiveDate"),
            ("报关收费", "DeclarePrice"),
            ("承诺说明", "PromiseNote"),
            ("报关员电话", "DeclTele")
        ];

        private static readonly Dictionary<string, string> CustomsCooPropertyGroups = new(StringComparer.Ordinal)
        {
            ["ApplyType"] = "证书基础",
            ["CertStatus"] = "证书基础",
            ["CertNo"] = "证书基础",
            ["CertType"] = "证书基础",
            ["EntMgrNo"] = "证书基础",
            ["CiqRegNo"] = "证书基础",
            ["AplRegNo"] = "证书基础",
            ["EtpsName"] = "证书基础",
            ["ApplName"] = "申报与对象",
            ["Applicant"] = "申报与对象",
            ["ApplTel"] = "申报与对象",
            ["OrgCode"] = "申报与对象",
            ["FetchPlace"] = "申报与对象",
            ["AplAdd"] = "申报与对象",
            ["InvDate"] = "申报与对象",
            ["InvNo"] = "申报与对象",
            ["AplDate"] = "申报与对象",
            ["DestCountry"] = "申报与对象",
            ["DestCountryCode"] = "申报与对象",
            ["DestCountryName"] = "申报与对象",
            ["Exporter"] = "申报与对象",
            ["Consignee"] = "申报与对象",
            ["ExporterTel"] = "申报与对象",
            ["ExporterFax"] = "申报与对象",
            ["ExporterEmail"] = "申报与对象",
            ["ConsigneeTel"] = "申报与对象",
            ["ConsigneeFax"] = "申报与对象",
            ["ConsigneeEmail"] = "申报与对象",
            ["EtpsConcEr"] = "申报与对象",
            ["EtpsTel"] = "申报与对象",
            ["GoodsSpecClause"] = "运输与贸易",
            ["Mark"] = "运输与贸易",
            ["LoadPort"] = "运输与贸易",
            ["UnloadPort"] = "运输与贸易",
            ["TransMeans"] = "运输与贸易",
            ["TransName"] = "运输与贸易",
            ["TransCountryCode"] = "运输与贸易",
            ["TransCountryName"] = "运输与贸易",
            ["TransPort"] = "运输与贸易",
            ["DestPort"] = "运输与贸易",
            ["TransDetails"] = "运输与贸易",
            ["IntendExpDate"] = "运输与贸易",
            ["TradeModeCode"] = "运输与贸易",
            ["FobValue"] = "运输与贸易",
            ["TotalAmt"] = "运输与贸易",
            ["Note"] = "运输与贸易",
            ["LcNo"] = "运输与贸易",
            ["SpecInvTerms"] = "运输与贸易",
            ["PriceTerms"] = "运输与贸易",
            ["Curr"] = "运输与贸易",
            ["Producer"] = "补充与特殊项",
            ["ProducerSertFlag"] = "补充与特殊项",
            ["ExhibitFlag"] = "补充与特殊项",
            ["ThirdPartyInvFlag"] = "补充与特殊项",
            ["PredictFlag"] = "补充与特殊项",
            ["ExpDeclDate"] = "补充与特殊项",
            ["OriCountryCode"] = "补充与特殊项",
            ["OriCountry"] = "补充与特殊项",
            ["ChkValidDate"] = "补充与特殊项",
            ["EntryId"] = "补充与特殊项",
            ["PrcsAssembly"] = "补充与特殊项",
            ["OldCertNo"] = "补充与特殊项",
            ["ModReason"] = "补充与特殊项",
            ["ModColm"] = "补充与特殊项",
            ["OldSituDesc"] = "补充与特殊项",
            ["ModSituDesc"] = "补充与特殊项",
            ["OldDeclDate"] = "补充与特殊项",
            ["OldIssueDate"] = "补充与特殊项",
            ["AplPromiseCode"] = "补充与特殊项"
        };

        private static readonly HashSet<string> CustomsCooAutoRepairGroups = new(StringComparer.Ordinal)
        {
            "证书基础",
            "申报与对象",
            "运输与贸易",
            "补充与特殊项",
            "明细项目",
            "附件"
        };

        private const string AgentConsignmentDefaultSectionKey = "基础标识";
        private const string AgentConsignmentDeclarationSectionKey = "申报要素";
        private const string AgentConsignmentDocumentSectionKey = "单证与费用";
        private const string AgentConsignmentReceiptSectionKey = "回执回写信息";

        private static readonly IReadOnlySet<string> AgentConsignmentAutoRepairGroups =
            new HashSet<string>(StringComparer.Ordinal)
            {
                AgentConsignmentDefaultSectionKey,
                AgentConsignmentDeclarationSectionKey,
                AgentConsignmentDocumentSectionKey
            };

        private static readonly IReadOnlyDictionary<string, string> AgentConsignmentPropertySectionKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CopCusCode"] = AgentConsignmentDefaultSectionKey,
                ["Sign"] = AgentConsignmentDefaultSectionKey,
                ["OperType"] = AgentConsignmentDefaultSectionKey,
                ["GName"] = AgentConsignmentDefaultSectionKey,
                ["CodeTS"] = AgentConsignmentDefaultSectionKey,
                ["DeclTotal"] = AgentConsignmentDeclarationSectionKey,
                ["IEDate"] = AgentConsignmentDeclarationSectionKey,
                ["ListNo"] = AgentConsignmentDeclarationSectionKey,
                ["TradeMode"] = AgentConsignmentDeclarationSectionKey,
                ["OriCountry"] = AgentConsignmentDeclarationSectionKey,
                ["TradeCode"] = AgentConsignmentDeclarationSectionKey,
                ["AgentCode"] = AgentConsignmentDeclarationSectionKey,
                ["Curr"] = AgentConsignmentDeclarationSectionKey,
                ["QtyOrWeight"] = AgentConsignmentDeclarationSectionKey,
                ["PackingCondition"] = AgentConsignmentDeclarationSectionKey,
                ["OtherNote"] = AgentConsignmentDeclarationSectionKey,
                ["ConsignTele"] = AgentConsignmentDocumentSectionKey,
                ["EntryId"] = AgentConsignmentDocumentSectionKey,
                ["ReceiveDate"] = AgentConsignmentDocumentSectionKey,
                ["PaperInfo"] = AgentConsignmentDocumentSectionKey,
                ["OtherRecInfo"] = AgentConsignmentDocumentSectionKey,
                ["DeclarePrice"] = AgentConsignmentDocumentSectionKey,
                ["PromiseNote"] = AgentConsignmentDocumentSectionKey,
                ["DeclTele"] = AgentConsignmentDocumentSectionKey,
                ["ConsignNo"] = AgentConsignmentReceiptSectionKey,
                ["CounterpartyStatus"] = AgentConsignmentReceiptSectionKey
            };

        public static IReadOnlyList<SingleWindowExportIssueGroup> BuildGroups(
            SingleWindowBusinessType businessType,
            IEnumerable<(SingleWindowExportIssueSeverity Severity, string Message)> issues)
        {
            var grouped = (issues ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Message))
                .Select(item =>
                {
                    var classification = ClassifyIssue(businessType, item.Message);
                    return new SingleWindowExportIssue
                    {
                        GroupKey = classification.GroupKey,
                        GroupDisplayName = classification.GroupDisplayName,
                        Message = item.Message.Trim(),
                        Severity = item.Severity,
                        CanAutoRepair = classification.CanAutoRepair,
                        NavigationTarget = new SingleWindowEditorNavigationTarget
                        {
                            GroupKey = classification.NavigationGroupKey,
                            PropertyKey = classification.PropertyKey,
                            GoodsLineNo = classification.GoodsLineNo
                        }
                    };
                })
                .GroupBy(item => item.GroupKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new SingleWindowExportIssueGroup
                    {
                        GroupKey = group.Key,
                        GroupDisplayName = first.GroupDisplayName,
                        CanAutoRepair = first.CanAutoRepair,
                        Issues = group
                            .OrderByDescending(item => item.Severity)
                            .ThenBy(item => item.Message, StringComparer.Ordinal)
                            .ToList()
                    };
                })
                .OrderByDescending(group => group.ErrorCount)
                .ThenByDescending(group => group.WarningCount)
                .ThenBy(group => group.GroupDisplayName, StringComparer.Ordinal)
                .ToList();

            return grouped;
        }

        private static (string GroupKey, string GroupDisplayName, bool CanAutoRepair, string NavigationGroupKey, string PropertyKey, int GoodsLineNo) ClassifyIssue(
            SingleWindowBusinessType businessType,
            string message)
        {
            if (businessType == SingleWindowBusinessType.CustomsCoo)
            {
                return ClassifyCustomsCooIssue(message);
            }

            return ClassifyAgentConsignmentIssue(message);
        }

        private static (string GroupKey, string GroupDisplayName, bool CanAutoRepair, string NavigationGroupKey, string PropertyKey, int GoodsLineNo) ClassifyCustomsCooIssue(string message)
        {
            string normalizedMessage = message?.Trim() ?? string.Empty;
            string fieldCode = TryExtractFieldCode(normalizedMessage);
            int goodsLineNo = TryExtractGoodsLineNo(normalizedMessage);
            string propertyKey = ResolveCustomsCooPropertyKey(normalizedMessage, fieldCode, goodsLineNo);

            if (normalizedMessage.Contains("非缔约方", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "NonpartyCorpList", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "EntName", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "EntAddr", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "EntCountryCode", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "EntCountryName", StringComparison.Ordinal) ||
                string.Equals(propertyKey, "SortNo", StringComparison.Ordinal))
            {
                return ("非缔约方公司", "非缔约方公司", false, "非缔约方公司", string.Empty, 0);
            }

            if (normalizedMessage.Contains("附件", StringComparison.Ordinal) ||
                normalizedMessage.Contains("文件", StringComparison.Ordinal))
            {
                return ("附件", "附件", true, "附件", string.Empty, 0);
            }

            if (normalizedMessage.Contains("(GNo=", StringComparison.Ordinal))
            {
                return ("明细项目", "明细项目", true, "明细项目", propertyKey, goodsLineNo);
            }

            if (!string.IsNullOrWhiteSpace(propertyKey) &&
                CustomsCooPropertyGroups.TryGetValue(propertyKey, out var group))
            {
                return (group, group, CustomsCooAutoRepairGroups.Contains(group), group, propertyKey, 0);
            }

            return GuessCustomsCooGroupByText(normalizedMessage);
        }

        private static (string GroupKey, string GroupDisplayName, bool CanAutoRepair, string NavigationGroupKey, string PropertyKey, int GoodsLineNo) GuessCustomsCooGroupByText(string message)
        {
            if (message.Contains("进口国", StringComparison.Ordinal) ||
                message.Contains("收货人", StringComparison.Ordinal) ||
                message.Contains("出口商", StringComparison.Ordinal) ||
                message.Contains("申报员", StringComparison.Ordinal) ||
                message.Contains("签证机构", StringComparison.Ordinal) ||
                message.Contains("领证机构", StringComparison.Ordinal) ||
                message.Contains("申请地址", StringComparison.Ordinal))
            {
                return ("申报与对象", "申报与对象", true, "申报与对象", string.Empty, 0);
            }

            if (message.Contains("装货港", StringComparison.Ordinal) ||
                message.Contains("卸货港", StringComparison.Ordinal) ||
                message.Contains("运输", StringComparison.Ordinal) ||
                message.Contains("币制", StringComparison.Ordinal) ||
                message.Contains("价格条款", StringComparison.Ordinal) ||
                message.Contains("发票金额", StringComparison.Ordinal))
            {
                return ("运输与贸易", "运输与贸易", true, "运输与贸易", string.Empty, 0);
            }

            if (message.Contains("原产", StringComparison.Ordinal) ||
                message.Contains("第三方发票", StringComparison.Ordinal) ||
                message.Contains("更改", StringComparison.Ordinal) ||
                message.Contains("重发", StringComparison.Ordinal) ||
                message.Contains("承诺", StringComparison.Ordinal) ||
                message.Contains("生产商", StringComparison.Ordinal))
            {
                return ("补充与特殊项", "补充与特殊项", true, "补充与特殊项", string.Empty, 0);
            }

            return ("证书基础", "证书基础", true, "证书基础", string.Empty, 0);
        }

        private static (string GroupKey, string GroupDisplayName, bool CanAutoRepair, string NavigationGroupKey, string PropertyKey, int GoodsLineNo) ClassifyAgentConsignmentIssue(string message)
        {
            string normalizedMessage = message?.Trim() ?? string.Empty;
            string fieldCode = TryExtractFieldCode(normalizedMessage);
            string propertyKey = ResolveAgentConsignmentPropertyKey(normalizedMessage, fieldCode);
            if (!string.IsNullOrWhiteSpace(propertyKey) &&
                TryGetAgentConsignmentPropertyGroup(propertyKey, out var group) &&
                !string.Equals(group, AgentConsignmentReceiptSectionKey, StringComparison.Ordinal))
            {
                return (group, group, AgentConsignmentAutoRepairGroups.Contains(group), group, propertyKey, 0);
            }

            if (normalizedMessage.Contains("电话", StringComparison.Ordinal) ||
                normalizedMessage.Contains("收费", StringComparison.Ordinal) ||
                normalizedMessage.Contains("承诺", StringComparison.Ordinal) ||
                normalizedMessage.Contains("收到证件", StringComparison.Ordinal))
            {
                return (AgentConsignmentDocumentSectionKey, AgentConsignmentDocumentSectionKey, true, AgentConsignmentDocumentSectionKey, propertyKey, 0);
            }

            if (normalizedMessage.Contains("贸易方式", StringComparison.Ordinal) ||
                normalizedMessage.Contains("原产地", StringComparison.Ordinal) ||
                normalizedMessage.Contains("币制", StringComparison.Ordinal) ||
                normalizedMessage.Contains("进出口日期", StringComparison.Ordinal) ||
                normalizedMessage.Contains("总价", StringComparison.Ordinal))
            {
                return (AgentConsignmentDeclarationSectionKey, AgentConsignmentDeclarationSectionKey, true, AgentConsignmentDeclarationSectionKey, propertyKey, 0);
            }

            return (AgentConsignmentDefaultSectionKey, AgentConsignmentDefaultSectionKey, true, AgentConsignmentDefaultSectionKey, propertyKey, 0);
        }

        private static bool TryGetAgentConsignmentPropertyGroup(string propertyName, out string groupKey)
        {
            return AgentConsignmentPropertySectionKeys.TryGetValue(propertyName ?? string.Empty, out groupKey);
        }

        private static string ResolveCustomsCooPropertyKey(string message, string fieldCode, int goodsLineNo)
        {
            if (goodsLineNo > 0)
            {
                string goodsPropertyKey = ResolvePropertyKeyFromHints(message, CustomsCooGoodsMessageFieldHints);
                if (!string.IsNullOrWhiteSpace(goodsPropertyKey))
                {
                    return goodsPropertyKey;
                }
            }

            if (!string.IsNullOrWhiteSpace(fieldCode))
            {
                return fieldCode;
            }

            return goodsLineNo > 0
                ? string.Empty
                : ResolvePropertyKeyFromHints(message, CustomsCooMessageFieldHints);
        }

        private static string ResolveAgentConsignmentPropertyKey(string message, string fieldCode)
        {
            if (!string.IsNullOrWhiteSpace(fieldCode))
            {
                return fieldCode;
            }

            return ResolvePropertyKeyFromHints(message, AgentConsignmentMessageFieldHints);
        }

        private static string ResolvePropertyKeyFromHints(string message, IEnumerable<(string Fragment, string PropertyKey)> hints)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            foreach (var (fragment, propertyKey) in hints ?? [])
            {
                if (!string.IsNullOrWhiteSpace(fragment) &&
                    message.Contains(fragment, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(propertyKey))
                {
                    return propertyKey;
                }
            }

            return string.Empty;
        }

        private static string TryExtractFieldCode(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            var match = FieldCodeRegex.Match(message);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static int TryExtractGoodsLineNo(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return 0;
            }

            var match = GoodsLineRegex.Match(message);
            return match.Success && int.TryParse(match.Groups[1].Value, out var lineNo)
                ? Math.Max(0, lineNo)
                : 0;
        }
    }
}
