using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowSourceCloneHelper
    {
        public static Invoice CloneInvoice(Invoice invoice) => invoice?.CloneHeader();

        public static List<Item> CloneItems(IEnumerable<Item> items) =>
            items?
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList()
            ?? [];

        public static Customer CloneCustomer(Customer customer)
        {
            if (customer == null)
            {
                return null;
            }

            return new Customer
            {
                Id = customer.Id,
                CustomerNameEN = customer.CustomerNameEN,
                NotifyPartyName = customer.NotifyPartyName,
                AddressEN = customer.AddressEN,
                NotifyPartyAddress = customer.NotifyPartyAddress,
                ContactPerson = customer.ContactPerson,
                Phone = customer.Phone,
                Email = customer.Email,
                TaxId = customer.TaxId,
                Notes = customer.Notes,
                RowVersion = customer.RowVersion?.ToArray()
            };
        }

        public static Exporter CloneExporter(Exporter exporter)
        {
            if (exporter == null)
            {
                return null;
            }

            return new Exporter
            {
                Id = exporter.Id,
                ExporterNameEN = exporter.ExporterNameEN,
                ExporterNameCN = exporter.ExporterNameCN,
                AddressEN = exporter.AddressEN,
                AddressCN = exporter.AddressCN,
                ContactPerson = exporter.ContactPerson,
                CreditCode = exporter.CreditCode,
                CustomsCode = exporter.CustomsCode,
                Phone = exporter.Phone,
                BankName = exporter.BankName,
                BankAccount = exporter.BankAccount,
                SwiftCode = exporter.SwiftCode,
                Notes = exporter.Notes,
                DocSealPath = exporter.DocSealPath,
                CustomsSealPath = exporter.CustomsSealPath,
                RowVersion = exporter.RowVersion?.ToArray()
            };
        }

        public static CustomsCooDocument CloneCustomsCooDocument(CustomsCooDocument document)
        {
            if (document == null)
            {
                return null;
            }

            return new CustomsCooDocument
            {
                Id = document.Id,
                SourceInvoiceId = document.SourceInvoiceId,
                InvoiceNo = document.InvoiceNo,
                ContractNo = document.ContractNo,
                Status = document.Status,
                CertNo = document.CertNo,
                ApplyType = document.ApplyType,
                CertStatus = document.CertStatus,
                CertType = document.CertType,
                EntMgrNo = document.EntMgrNo,
                CiqRegNo = document.CiqRegNo,
                AplRegNo = document.AplRegNo,
                EtpsName = document.EtpsName,
                ApplName = document.ApplName,
                Applicant = document.Applicant,
                ApplTel = document.ApplTel,
                OrgCode = document.OrgCode,
                FetchPlace = document.FetchPlace,
                AplAdd = document.AplAdd,
                InvDate = document.InvDate,
                InvNo = document.InvNo,
                AplDate = document.AplDate,
                DestCountry = document.DestCountry,
                DestCountryCode = document.DestCountryCode,
                DestCountryName = document.DestCountryName,
                Exporter = document.Exporter,
                Consignee = document.Consignee,
                GoodsSpecClause = document.GoodsSpecClause,
                Mark = document.Mark,
                LoadPort = document.LoadPort,
                UnloadPort = document.UnloadPort,
                TransMeans = document.TransMeans,
                TransName = document.TransName,
                TransCountryCode = document.TransCountryCode,
                TransCountryName = document.TransCountryName,
                TransPort = document.TransPort,
                DestPort = document.DestPort,
                TransDetails = document.TransDetails,
                IntendExpDate = document.IntendExpDate,
                TradeModeCode = document.TradeModeCode,
                FobValue = document.FobValue,
                TotalAmt = document.TotalAmt,
                Note = document.Note,
                LcNo = document.LcNo,
                SpecInvTerms = document.SpecInvTerms,
                PriceTerms = document.PriceTerms,
                Curr = document.Curr,
                Remark = document.Remark,
                Producer = document.Producer,
                ProducerSertFlag = document.ProducerSertFlag,
                ExhibitFlag = document.ExhibitFlag,
                ThirdPartyInvFlag = document.ThirdPartyInvFlag,
                ExporterTel = document.ExporterTel,
                ExporterFax = document.ExporterFax,
                ExporterEmail = document.ExporterEmail,
                ConsigneeTel = document.ConsigneeTel,
                ConsigneeFax = document.ConsigneeFax,
                ConsigneeEmail = document.ConsigneeEmail,
                PredictFlag = document.PredictFlag,
                ExpDeclDate = document.ExpDeclDate,
                OriCountryCode = document.OriCountryCode,
                OriCountry = document.OriCountry,
                ChkValidDate = document.ChkValidDate,
                EtpsConcEr = document.EtpsConcEr,
                EtpsTel = document.EtpsTel,
                EntryId = document.EntryId,
                PrcsAssembly = document.PrcsAssembly,
                OldCertNo = document.OldCertNo,
                ModReason = document.ModReason,
                ModColm = document.ModColm,
                OldSituDesc = document.OldSituDesc,
                ModSituDesc = document.ModSituDesc,
                OldDeclDate = document.OldDeclDate,
                OldIssueDate = document.OldIssueDate,
                AplPromiseCode = document.AplPromiseCode,
                WarningCount = document.WarningCount,
                WarningSummary = document.WarningSummary,
                LastGeneratedAt = document.LastGeneratedAt,
                Items = document.Items?
                    .Select(item => new CustomsCooItem
                    {
                        Id = item.Id,
                        DocumentId = item.DocumentId,
                        SourceItemId = item.SourceItemId,
                        SourceStyleNo = item.SourceStyleNo,
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
                        PackType = item.PackType,
                        GoodsTaxRate = item.GoodsTaxRate
                    })
                    .ToList() ?? [],
                NonpartyCorps = document.NonpartyCorps?
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
                    .ToList() ?? [],
                Attachments = document.Attachments?
                    .Select(item => new CustomsCooAttachment
                    {
                        Id = item.Id,
                        DocumentId = item.DocumentId,
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
                        FileExistsAtBuild = item.FileExistsAtBuild,
                        SortOrder = item.SortOrder
                    })
                    .ToList() ?? []
            };
        }

        public static AgentConsignmentDocument CloneAgentConsignmentDocument(AgentConsignmentDocument document)
        {
            if (document == null)
            {
                return null;
            }

            return new AgentConsignmentDocument
            {
                Id = document.Id,
                SourceInvoiceId = document.SourceInvoiceId,
                InvoiceNo = document.InvoiceNo,
                ContractNo = document.ContractNo,
                Status = document.Status,
                CounterpartyStatus = document.CounterpartyStatus,
                CopCusCode = document.CopCusCode,
                Sign = document.Sign,
                OperType = document.OperType,
                GName = document.GName,
                CodeTS = document.CodeTS,
                DeclTotal = document.DeclTotal,
                IEDate = document.IEDate,
                ListNo = document.ListNo,
                TradeMode = document.TradeMode,
                OriCountry = document.OriCountry,
                TradeCode = document.TradeCode,
                AgentCode = document.AgentCode,
                Curr = document.Curr,
                QtyOrWeight = document.QtyOrWeight,
                PackingCondition = document.PackingCondition,
                OtherNote = document.OtherNote,
                ConsignTele = document.ConsignTele,
                EntryId = document.EntryId,
                ReceiveDate = document.ReceiveDate,
                PaperInfo = document.PaperInfo,
                OtherRecInfo = document.OtherRecInfo,
                DeclarePrice = document.DeclarePrice,
                PromiseNote = document.PromiseNote,
                DeclTele = document.DeclTele,
                ConsignNo = document.ConsignNo,
                WarningCount = document.WarningCount,
                WarningSummary = document.WarningSummary,
                LastGeneratedAt = document.LastGeneratedAt
            };
        }

        public static List<SingleWindowAttachmentSource> CloneAttachmentSources(IEnumerable<CustomsCooAttachment> attachments) =>
            attachments?
                .Where(item => item != null)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => new SingleWindowAttachmentSource
                {
                    CertNo = item.CertNo ?? string.Empty,
                    CertType = item.CertType ?? string.Empty,
                    AplRegNo = item.AplRegNo ?? string.Empty,
                    CiqRegNo = item.CiqRegNo ?? string.Empty,
                    FileName = item.FileName ?? string.Empty,
                    FilePath = item.FilePath ?? string.Empty,
                    MediaType = item.MediaType ?? string.Empty,
                    Description = item.Description ?? string.Empty,
                    FileType = item.FileType ?? string.Empty,
                    DocType = item.DocType ?? string.Empty,
                    IsDelay = item.IsDelay
                })
                .ToList()
            ?? [];
    }
}
