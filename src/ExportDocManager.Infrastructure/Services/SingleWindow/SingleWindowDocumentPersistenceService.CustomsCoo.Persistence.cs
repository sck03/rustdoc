using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowDocumentPersistenceService
    {
        private static void ApplyEditableCustomsCooDocumentValues(CustomsCooDocument target, CustomsCooDocument source)
        {
            target.ApplyType = NormalizePersistedValue(source.ApplyType);
            target.CertStatus = NormalizePersistedValue(source.CertStatus);
            target.CertNo = NormalizePersistedValue(source.CertNo);
            target.CertType = NormalizePersistedValue(source.CertType);
            target.EntMgrNo = NormalizePersistedValue(source.EntMgrNo);
            target.CiqRegNo = NormalizePersistedValue(source.CiqRegNo);
            target.AplRegNo = NormalizePersistedValue(source.AplRegNo);
            target.EtpsName = NormalizePersistedValue(source.EtpsName);
            target.ApplName = NormalizePersistedValue(source.ApplName);
            target.Applicant = NormalizePersistedValue(source.Applicant);
            target.ApplTel = NormalizePersistedValue(source.ApplTel);
            target.OrgCode = NormalizePersistedValue(source.OrgCode);
            target.FetchPlace = NormalizePersistedValue(source.FetchPlace);
            target.AplAdd = NormalizePersistedValue(source.AplAdd);
            target.InvDate = NormalizePersistedValue(source.InvDate);
            target.InvNo = NormalizePersistedValue(source.InvNo);
            target.AplDate = NormalizePersistedValue(source.AplDate);
            target.DestCountry = NormalizePersistedValue(source.DestCountry);
            target.DestCountryCode = NormalizePersistedValue(source.DestCountryCode);
            target.DestCountryName = NormalizePersistedValue(source.DestCountryName);
            target.Exporter = NormalizePersistedValue(source.Exporter);
            target.Consignee = NormalizePersistedValue(source.Consignee);
            target.GoodsSpecClause = NormalizePersistedValue(source.GoodsSpecClause);
            target.Mark = NormalizePersistedValue(source.Mark);
            target.LoadPort = NormalizePersistedValue(source.LoadPort);
            target.UnloadPort = NormalizePersistedValue(source.UnloadPort);
            target.TransMeans = NormalizePersistedValue(source.TransMeans);
            target.TransName = NormalizePersistedValue(source.TransName);
            target.TransCountryCode = NormalizePersistedValue(source.TransCountryCode);
            target.TransCountryName = NormalizePersistedValue(source.TransCountryName);
            target.TransPort = NormalizePersistedValue(source.TransPort);
            target.DestPort = NormalizePersistedValue(source.DestPort);
            target.TransDetails = NormalizePersistedValue(source.TransDetails);
            target.IntendExpDate = NormalizePersistedValue(source.IntendExpDate);
            target.TradeModeCode = NormalizePersistedValue(source.TradeModeCode);
            target.FobValue = NormalizePersistedValue(source.FobValue);
            target.TotalAmt = NormalizePersistedValue(source.TotalAmt);
            target.Note = NormalizePersistedValue(source.Note);
            target.LcNo = NormalizePersistedValue(source.LcNo);
            target.SpecInvTerms = NormalizePersistedValue(source.SpecInvTerms);
            target.PriceTerms = NormalizePersistedValue(source.PriceTerms);
            target.Curr = NormalizePersistedValue(source.Curr);
            target.Remark = NormalizePersistedValue(source.Remark);
            target.Producer = NormalizePersistedValue(source.Producer);
            target.ProducerSertFlag = NormalizePersistedValue(source.ProducerSertFlag);
            target.ExhibitFlag = NormalizePersistedValue(source.ExhibitFlag);
            target.ThirdPartyInvFlag = NormalizePersistedValue(source.ThirdPartyInvFlag);
            target.ExporterTel = NormalizePersistedValue(source.ExporterTel);
            target.ExporterFax = NormalizePersistedValue(source.ExporterFax);
            target.ExporterEmail = NormalizePersistedValue(source.ExporterEmail);
            target.ConsigneeTel = NormalizePersistedValue(source.ConsigneeTel);
            target.ConsigneeFax = NormalizePersistedValue(source.ConsigneeFax);
            target.ConsigneeEmail = NormalizePersistedValue(source.ConsigneeEmail);
            target.PredictFlag = NormalizePersistedValue(source.PredictFlag);
            target.ExpDeclDate = NormalizePersistedValue(source.ExpDeclDate);
            target.OriCountryCode = NormalizePersistedValue(source.OriCountryCode);
            target.OriCountry = NormalizePersistedValue(source.OriCountry);
            target.ChkValidDate = NormalizePersistedValue(source.ChkValidDate);
            target.EtpsConcEr = NormalizePersistedValue(source.EtpsConcEr);
            target.EtpsTel = NormalizePersistedValue(source.EtpsTel);
            target.EntryId = NormalizePersistedValue(source.EntryId);
            target.PrcsAssembly = NormalizePersistedValue(source.PrcsAssembly);
            target.OldCertNo = NormalizePersistedValue(source.OldCertNo);
            target.ModReason = NormalizePersistedValue(source.ModReason);
            target.ModColm = NormalizePersistedValue(source.ModColm);
            target.OldSituDesc = NormalizePersistedValue(source.OldSituDesc);
            target.ModSituDesc = NormalizePersistedValue(source.ModSituDesc);
            target.OldDeclDate = NormalizePersistedValue(source.OldDeclDate);
            target.OldIssueDate = NormalizePersistedValue(source.OldIssueDate);
        }

        private static void ApplyMappedCustomsCooDocumentValues(
            CustomsCooDocument target,
            CooMappedDocument source,
            bool preserveExistingCertNoWhenEmpty)
        {
            target.CertNo = preserveExistingCertNoWhenEmpty && string.IsNullOrWhiteSpace(source.CertNo)
                ? NormalizePersistedValue(target.CertNo)
                : NormalizePersistedValue(source.CertNo);
            target.ApplyType = NormalizePersistedValue(source.ApplyType);
            target.CertStatus = NormalizePersistedValue(source.CertStatus);
            target.CertType = NormalizePersistedValue(source.CertType);
            target.EntMgrNo = NormalizePersistedValue(source.EntMgrNo);
            target.CiqRegNo = NormalizePersistedValue(source.CiqRegNo);
            target.AplRegNo = NormalizePersistedValue(source.AplRegNo);
            target.EtpsName = NormalizePersistedValue(source.EtpsName);
            target.ApplName = NormalizePersistedValue(source.ApplName);
            target.Applicant = NormalizePersistedValue(source.Applicant);
            target.ApplTel = NormalizePersistedValue(source.ApplTel);
            target.OrgCode = NormalizePersistedValue(source.OrgCode);
            target.FetchPlace = NormalizePersistedValue(source.FetchPlace);
            target.AplAdd = NormalizePersistedValue(source.AplAdd);
            target.InvDate = NormalizePersistedValue(source.InvDate);
            target.InvNo = NormalizePersistedValue(source.InvNo);
            target.AplDate = NormalizePersistedValue(source.AplDate);
            target.DestCountry = NormalizePersistedValue(source.DestCountry);
            target.DestCountryCode = NormalizePersistedValue(source.DestCountryCode);
            target.DestCountryName = NormalizePersistedValue(source.DestCountryName);
            target.Exporter = NormalizePersistedValue(source.Exporter);
            target.Consignee = NormalizePersistedValue(source.Consignee);
            target.GoodsSpecClause = NormalizePersistedValue(source.GoodsSpecClause);
            target.Mark = NormalizePersistedValue(source.Mark);
            target.LoadPort = NormalizePersistedValue(source.LoadPort);
            target.UnloadPort = NormalizePersistedValue(source.UnloadPort);
            target.TransMeans = NormalizePersistedValue(source.TransMeans);
            target.TransName = NormalizePersistedValue(source.TransName);
            target.TransCountryCode = NormalizePersistedValue(source.TransCountryCode);
            target.TransCountryName = NormalizePersistedValue(source.TransCountryName);
            target.TransPort = NormalizePersistedValue(source.TransPort);
            target.DestPort = NormalizePersistedValue(source.DestPort);
            target.TransDetails = NormalizePersistedValue(source.TransDetails);
            target.IntendExpDate = NormalizePersistedValue(source.IntendExpDate);
            target.TradeModeCode = NormalizePersistedValue(source.TradeModeCode);
            target.FobValue = NormalizePersistedValue(source.FobValue);
            target.TotalAmt = NormalizePersistedValue(source.TotalAmt);
            target.Note = NormalizePersistedValue(source.Note);
            target.LcNo = NormalizePersistedValue(source.LcNo);
            target.SpecInvTerms = NormalizePersistedValue(source.SpecInvTerms);
            target.PriceTerms = NormalizePersistedValue(source.PriceTerms);
            target.Curr = NormalizePersistedValue(source.Curr);
            target.Remark = NormalizePersistedValue(source.Remark);
            target.Producer = NormalizePersistedValue(source.Producer);
            target.ProducerSertFlag = NormalizePersistedValue(source.ProducerSertFlag);
            target.ExhibitFlag = NormalizePersistedValue(source.ExhibitFlag);
            target.ThirdPartyInvFlag = NormalizePersistedValue(source.ThirdPartyInvFlag);
            target.ExporterTel = NormalizePersistedValue(source.ExporterTel);
            target.ExporterFax = NormalizePersistedValue(source.ExporterFax);
            target.ExporterEmail = NormalizePersistedValue(source.ExporterEmail);
            target.ConsigneeTel = NormalizePersistedValue(source.ConsigneeTel);
            target.ConsigneeFax = NormalizePersistedValue(source.ConsigneeFax);
            target.ConsigneeEmail = NormalizePersistedValue(source.ConsigneeEmail);
            target.PredictFlag = NormalizePersistedValue(source.PredictFlag);
            target.ExpDeclDate = NormalizePersistedValue(source.ExpDeclDate);
            target.OriCountryCode = NormalizePersistedValue(source.OriCountryCode);
            target.OriCountry = NormalizePersistedValue(source.OriCountry);
            target.ChkValidDate = NormalizePersistedValue(source.ChkValidDate);
            target.EtpsConcEr = NormalizePersistedValue(source.EtpsConcEr);
            target.EtpsTel = NormalizePersistedValue(source.EtpsTel);
            target.EntryId = NormalizePersistedValue(source.EntryId);
            target.PrcsAssembly = NormalizePersistedValue(source.PrcsAssembly);
            target.OldCertNo = NormalizePersistedValue(source.OldCertNo);
            target.ModReason = NormalizePersistedValue(source.ModReason);
            target.ModColm = NormalizePersistedValue(source.ModColm);
            target.OldSituDesc = NormalizePersistedValue(source.OldSituDesc);
            target.ModSituDesc = NormalizePersistedValue(source.ModSituDesc);
            target.OldDeclDate = NormalizePersistedValue(source.OldDeclDate);
            target.OldIssueDate = NormalizePersistedValue(source.OldIssueDate);
            target.AplPromiseCode = NormalizePersistedValue(source.AplPromiseCode);
            target.WarningCount = source.Warnings?.Count ?? 0;
            target.WarningSummary = BuildWarningSummary(source.Warnings);
        }

        private static void EnsureGeneratedCustomsCooBaseline(
            CustomsCooDocument entity,
            Invoice invoice,
            IReadOnlyList<Item> invoiceItems,
            CooMappedDocument document)
        {
            if (!string.IsNullOrWhiteSpace(entity.SourceBaselineJson))
            {
                return;
            }

            var baselineDocument = CreateCustomsCooDocumentFromMapped(
                invoice.Id,
                invoice,
                invoiceItems,
                null,
                [],
                document);
            entity.SourceBaselineJson = SingleWindowDraftStateHelper.BuildCustomsCooSourceBaselineJson(baselineDocument);
        }

        private static CustomsCooDocument CreateCustomsCooDocumentFromMapped(
            int invoiceId,
            Invoice invoice,
            IReadOnlyList<Item> invoiceItems,
            CustomsCooDocument baseDocument,
            IReadOnlyList<SingleWindowAttachmentSource> attachmentSources,
            CooMappedDocument mapped)
        {
            var document = baseDocument != null
                ? SingleWindowSourceCloneHelper.CloneCustomsCooDocument(baseDocument)
                : new CustomsCooDocument
                {
                    SourceInvoiceId = invoiceId
                };

            document.SourceInvoiceId = invoiceId;
            document.InvoiceNo = invoice.InvoiceNo ?? string.Empty;
            document.ContractNo = invoice.ContractNo ?? string.Empty;
            ApplyMappedCustomsCooDocumentValues(document, mapped, preserveExistingCertNoWhenEmpty: true);
            document.CertNo = CustomsCooCertNoGenerator.NormalizeOrGenerate(
                document.CertNo,
                document.CertType,
                document.CiqRegNo,
                document.AplRegNo,
                document.AplDate);
            document.NonpartyCorps = (baseDocument?.NonpartyCorps ?? [])
                .OrderBy(item => item.SortNo)
                .Select(item => new CustomsCooNonpartyCorp
                {
                    Id = item.Id,
                    DocumentId = item.DocumentId,
                    SortNo = item.SortNo,
                    EntName = item.EntName,
                    EntAddr = item.EntAddr,
                    EntCountryCode = item.EntCountryCode,
                    EntCountryName = item.EntCountryName
                })
                .ToList();

            var existingItems = (baseDocument?.Items ?? [])
                .OrderBy(item => item.GNo)
                .ToList();
            document.Items = (mapped.Goods ?? [])
                .Select((item, index) =>
                {
                    var sourceItem = invoiceItems.ElementAtOrDefault(index);
                    var existingItem = existingItems.ElementAtOrDefault(index);
                    return new CustomsCooItem
                    {
                        SourceItemId = sourceItem?.Id ?? existingItem?.SourceItemId ?? 0,
                        SourceStyleNo = sourceItem?.StyleNo ?? existingItem?.SourceStyleNo ?? string.Empty,
                        GoodsItemFlag = CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(item.GoodsItemFlag),
                        GNo = item.GNo,
                        HSCode = item.HSCode,
                        GoodsName = item.GoodsName,
                        GoodsNameE = item.GoodsNameE,
                        PackQty = item.PackQty,
                        PackUnit = item.PackUnit,
                        GoodsQty = item.GoodsQty,
                        GoodsQtyRef = item.GoodsQtyRef,
                        GoodsUnitE = item.GoodsUnitE,
                        GoodsUnit = item.GoodsUnit,
                        GoodsUnitRef = item.GoodsUnitRef,
                        SecdGoodsQtyRef = item.SecdGoodsQtyRef,
                        SecdGoodsUnitRef = item.SecdGoodsUnitRef,
                        GrossWt = item.GrossWt,
                        NetWt = item.NetWt,
                        WtUnit = item.WtUnit,
                        InvPrice = item.InvPrice,
                        InvValue = item.InvValue,
                        FobValue = item.FobValue,
                        ICompPrpr = item.ICompPrpr,
                        GoodsDesc = item.GoodsDesc,
                        OriCriteria = item.OriCriteria,
                        OriCriteriaRef = item.OriCriteriaRef,
                        GoodsOriginCountry = item.GoodsOriginCountry,
                        GoodsOriginCountryEn = item.GoodsOriginCountryEn,
                        Producer = item.Producer,
                        ProducerTel = item.ProducerTel,
                        ProducerFax = item.ProducerFax,
                        ProducerEmail = item.ProducerEmail,
                        CiqRegNo = item.CiqRegNo,
                        PrdcEtpsName = item.PrdcEtpsName,
                        PrdcEtpsConcEr = item.PrdcEtpsConcEr,
                        PrdcEtpsTel = item.PrdcEtpsTel,
                        ProducerSertFlag = item.ProducerSertFlag,
                        OriCriteriaSub = item.OriCriteriaSub,
                        InvNo = item.InvNo,
                        PackType = CustomsCooPackTypeCatalog.NormalizeOrDefault(item.PackType),
                        GoodsTaxRate = item.GoodsTaxRate
                    };
                })
                .ToList();

            document.Attachments = (attachmentSources ?? [])
                .Select((item, index) => new CustomsCooAttachment
                {
                    CertNo = item.CertNo,
                    CertType = item.CertType,
                    AplRegNo = item.AplRegNo,
                    CiqRegNo = item.CiqRegNo,
                    FileType = item.FileType,
                    FileName = item.FileName,
                    FilePath = item.FilePath,
                    MediaType = item.MediaType,
                    Description = item.Description,
                    DocType = item.DocType,
                    IsDelay = item.IsDelay,
                    FileExistsAtBuild = item.Exists,
                    SortOrder = index + 1
                })
                .ToList();

            return document;
        }

        private static string NormalizePersistedValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
