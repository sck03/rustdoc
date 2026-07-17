using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class SingleWindowDraftStateHelper
    {
        private static readonly HashSet<string> CustomsCooDocumentEditableExclusions = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooDocument.Id),
            nameof(CustomsCooDocument.SourceInvoiceId),
            nameof(CustomsCooDocument.InvoiceNo),
            nameof(CustomsCooDocument.ContractNo),
            nameof(CustomsCooDocument.Status),
            nameof(CustomsCooDocument.CertNo),
            nameof(CustomsCooDocument.WarningCount),
            nameof(CustomsCooDocument.WarningSummary),
            nameof(CustomsCooDocument.DraftRevision),
            nameof(CustomsCooDocument.ManualLockedFieldsJson),
            nameof(CustomsCooDocument.SourceBaselineJson),
            nameof(CustomsCooDocument.SourceBaselineHash),
            nameof(CustomsCooDocument.LastGeneratedAt),
            nameof(CustomsCooDocument.Items),
            nameof(CustomsCooDocument.NonpartyCorps),
            nameof(CustomsCooDocument.Attachments),
            nameof(CustomsCooDocument.SourceDiffCount),
            nameof(CustomsCooDocument.SourceDiffSummary),
            nameof(CustomsCooDocument.ManualLockedFieldCount)
        };

        private static readonly HashSet<string> CustomsCooItemEditableExclusions = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooItem.Id),
            nameof(CustomsCooItem.DocumentId),
            nameof(CustomsCooItem.SourceItemId),
            nameof(CustomsCooItem.SourceStyleNo),
            nameof(CustomsCooItem.GNo),
            nameof(CustomsCooItem.Document)
        };

        private static readonly HashSet<string> AgentConsignmentEditableExclusions = new(StringComparer.Ordinal)
        {
            nameof(AgentConsignmentDocument.Id),
            nameof(AgentConsignmentDocument.SourceInvoiceId),
            nameof(AgentConsignmentDocument.InvoiceNo),
            nameof(AgentConsignmentDocument.ContractNo),
            nameof(AgentConsignmentDocument.Status),
            nameof(AgentConsignmentDocument.CounterpartyStatus),
            nameof(AgentConsignmentDocument.ConsignNo),
            nameof(AgentConsignmentDocument.WarningCount),
            nameof(AgentConsignmentDocument.WarningSummary),
            nameof(AgentConsignmentDocument.DraftRevision),
            nameof(AgentConsignmentDocument.ManualLockedFieldsJson),
            nameof(AgentConsignmentDocument.SourceBaselineJson),
            nameof(AgentConsignmentDocument.SourceBaselineHash),
            nameof(AgentConsignmentDocument.LastGeneratedAt),
            nameof(AgentConsignmentDocument.SourceDiffCount),
            nameof(AgentConsignmentDocument.SourceDiffSummary),
            nameof(AgentConsignmentDocument.ManualLockedFieldCount)
        };

        private static readonly Dictionary<string, string> PropertyDisplayNames = new(StringComparer.Ordinal)
        {
            [nameof(CustomsCooDocument.ApplName)] = "申报员姓名",
            [nameof(CustomsCooDocument.Applicant)] = "申报员身份证号",
            [nameof(CustomsCooDocument.ApplTel)] = "申报员联系电话",
            [nameof(CustomsCooDocument.OrgCode)] = "签证机构代码",
            [nameof(CustomsCooDocument.FetchPlace)] = "领证机构代码",
            [nameof(CustomsCooDocument.AplAdd)] = "申请地址",
            [nameof(CustomsCooDocument.InvNo)] = "发票号",
            [nameof(CustomsCooDocument.InvDate)] = "发票日期",
            [nameof(CustomsCooDocument.DestCountry)] = "进口国/地区",
            [nameof(CustomsCooDocument.Exporter)] = "出口商",
            [nameof(CustomsCooDocument.Consignee)] = "收货人",
            [nameof(CustomsCooDocument.Mark)] = "唛头",
            [nameof(CustomsCooDocument.LoadPort)] = "装货港",
            [nameof(CustomsCooDocument.UnloadPort)] = "卸货港",
            [nameof(CustomsCooDocument.Curr)] = "币制",
            [nameof(CustomsCooDocument.PriceTerms)] = "价格条款",
            [nameof(CustomsCooDocument.OriCountryCode)] = "原产国代码",
            [nameof(CustomsCooDocument.OriCountry)] = "原产国英文",
            [nameof(AgentConsignmentDocument.GName)] = "主要货物名称",
            [nameof(AgentConsignmentDocument.CodeTS)] = "HS编码",
            [nameof(AgentConsignmentDocument.DeclTotal)] = "货物总价",
            [nameof(AgentConsignmentDocument.TradeMode)] = "贸易方式",
            [nameof(AgentConsignmentDocument.OriCountry)] = "原产地/货源地",
            [nameof(AgentConsignmentDocument.TradeCode)] = "委托方海关编码",
            [nameof(AgentConsignmentDocument.AgentCode)] = "申报单位海关编码",
            [nameof(AgentConsignmentDocument.Curr)] = "币制",
            [nameof(AgentConsignmentDocument.QtyOrWeight)] = "件数或重量",
            [nameof(AgentConsignmentDocument.PackingCondition)] = "包装情况",
            [nameof(AgentConsignmentDocument.EntryId)] = "报关单号",
            [nameof(AgentConsignmentDocument.ReceiveDate)] = "收到单证日期",
            [nameof(AgentConsignmentDocument.DeclarePrice)] = "报关收费",
            [nameof(CustomsCooItem.HSCode)] = "HS编码",
            [nameof(CustomsCooItem.GoodsName)] = "货物中文名",
            [nameof(CustomsCooItem.GoodsNameE)] = "货物英文名",
            [nameof(CustomsCooItem.PackQty)] = "包装件数",
            [nameof(CustomsCooItem.PackUnit)] = "包装单位",
            [nameof(CustomsCooItem.GoodsQty)] = "标准数量",
            [nameof(CustomsCooItem.GoodsQtyRef)] = "辅助数量",
            [nameof(CustomsCooItem.GoodsUnitE)] = "标准单位英文",
            [nameof(CustomsCooItem.GoodsUnit)] = "标准单位中文",
            [nameof(CustomsCooItem.SecdGoodsQtyRef)] = "第二辅助数量",
            [nameof(CustomsCooItem.SecdGoodsUnitRef)] = "第二辅助单位",
            [nameof(CustomsCooItem.GrossWt)] = "毛重",
            [nameof(CustomsCooItem.NetWt)] = "净重",
            [nameof(CustomsCooItem.InvPrice)] = "发票单价",
            [nameof(CustomsCooItem.InvValue)] = "发票金额",
            [nameof(CustomsCooItem.FobValue)] = "FOB值",
            [nameof(CustomsCooItem.GoodsDesc)] = "货物描述",
            [nameof(CustomsCooItem.OriCriteria)] = "原产标准",
            [nameof(CustomsCooItem.OriCriteriaRef)] = "原产标准辅助项",
            [nameof(CustomsCooItem.OriCriteriaSub)] = "原产标准子项",
            [nameof(CustomsCooItem.GoodsOriginCountry)] = "协定原产国代码",
            [nameof(CustomsCooItem.GoodsOriginCountryEn)] = "协定原产国英文",
            [nameof(CustomsCooItem.InvNo)] = "发票号",
            [nameof(CustomsCooItem.Producer)] = "生产商描述",
            [nameof(CustomsCooItem.ProducerTel)] = "生产商电话",
            [nameof(CustomsCooItem.PrdcEtpsName)] = "生产企业名称",
            [nameof(CustomsCooItem.PrdcEtpsConcEr)] = "生产企业联系人",
            [nameof(CustomsCooItem.PrdcEtpsTel)] = "生产企业联系电话",
            [nameof(CustomsCooItem.GoodsTaxRate)] = "最高税率标志"
        };

        private static readonly HashSet<string> SourceDiffPriorityFields = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooDocument.InvNo),
            nameof(CustomsCooDocument.InvDate),
            nameof(CustomsCooDocument.DestCountry),
            nameof(CustomsCooDocument.DestCountryCode),
            nameof(CustomsCooDocument.DestCountryName),
            nameof(CustomsCooDocument.Exporter),
            nameof(CustomsCooDocument.Consignee),
            nameof(CustomsCooDocument.Mark),
            nameof(CustomsCooDocument.LoadPort),
            nameof(CustomsCooDocument.UnloadPort),
            nameof(CustomsCooDocument.TransMeans),
            nameof(CustomsCooDocument.TransName),
            nameof(CustomsCooDocument.Curr),
            nameof(CustomsCooDocument.PriceTerms),
            nameof(CustomsCooDocument.TotalAmt),
            nameof(CustomsCooDocument.FobValue)
        };

        private static readonly HashSet<string> SourceDiffPriorityGoodsFields = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooItem.HSCode),
            nameof(CustomsCooItem.GoodsName),
            nameof(CustomsCooItem.GoodsNameE),
            nameof(CustomsCooItem.PackQty),
            nameof(CustomsCooItem.GoodsQty),
            nameof(CustomsCooItem.GoodsQtyRef),
            nameof(CustomsCooItem.GoodsUnitE),
            nameof(CustomsCooItem.GoodsUnit),
            nameof(CustomsCooItem.GrossWt),
            nameof(CustomsCooItem.NetWt)
        };

        private static readonly HashSet<string> CustomsCooSourceDiffFields = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooDocument.InvNo),
            nameof(CustomsCooDocument.InvDate),
            nameof(CustomsCooDocument.DestCountry),
            nameof(CustomsCooDocument.Exporter),
            nameof(CustomsCooDocument.Consignee),
            nameof(CustomsCooDocument.GoodsSpecClause),
            nameof(CustomsCooDocument.Mark),
            nameof(CustomsCooDocument.LoadPort),
            nameof(CustomsCooDocument.UnloadPort),
            nameof(CustomsCooDocument.TransMeans),
            nameof(CustomsCooDocument.TransName),
            nameof(CustomsCooDocument.Curr),
            nameof(CustomsCooDocument.PriceTerms),
            nameof(CustomsCooDocument.TotalAmt),
            nameof(CustomsCooDocument.FobValue)
        };

        private static readonly HashSet<string> CustomsCooSourceDiffGoodsFields = new(StringComparer.Ordinal)
        {
            nameof(CustomsCooItem.HSCode),
            nameof(CustomsCooItem.GoodsName),
            nameof(CustomsCooItem.GoodsNameE),
            nameof(CustomsCooItem.PackQty),
            nameof(CustomsCooItem.PackUnit),
            nameof(CustomsCooItem.GoodsQty),
            nameof(CustomsCooItem.GoodsQtyRef),
            nameof(CustomsCooItem.GoodsUnitE),
            nameof(CustomsCooItem.GoodsUnit),
            nameof(CustomsCooItem.GrossWt),
            nameof(CustomsCooItem.NetWt),
            nameof(CustomsCooItem.InvPrice),
            nameof(CustomsCooItem.InvValue),
            nameof(CustomsCooItem.FobValue),
            nameof(CustomsCooItem.GoodsDesc),
            nameof(CustomsCooItem.InvNo)
        };
    }
}
