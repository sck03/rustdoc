using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowExportReviewRepairCatalog
    {
        public const string CustomsCooGoodsGroupKey = "明细项目";
        public const string CustomsCooAttachmentGroupKey = "附件";

        public const string AgentConsignmentDefaultSectionKey = "基础标识";
        public const string AgentConsignmentDeclarationSectionKey = "申报要素";
        public const string AgentConsignmentDocumentSectionKey = "单证与费用";
        public const string AgentConsignmentReceiptSectionKey = "回执回写信息";

        public static IReadOnlyDictionary<string, string> CustomsCooHeaderDefaultFallbacks { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [nameof(CustomsCooDocument.ApplyType)] = "0",
                [nameof(CustomsCooDocument.CertStatus)] = "0",
                [nameof(CustomsCooDocument.CertType)] = "C",
                [nameof(CustomsCooDocument.AplPromiseCode)] = "1"
            };

        public static IReadOnlyDictionary<string, GroupScopedClearOption[]> CustomsCooScopedClearOptionsByGroup { get; } =
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
                [CustomsCooGoodsGroupKey] =
                [
                    new("goods_name_pack", "品名与包装", "只恢复明细里的品名、包装和描述。"),
                    new("goods_quantity_weight", "数量重量", "只恢复数量、单位、价格和毛净重。"),
                    new("goods_origin_standard", "编码与原产标准", "只恢复 HS 编码、原产标准和原产国。"),
                    new("goods_producer", "生产商信息", "只恢复生产商名称和联系方式。")
                ],
                [CustomsCooAttachmentGroupKey] =
                [
                    new("attachment_identity", "资料标识", "只清附件的证书类型、企业代码和文档类型说明。"),
                    new("attachment_note_delay", "说明与提交", "只清附件说明和延迟提交标志。")
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> CustomsCooScopedHeaderFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["certificate_type"] =
                [
                    nameof(CustomsCooDocument.ApplyType),
                    nameof(CustomsCooDocument.CertStatus),
                    nameof(CustomsCooDocument.CertNo),
                    nameof(CustomsCooDocument.CertType)
                ],
                ["enterprise_code"] =
                [
                    nameof(CustomsCooDocument.EtpsName),
                    nameof(CustomsCooDocument.EntMgrNo),
                    nameof(CustomsCooDocument.CiqRegNo),
                    nameof(CustomsCooDocument.AplRegNo)
                ],
                ["declarant"] =
                [
                    nameof(CustomsCooDocument.ApplName),
                    nameof(CustomsCooDocument.Applicant),
                    nameof(CustomsCooDocument.ApplTel),
                    nameof(CustomsCooDocument.AplDate)
                ],
                ["organization_address"] =
                [
                    nameof(CustomsCooDocument.OrgCode),
                    nameof(CustomsCooDocument.FetchPlace),
                    nameof(CustomsCooDocument.AplAdd),
                    nameof(CustomsCooDocument.EtpsConcEr),
                    nameof(CustomsCooDocument.EtpsTel)
                ],
                ["destination_parties"] =
                [
                    nameof(CustomsCooDocument.InvDate),
                    nameof(CustomsCooDocument.InvNo),
                    nameof(CustomsCooDocument.DestCountry),
                    nameof(CustomsCooDocument.DestCountryCode),
                    nameof(CustomsCooDocument.DestCountryName),
                    nameof(CustomsCooDocument.Exporter),
                    nameof(CustomsCooDocument.Consignee),
                    nameof(CustomsCooDocument.ExporterTel),
                    nameof(CustomsCooDocument.ExporterFax),
                    nameof(CustomsCooDocument.ExporterEmail),
                    nameof(CustomsCooDocument.ConsigneeTel),
                    nameof(CustomsCooDocument.ConsigneeFax),
                    nameof(CustomsCooDocument.ConsigneeEmail)
                ],
                ["goods_notes"] =
                [
                    nameof(CustomsCooDocument.GoodsSpecClause),
                    nameof(CustomsCooDocument.Mark),
                    nameof(CustomsCooDocument.Note)
                ],
                ["transport_route"] =
                [
                    nameof(CustomsCooDocument.LoadPort),
                    nameof(CustomsCooDocument.UnloadPort),
                    nameof(CustomsCooDocument.TransMeans),
                    nameof(CustomsCooDocument.TransName),
                    nameof(CustomsCooDocument.TransCountryCode),
                    nameof(CustomsCooDocument.TransCountryName),
                    nameof(CustomsCooDocument.TransPort),
                    nameof(CustomsCooDocument.DestPort),
                    nameof(CustomsCooDocument.TransDetails),
                    nameof(CustomsCooDocument.IntendExpDate),
                    nameof(CustomsCooDocument.PredictFlag),
                    nameof(CustomsCooDocument.ExpDeclDate)
                ],
                ["trade_terms"] =
                [
                    nameof(CustomsCooDocument.TradeModeCode),
                    nameof(CustomsCooDocument.FobValue),
                    nameof(CustomsCooDocument.TotalAmt),
                    nameof(CustomsCooDocument.LcNo),
                    nameof(CustomsCooDocument.SpecInvTerms),
                    nameof(CustomsCooDocument.PriceTerms),
                    nameof(CustomsCooDocument.Curr)
                ],
                ["certificate_extra"] =
                [
                    nameof(CustomsCooDocument.Remark),
                    nameof(CustomsCooDocument.ChkValidDate),
                    nameof(CustomsCooDocument.EntryId),
                    nameof(CustomsCooDocument.PrcsAssembly),
                    nameof(CustomsCooDocument.OldCertNo),
                    nameof(CustomsCooDocument.ModReason),
                    nameof(CustomsCooDocument.ModColm),
                    nameof(CustomsCooDocument.OldSituDesc),
                    nameof(CustomsCooDocument.ModSituDesc),
                    nameof(CustomsCooDocument.OldDeclDate),
                    nameof(CustomsCooDocument.OldIssueDate),
                    nameof(CustomsCooDocument.AplPromiseCode)
                ],
                ["producer_third_party"] =
                [
                    nameof(CustomsCooDocument.Producer),
                    nameof(CustomsCooDocument.ProducerSertFlag),
                    nameof(CustomsCooDocument.ExhibitFlag),
                    nameof(CustomsCooDocument.ThirdPartyInvFlag)
                ],
                ["origin_country"] =
                [
                    nameof(CustomsCooDocument.OriCountryCode),
                    nameof(CustomsCooDocument.OriCountry)
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> CustomsCooScopedGoodsFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["goods_name_pack"] =
                [
                    nameof(CustomsCooItem.GoodsItemFlag),
                    nameof(CustomsCooItem.GoodsName),
                    nameof(CustomsCooItem.GoodsNameE),
                    nameof(CustomsCooItem.PackQty),
                    nameof(CustomsCooItem.PackUnit),
                    nameof(CustomsCooItem.PackType),
                    nameof(CustomsCooItem.GoodsDesc)
                ],
                ["goods_quantity_weight"] =
                [
                    nameof(CustomsCooItem.GoodsQty),
                    nameof(CustomsCooItem.GoodsQtyRef),
                    nameof(CustomsCooItem.GoodsUnitE),
                    nameof(CustomsCooItem.GoodsUnit),
                    nameof(CustomsCooItem.GoodsUnitRef),
                    nameof(CustomsCooItem.SecdGoodsQtyRef),
                    nameof(CustomsCooItem.SecdGoodsUnitRef),
                    nameof(CustomsCooItem.GrossWt),
                    nameof(CustomsCooItem.NetWt),
                    nameof(CustomsCooItem.WtUnit),
                    nameof(CustomsCooItem.InvPrice),
                    nameof(CustomsCooItem.InvValue),
                    nameof(CustomsCooItem.FobValue)
                ],
                ["goods_origin_standard"] =
                [
                    nameof(CustomsCooItem.HSCode),
                    nameof(CustomsCooItem.OriCriteria),
                    nameof(CustomsCooItem.OriCriteriaRef),
                    nameof(CustomsCooItem.OriCriteriaSub),
                    nameof(CustomsCooItem.GoodsOriginCountry),
                    nameof(CustomsCooItem.GoodsOriginCountryEn),
                    nameof(CustomsCooItem.InvNo)
                ],
                ["goods_producer"] =
                [
                    nameof(CustomsCooItem.ICompPrpr),
                    nameof(CustomsCooItem.Producer),
                    nameof(CustomsCooItem.ProducerTel),
                    nameof(CustomsCooItem.ProducerFax),
                    nameof(CustomsCooItem.ProducerEmail),
                    nameof(CustomsCooItem.CiqRegNo),
                    nameof(CustomsCooItem.PrdcEtpsName),
                    nameof(CustomsCooItem.PrdcEtpsConcEr),
                    nameof(CustomsCooItem.PrdcEtpsTel),
                    nameof(CustomsCooItem.ProducerSertFlag),
                    nameof(CustomsCooItem.GoodsTaxRate)
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> CustomsCooScopedAttachmentStringFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["attachment_identity"] =
                [
                    nameof(CustomsCooAttachment.CertType),
                    nameof(CustomsCooAttachment.AplRegNo),
                    nameof(CustomsCooAttachment.CiqRegNo),
                    nameof(CustomsCooAttachment.FileType),
                    nameof(CustomsCooAttachment.DocType)
                ],
                ["attachment_note_delay"] = [nameof(CustomsCooAttachment.Description)]
            };

        public static IReadOnlyDictionary<string, GroupScopedClearOption[]> AgentConsignmentScopedClearOptionsByGroup { get; } =
            new Dictionary<string, GroupScopedClearOption[]>(StringComparer.Ordinal)
            {
                [AgentConsignmentDefaultSectionKey] =
                [
                    new("identity", "企业与操作", "只恢复企业内部编号和操作类型。"),
                    new("goods", "签名与货物", "只恢复数字签名、主要货物名称和 HS 编码。")
                ],
                [AgentConsignmentDeclarationSectionKey] =
                [
                    new("schedule_doc", "日期与单号", "只恢复进出口日期和提单号。"),
                    new("trade_code", "贸易与编码", "只恢复贸易方式、原产地、经营/申报单位和币制。"),
                    new("quantity_note", "数量与补充", "只恢复总价、数量重量、包装情况和其他要求。")
                ],
                [AgentConsignmentDocumentSectionKey] =
                [
                    new("contact", "联系电话", "只恢复委托方电话和被委托方电话。"),
                    new("receipt", "收件信息", "只恢复收到证件日期、收到单证情况和其他收件信息。"),
                    new("document_fee", "单证与费用", "只恢复报关单编号、报关收费和承诺说明。")
                ]
            };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> AgentConsignmentScopedFieldKeysByCategory { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["identity"] = [nameof(AgentConsignmentDocument.CopCusCode), nameof(AgentConsignmentDocument.OperType)],
                ["goods"] = [nameof(AgentConsignmentDocument.Sign), nameof(AgentConsignmentDocument.GName), nameof(AgentConsignmentDocument.CodeTS)],
                ["schedule_doc"] = [nameof(AgentConsignmentDocument.IEDate), nameof(AgentConsignmentDocument.ListNo)],
                ["trade_code"] = [nameof(AgentConsignmentDocument.TradeMode), nameof(AgentConsignmentDocument.OriCountry), nameof(AgentConsignmentDocument.TradeCode), nameof(AgentConsignmentDocument.AgentCode), nameof(AgentConsignmentDocument.Curr)],
                ["quantity_note"] = [nameof(AgentConsignmentDocument.DeclTotal), nameof(AgentConsignmentDocument.QtyOrWeight), nameof(AgentConsignmentDocument.PackingCondition), nameof(AgentConsignmentDocument.OtherNote)],
                ["contact"] = [nameof(AgentConsignmentDocument.ConsignTele), nameof(AgentConsignmentDocument.DeclTele)],
                ["receipt"] = [nameof(AgentConsignmentDocument.ReceiveDate), nameof(AgentConsignmentDocument.PaperInfo), nameof(AgentConsignmentDocument.OtherRecInfo)],
                ["document_fee"] = [nameof(AgentConsignmentDocument.EntryId), nameof(AgentConsignmentDocument.DeclarePrice), nameof(AgentConsignmentDocument.PromiseNote)]
            };
    }
}
