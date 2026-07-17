using ExportDocManager.Models.DTOs.SingleWindow;
using static ExportDocManager.Services.SingleWindow.SingleWindowFieldValidationHelper;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowXmlValidator
    {
        IReadOnlyList<string> ValidateForBuild(SingleWindowBusinessType businessType, object mappedDocument);
    }

    public sealed class SingleWindowXmlValidator : ISingleWindowXmlValidator
    {
        public IReadOnlyList<string> ValidateForBuild(SingleWindowBusinessType businessType, object mappedDocument)
        {
            var errors = new List<string>();
            switch (businessType)
            {
                case SingleWindowBusinessType.CustomsCoo:
                    ValidateCustomsCoo(mappedDocument as CooMappedDocument, errors);
                    break;
                case SingleWindowBusinessType.AgentConsignment:
                    ValidateAgentConsignment(mappedDocument as AcdMappedDocument, errors);
                    break;
                default:
                    errors.Add("未知的单一窗口业务类型。");
                    break;
            }

            return errors;
        }

        private static void ValidateCustomsCoo(CooMappedDocument document, ICollection<string> errors)
        {
            if (document == null)
            {
                errors.Add("海关原产地证映射结果为空。");
                return;
            }

            RequireValue(document.ApplyType, "申请类型(ApplyType)", errors);
            RequireValue(document.CertStatus, "证书类别(CertStatus)", errors);
            RequireValue(document.CertType, "证书类型(CertType)", errors);
            RequireValue(document.CertNo, "原产地证编号(CertNo)", errors);
            RequireValue(document.CiqRegNo, "出口商代码(CiqRegNo)", errors);
            RequireValue(document.AplRegNo, "录入企业代码(AplRegNo)", errors);
            RequireValue(document.EtpsName, "企业名称(EtpsName)", errors);
            RequireValue(document.ApplName, "申报员姓名(ApplName)", errors);
            RequireValue(document.Applicant, "申报员身份证号(Applicant)", errors);
            RequireValue(document.ApplTel, "申报员电话(ApplTel)", errors);
            RequireValue(document.OrgCode, "签证机构代码(OrgCode)", errors);
            RequireValue(document.FetchPlace, "领证机构代码(FetchPlace)", errors);
            RequireValue(document.AplAdd, "申请地址(AplAdd)", errors);
            RequireValue(document.AplDate, "申请日期(AplDate)", errors);
            RequireValue(document.Exporter, "出口商(Exporter)", errors);
            RequireValue(document.Consignee, "收货人(Consignee)", errors);
            RequireValue(document.DestCountry, "进口国/地区(DestCountry)", errors);
            RequireValue(document.DestCountryCode, "进口国/地区编码(DestCountryCode)", errors);
            RequireValue(document.DestCountryName, "进口国/地区中文名(DestCountryName)", errors);
            RequireValue(document.Mark, "唛头(Mark)", errors);
            RequireValue(document.AplPromiseCode, "申请企业承诺代码(AplPromiseCode)", errors);
            ValidateAllowedValues(document.ApplyType, ["0", "1"], "申请类型(ApplyType)", errors);
            ValidateAllowedValues(document.CertStatus, ["0", "1", "2", "3"], "证书类别(CertStatus)", errors);
            ValidateAllowedValues(document.ProducerSertFlag, ["Y", "N"], "是否生产商保密(ProducerSertFlag)", errors);
            ValidateAllowedValues(document.ExhibitFlag, ["0", "1"], "是否展览证书(ExhibitFlag)", errors);
            ValidateAllowedValues(document.ThirdPartyInvFlag, ["0", "1"], "第三方发票标志(ThirdPartyInvFlag)", errors);
            ValidateAllowedValues(document.PredictFlag, ["0", "1"], "预计离港日期标志(PredictFlag)", errors);
            ValidateAllowedValues(document.AplPromiseCode, ["1"], "申请企业承诺代码(AplPromiseCode)", errors);
            ValidateAllowedValues(document.TradeModeCode, CooTradeModeCodeValues, "贸易方式代码(TradeModeCode)", errors);
            ValidateMaxLength(document.CertNo, 20, "原产地证编号(CertNo)", errors);
            ValidateMaxLength(document.CertType, 2, "证书类型(CertType)", errors);
            ValidateMaxLength(document.EntMgrNo, 19, "企业编号(EntMgrNo)", errors);
            ValidateMaxLength(document.CiqRegNo, 18, "出口商代码(CiqRegNo)", errors);
            ValidateMaxLength(document.AplRegNo, 18, "录入企业代码(AplRegNo)", errors);
            ValidateMaxLength(document.EtpsName, 400, "企业名称(EtpsName)", errors);
            ValidateMaxLength(document.ApplName, 20, "申报员姓名(ApplName)", errors);
            ValidateMaxLength(document.Applicant, 18, "申报员身份证号(Applicant)", errors);
            ValidateMaxLength(document.ApplTel, 30, "申报员电话(ApplTel)", errors);
            ValidateDigits(document.OrgCode, 4, "签证机构代码(OrgCode)", errors);
            ValidateDigits(document.FetchPlace, 4, "领证机构代码(FetchPlace)", errors);
            ValidateMaxLength(document.AplAdd, 30, "申请地址(AplAdd)", errors);
            ValidateMaxLength(document.DestCountry, 80, "进口国/地区(DestCountry)", errors);
            ValidateMaxLength(document.DestCountryCode, 4, "进口国/地区编码(DestCountryCode)", errors);
            ValidateMaxLength(document.DestCountryName, 50, "进口国/地区中文名(DestCountryName)", errors);
            ValidateMaxLength(document.Exporter, 400, "出口商(Exporter)", errors);
            ValidateMaxLength(document.Consignee, 400, "收货人(Consignee)", errors);
            ValidateMaxLength(document.GoodsSpecClause, 2000, "货物特殊条款(GoodsSpecClause)", errors);
            ValidateMaxLength(document.Mark, 2000, "唛头(Mark)", errors);
            ValidateMaxLength(document.LoadPort, 50, "启运港(LoadPort)", errors);
            ValidateMaxLength(document.UnloadPort, 50, "卸货港(UnloadPort)", errors);
            ValidateMaxLength(document.TransMeans, 100, "运输方式(TransMeans)", errors);
            ValidateMaxLength(document.TransName, 100, "运输工具名称(TransName)", errors);
            ValidateMaxLength(document.TransCountryCode, 4, "转运国/地区代码(TransCountryCode)", errors);
            ValidateMaxLength(document.TransCountryName, 150, "转运国/地区名称(TransCountryName)", errors);
            ValidateMaxLength(document.TransPort, 50, "转运港(TransPort)", errors);
            ValidateMaxLength(document.DestPort, 50, "目的港(DestPort)", errors);
            ValidateMaxLength(document.InvNo, 50, "发票号(InvNo)", errors);
            ValidateMaxLength(document.TransDetails, 630, "运输细节(TransDetails)", errors);
            ValidateMaxLength(document.FobValue, 25, "FOB值(FOBValue)", errors);
            ValidateMaxLength(document.TotalAmt, 25, "总金额(TotalAmt)", errors);
            ValidateMaxLength(document.Note, 1000, "备注(Note)", errors);
            ValidateMaxLength(document.ContractNo, 200, "合同号(ContractNo)", errors);
            ValidateMaxLength(document.LcNo, 50, "信用证号(LcNo)", errors);
            ValidateMaxLength(document.TradeModeCode, 4, "贸易方式代码(TradeModeCode)", errors);
            ValidateMaxLength(document.SpecInvTerms, 1000, "发票特殊条款(SpecInvTerms)", errors);
            ValidateMaxLength(document.PriceTerms, 20, "价格条款(PriceTerms)", errors);
            ValidateMaxLength(document.Curr, 10, "货币单位(Curr)", errors);
            ValidateMaxLength(document.Remark, 300, "备注说明(Remark)", errors);
            ValidateMaxLength(document.Producer, 1000, "证书货物生产商描述(Producer)", errors);
            ValidateMaxLength(document.ExporterTel, 50, "出口商电话(ExporterTel)", errors);
            ValidateMaxLength(document.ExporterFax, 50, "出口商传真(ExporterFax)", errors);
            ValidateMaxLength(document.ExporterEmail, 50, "出口商邮箱(ExporterEmail)", errors);
            ValidateMaxLength(document.ConsigneeTel, 50, "进口商电话(ConsigneeTel)", errors);
            ValidateMaxLength(document.ConsigneeFax, 50, "进口商传真(ConsigneeFax)", errors);
            ValidateMaxLength(document.ConsigneeEmail, 50, "进口商邮箱(ConsigneeEmail)", errors);
            ValidateMaxLength(document.OriCountryCode, 4, "原产国/地区代码(OriCountryCode)", errors);
            ValidateMaxLength(document.OriCountry, 30, "原产国/地区(OriCountry)", errors);
            ValidateMaxLength(document.EtpsConcEr, 30, "企业联系人(EtpsConcEr)", errors);
            ValidateMaxLength(document.EtpsTel, 20, "企业联系电话(EtpsTel)", errors);
            ValidateMaxLength(document.PrcsAssembly, 30, "加工装配工序(PrcsAssembly)", errors);
            ValidateMaxLength(document.EntryId, 600, "报关单号(EntryId)", errors);
            ValidateExactDate(document.InvDate, "yyyy-MM-dd", "发票日期(InvDate)", errors);
            ValidateExactDate(document.AplDate, "yyyy-MM-dd", "申请日期(AplDate)", errors);
            ValidateExactDate(document.IntendExpDate, "yyyy-MM-dd", "出运日期(IntendExpDate)", errors);
            ValidateExactDate(document.ExpDeclDate, "yyyy-MM-dd", "出口报关日期(ExpDeclDate)", errors);
            ValidateExactDate(document.ChkValidDate, "yyyy-MM-dd", "签发有效日期(ChkValidDate)", errors);
            ValidateExactDate(document.OldDeclDate, "yyyy-MM-dd", "原证申请日期(OldDeclDate)", errors);
            ValidateExactDate(document.OldIssueDate, "yyyy-MM-dd", "原证签发日期(OldIssueDate)", errors);
            ValidateDecimal(document.FobValue, 19, 5, "FOB值(FOBValue)", errors);
            ValidateDecimal(document.TotalAmt, 19, 5, "总金额(TotalAmt)", errors);

            if (CustomsCooRuleCatalog.UsesRcepInvoiceInfo(document.CertType))
            {
                RequireValue(document.InvDate, "发票日期(InvDate)", errors);
                RequireValue(document.InvNo, "发票号(InvNo)", errors);
                RequireValue(document.Curr, "货币单位(Curr)", errors);
                RequireValue(document.PriceTerms, "价格条款(PriceClause)", errors);
                ValidateMaxLength(document.ContractNo, 50, "RCEP发票合同号(ContractNo)", errors);
                ValidateMaxLength(document.TotalAmt, 20, "RCEP发票金额(Value)", errors);
            }

            if (RequiresModificationFields(document.CertStatus))
            {
                RequireValue(document.OldCertNo, "原证书号(OldCertNo)", errors);
                RequireValue(document.ModReason, "更改/重发原因(ModReason)", errors);
            }

            if (CustomsCooRuleCatalog.RequiresTaiwanExporterContacts(document.CertType, document.ApplyType))
            {
                RequireValue(document.ExporterTel, "出口商电话(ExporterTel)", errors);
                RequireValue(document.ExporterFax, "出口商传真(ExporterFax)", errors);
                RequireValue(document.ExporterEmail, "出口商邮箱(ExporterEmail)", errors);
            }

            if (CustomsCooRuleCatalog.RequiresHeaderProducer(document.CertType, document.ThirdPartyInvFlag))
            {
                RequireValue(document.Producer, "证书货物生产商描述(Producer)", errors);
            }

            if (string.Equals(document.ThirdPartyInvFlag?.Trim(), "1", StringComparison.Ordinal))
            {
                if (document.NonpartyCorps == null || document.NonpartyCorps.Count == 0)
                {
                    errors.Add("第三方发票标志为 1 时，非缔约方公司信息(NonpartyCorpList)至少需要填写一行。");
                }

                foreach (var corp in document.NonpartyCorps ?? [])
                {
                    if (corp.SortNo <= 0)
                    {
                        errors.Add("非缔约方公司信息的序号(SortNo)必须大于 0。");
                    }
                    RequireValue(corp.EntName, $"非缔约方企业名称(SortNo={corp.SortNo})", errors);
                    RequireValue(corp.EntCountryCode, $"非缔约方国别/地区代码(SortNo={corp.SortNo})", errors);
                    RequireValue(corp.EntCountryName, $"非缔约方国别/地区名称(SortNo={corp.SortNo})", errors);
                    ValidateMaxLength(corp.EntName, 500, $"非缔约方企业名称(SortNo={corp.SortNo})", errors);
                    ValidateMaxLength(corp.EntAddr, 1000, $"非缔约方企业地址(SortNo={corp.SortNo})", errors);
                    ValidateMaxLength(corp.EntCountryCode, 3, $"非缔约方国别/地区代码(SortNo={corp.SortNo})", errors);
                    ValidateMaxLength(corp.EntCountryName, 100, $"非缔约方国别/地区名称(SortNo={corp.SortNo})", errors);
                }
            }

            foreach (var item in document.Goods ?? [])
            {
                bool isNonGoodsItem = CustomsCooGoodsItemFlagCatalog.IsNonGoods(item.GoodsItemFlag);
                RequireValue(item.GoodsItemFlag, $"非货物项标志(GNo={item.GNo})", errors);
                RequireValue(item.GoodsNameE, $"货物名称英文(GNo={item.GNo})", errors);
                RequireValue(item.PackQty, $"包装件数(GNo={item.GNo})", errors);
                RequireValue(item.PackUnit, $"包装单位英文(GNo={item.GNo})", errors);
                if (!isNonGoodsItem)
                {
                    RequireValue(item.HSCode, $"商品HS编码(GNo={item.GNo})", errors);
                    RequireValue(item.GoodsName, $"货物名称中文(GNo={item.GNo})", errors);
                    RequireValue(item.GoodsQty, $"标准货物数量(GNo={item.GNo})", errors);
                    RequireValue(item.GoodsUnitE, $"标准货物单位英文(GNo={item.GNo})", errors);
                    RequireValue(item.GoodsUnit, $"标准货物单位中文(GNo={item.GNo})", errors);
                    RequireValue(item.InvValue, $"发票金额(GNo={item.GNo})", errors);
                    RequireValue(item.FobValue, $"FOB值(GNo={item.GNo})", errors);
                }

                if (string.IsNullOrWhiteSpace(item.GoodsDesc))
                {
                    errors.Add(BuildGoodsDescriptionRequiredMessage(item));
                }

                RequireValue(item.PackType, $"包装类型(GNo={item.GNo})", errors);
                ValidateAllowedValues(item.GoodsItemFlag, CustomsCooGoodsItemFlagCatalog.AllowedCodes, $"非货物项标志(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsItemFlag, 1, $"非货物项标志(GNo={item.GNo})", errors);
                ValidateMaxLength(item.HSCode, 10, $"商品HS编码(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsName, 200, $"货物名称中文(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsNameE, 400, $"货物名称英文(GNo={item.GNo})", errors);
                ValidateMaxLength(item.PackUnit, 20, $"包装单位(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsUnitE, 20, $"标准货物单位英文(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsUnit, 20, $"标准货物单位中文(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsUnitRef, 20, $"辅助单位(GNo={item.GNo})", errors);
                ValidateMaxLength(item.SecdGoodsUnitRef, 20, $"第二辅助单位(GNo={item.GNo})", errors);
                ValidateMaxLength(item.WtUnit, 10, $"重量单位(GNo={item.GNo})", errors);
                ValidateMaxLength(item.ICompPrpr, 10, $"进口成份比例(GNo={item.GNo})", errors);
                ValidateMaxLength(item.OriCriteria, 10, $"原产标准(GNo={item.GNo})", errors);
                ValidateMaxLength(item.OriCriteriaRef, 10, $"原产标准辅助项(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsOriginCountry, 3, $"协定原产国代码(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsOriginCountryEn, 80, $"协定原产国英文(GNo={item.GNo})", errors);
                ValidateMaxLength(item.Producer, 1000, $"生产商描述(GNo={item.GNo})", errors);
                ValidateMaxLength(item.ProducerTel, 50, $"生产商电话(GNo={item.GNo})", errors);
                ValidateMaxLength(item.ProducerFax, 50, $"生产商传真(GNo={item.GNo})", errors);
                ValidateMaxLength(item.ProducerEmail, 50, $"生产商邮箱(GNo={item.GNo})", errors);
                ValidateMaxLength(item.CiqRegNo, 10, $"生产企业组织机构代码(GNo={item.GNo})", errors);
                ValidateMaxLength(item.PrdcEtpsName, 400, $"生产企业名称(GNo={item.GNo})", errors);
                ValidateMaxLength(item.PrdcEtpsConcEr, 20, $"生产企业联系人(GNo={item.GNo})", errors);
                ValidateMaxLength(item.PrdcEtpsTel, 20, $"生产企业联系电话(GNo={item.GNo})", errors);
                ValidateMaxLength(item.ProducerSertFlag, 1, $"是否生产商保密(GNo={item.GNo})", errors);
                ValidateMaxLength(item.OriCriteriaSub, 1, $"原产标准子项(GNo={item.GNo})", errors);
                ValidateMaxLength(item.InvNo, 50, $"发票号(GNo={item.GNo})", errors);
                ValidateMaxLength(item.PackType, 1, $"包装类型(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsTaxRate, 1, $"最高税率标志(GNo={item.GNo})", errors);
                ValidateMaxLength(item.GoodsDesc, 1000, $"货物描述(GNo={item.GNo})", errors);
                ValidateDecimal(item.PackQty, 19, 5, $"包装件数(GNo={item.GNo})", errors);
                ValidateDecimal(item.GoodsQty, 19, 5, $"标准货物数量(GNo={item.GNo})", errors);
                ValidateDecimal(item.GoodsQtyRef, 19, 5, $"辅助数量(GNo={item.GNo})", errors);
                ValidateDecimal(item.SecdGoodsQtyRef, 19, 5, $"第二辅助数量(GNo={item.GNo})", errors);
                ValidateDecimal(item.GrossWt, 19, 5, $"毛重(GNo={item.GNo})", errors);
                ValidateDecimal(item.NetWt, 19, 5, $"净重(GNo={item.GNo})", errors);
                ValidateDecimal(item.InvPrice, 19, 5, $"发票单价(GNo={item.GNo})", errors);
                ValidateDecimal(item.InvValue, 19, 5, $"发票金额(GNo={item.GNo})", errors);
                ValidateDecimal(item.FobValue, 19, 5, $"FOB值(GNo={item.GNo})", errors);
                if (!isNonGoodsItem &&
                    !CustomsCooOriginCriteriaCatalog.IsValidOriginCriteria(document.CertType, item.OriCriteria))
                {
                    errors.Add($"原产标准(GNo={item.GNo})与当前证型不匹配。");
                }

                if (!isNonGoodsItem &&
                    !CustomsCooOriginCriteriaCatalog.IsValidOriginCriteriaSub(document.CertType, item.OriCriteria, item.OriCriteriaSub))
                {
                    errors.Add($"原产标准子项(GNo={item.GNo})与当前证型/主标准不匹配。");
                }

                bool usesPercentOriginCriteriaRef = CustomsCooOriginCriteriaCatalog.UsesOriginCriteriaRefPercent(document.CertType, item.OriCriteria);
                int percentMax = CustomsCooOriginCriteriaCatalog.GetOriginCriteriaRefPercentMax(document.CertType, item.OriCriteria) ?? 100;
                ValidatePercentText(
                    item.OriCriteriaRef,
                    percentMax,
                    2,
                    $"原产标准辅助项/进口成分比例(GNo={item.GNo})",
                    errors,
                    enabled: usesPercentOriginCriteriaRef);
                ValidateAllowedValues(item.ProducerSertFlag, ["Y", "N"], $"是否生产商保密(GNo={item.GNo})", errors);
                ValidateAllowedValues(item.GoodsTaxRate, ["0", "1", "2"], $"最高税率标志(GNo={item.GNo})", errors);
                ValidateAllowedValues(item.PackType, CustomsCooPackTypeCatalog.AllowedCodes, $"包装类型(GNo={item.GNo})", errors);
                ValidatePercentText(
                    item.ICompPrpr,
                    100,
                    2,
                    $"RCEP 进口成份比例(GNo={item.GNo})",
                    errors,
                    enabled: UsesRcepImportComponentRatio(document.CertType, item.OriCriteria));

                if (!isNonGoodsItem && CustomsCooRuleCatalog.UsesGoodsOriginCriteria(document.CertType))
                {
                    if (CustomsCooOriginCriteriaCatalog.RequiresOriginCriteria(document.CertType) &&
                        string.IsNullOrWhiteSpace(item.OriCriteria))
                    {
                        RequireValue(item.OriCriteria, $"原产标准(GNo={item.GNo})", errors);
                    }

                    if (string.Equals(document.CertType?.Trim(), "E", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.OriCriteria?.Trim(), "PSR", StringComparison.OrdinalIgnoreCase))
                    {
                        RequireValue(item.OriCriteriaSub, $"FORM E 子标准(GNo={item.GNo})", errors);
                    }

                    if (CustomsCooOriginCriteriaCatalog.RequiresOriginCriteriaRef(document.CertType, item.OriCriteria))
                    {
                        if (string.Equals(item.OriCriteria?.Trim(), "W", StringComparison.OrdinalIgnoreCase))
                        {
                            RequireValue(item.OriCriteriaRef, $"普惠制 W 对应 HS 前4位(GNo={item.GNo})", errors);
                        }

                        if (string.Equals(item.OriCriteria?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
                        {
                            RequireValue(item.OriCriteriaRef, $"普惠制 Y 对应进口成分比例(GNo={item.GNo})", errors);
                        }
                    }
                }

                if (!isNonGoodsItem && UsesRcepImportComponentRatio(document.CertType, item.OriCriteria))
                {
                    RequireValue(item.ICompPrpr, $"RCEP 进口成份比例(GNo={item.GNo})", errors);
                }

                if (!isNonGoodsItem)
                {
                    RequireValue(item.CiqRegNo, $"生产企业组织机构代码(GNo={item.GNo})", errors);
                    RequireValue(item.PrdcEtpsName, $"生产企业名称(GNo={item.GNo})", errors);
                    RequireValue(item.PrdcEtpsConcEr, $"生产企业联系人(GNo={item.GNo})", errors);
                    RequireValue(item.PrdcEtpsTel, $"生产企业联系电话(GNo={item.GNo})", errors);
                }

                if (!isNonGoodsItem && CustomsCooRuleCatalog.UsesGoodsRcepFields(document.CertType))
                {
                    RequireValue(item.GoodsOriginCountry, $"协定原产国代码(GNo={item.GNo})", errors);
                    RequireValue(item.GoodsOriginCountryEn, $"协定原产国英文(GNo={item.GNo})", errors);
                    RequireValue(item.InvNo, $"发票号(GNo={item.GNo})", errors);
                }
            }
        }

        private static void ValidateAgentConsignment(AcdMappedDocument document, ICollection<string> errors)
        {
            if (document == null)
            {
                errors.Add("报关代理委托映射结果为空。");
                return;
            }

            RequireValue(document.CopCusCode, "企业内部编号(CopCusCode)", errors);
            RequireValue(document.GName, "主要货物名称(GName)", errors);
            RequireValue(document.CodeTS, "HS编码(CodeTS)", errors);
            RequireValue(document.DeclTotal, "货物总价(DeclTotal)", errors);
            RequireValue(document.IEDate, "进出口日期(IEDate)", errors);
            RequireValue(document.TradeMode, "贸易方式(TradeMode)", errors);
            RequireValue(document.OriCountry, "原产地/货源地(OriCountry)", errors);
            RequireValue(document.TradeCode, "经营单位(委托方)海关10位编码(TradeCode)", errors);
            RequireValue(document.AgentCode, "申报单位(被委托方)海关10位编码(AgentCode)", errors);
            ValidateMaxLength(document.CodeTS, 10, "HS编码(CodeTS)", errors);
            ValidateMaxLength(document.ListNo, 32, "提单号(ListNo)", errors);
            ValidateMaxLength(document.TradeMode, 4, "贸易方式(TradeMode)", errors);
            ValidateExactDate(document.IEDate, "yyyyMMdd", "进出口日期(IEDate)", errors);
            ValidateExactDate(document.ReceiveDate, "yyyyMMdd", "收到证件日期(ReceiveDate)", errors);
            ValidateDecimal(document.DeclTotal, 19, 4, "货物总价(DeclTotal)", errors);
            ValidateDecimal(document.DeclarePrice, 19, 2, "报关收费(DeclarePrice)", errors);
            ValidateDigits(document.CopCusCode, 10, "企业内部编号(CopCusCode)", errors);
            ValidateDigits(document.Curr, 3, "币制代码(Curr)", errors);
            ValidateDigits(document.OriCountry, 3, "原产地/货源地(OriCountry)", errors);
            ValidateDigits(document.AgentCode, 10, "申报单位(被委托方)海关10位编码(AgentCode)", errors);
            ValidateDigits(document.TradeCode, 10, "经营单位(委托方)海关10位编码(TradeCode)", errors);
        }

        private static bool RequiresModificationFields(string certStatus)
        {
            return CustomsCooRuleCatalog.UsesModificationFields(certStatus);
        }

        private static bool UsesRcepImportComponentRatio(string certType, string originCriteria)
        {
            return string.Equals(certType?.Trim(), "RC", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(originCriteria?.Trim(), "RVC", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildGoodsDescriptionRequiredMessage(CooMappedGoodsItem item)
        {
            string lineTag = $"货物描述(GNo={item?.GNo ?? 0})";
            if (item == null)
            {
                return $"{lineTag}不能为空。";
            }

            if (CustomsCooGoodsItemFlagCatalog.IsNonGoods(item.GoodsItemFlag))
            {
                return CustomsCooPackTypeCatalog.IsIrregular(item.PackType)
                    ? $"{lineTag}不能为空。当前为非货物项 + 非常规包装，请先生成货物描述或手工填写，并确认包装单位/形式使用 HANGING GARMENT、IN BULK、PCS IN NUDE 等官方英文口径。"
                    : $"{lineTag}不能为空。当前为非货物项，请先生成货物描述或手工填写。";
            }

            if (CustomsCooPackTypeCatalog.IsIrregular(item.PackType))
            {
                return $"{lineTag}不能为空。当前为非常规包装的正常货物项，请先生成货物描述或手工填写实际包装形式说明。";
            }

            return $"{lineTag}不能为空。请先在 COO 编辑器里点击“生成货物描述”或手工填写。";
        }
    }
}
