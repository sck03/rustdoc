using System.IO;
using System.Xml.Linq;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class CustomsCooXmlPayloadGenerator : ICustomsCooPayloadGenerator
    {
        public PayloadBuildResult BuildCertificateXml(CooMappedDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            XNamespace ns = "http://www.w3.org/2000/09/xmldsig#";
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(ns + "Certificate",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "schemaLocation", $"{ns} coo.xsd"),
                    CreateCertificateHead(ns, document),
                    new XElement(ns + "CertificateList", document.Goods.Select(item => CreateGoods(ns, item))),
                    CreateOptionalModCertificate(ns, document),
                    CreateOptionalNonpartyCorpList(ns, document),
                    CreateOptionalOriInvs(ns, document),
                    new XElement(ns + "AplPromise",
                        new XElement(ns + "AplPromiseCode", document.AplPromiseCode))));

            return new PayloadBuildResult
            {
                FileName = SingleWindowPayloadFileNameHelper.BuildBaseFileName(document.InvNo, "coo", ".xml"),
                Content = xml.ToString(SaveOptions.DisableFormatting),
                Warnings = document.Warnings
            };
        }

        public IReadOnlyList<PayloadBuildResult> BuildAttachmentXmls(CooMappedDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            return document.Attachments
                .Where(attachment => attachment.Exists)
                .Select(attachment =>
                {
                    var xml = new XDocument(
                        new XDeclaration("1.0", "UTF-8", null),
                        new XElement("File",
                            new XElement("CertNo", string.IsNullOrWhiteSpace(attachment.CertNo) ? document.CertNo : attachment.CertNo),
                            new XElement("CertType", string.IsNullOrWhiteSpace(attachment.CertType) ? document.CertType : attachment.CertType),
                            new XElement("AplRegNo", string.IsNullOrWhiteSpace(attachment.AplRegNo) ? document.AplRegNo : attachment.AplRegNo),
                            new XElement("CiqRegNo", string.IsNullOrWhiteSpace(attachment.CiqRegNo) ? document.CiqRegNo : attachment.CiqRegNo),
                            new XElement(
                                "FileType",
                                string.IsNullOrWhiteSpace(attachment.FileType)
                                    ? SingleWindowPayloadFileNameHelper.ResolveCooAttachmentFileType(attachment.FileName)
                                    : attachment.FileType),
                            new XElement("FileName", attachment.FileName),
                            new XElement(
                                "DocType",
                                string.IsNullOrWhiteSpace(attachment.DocType)
                                    ? SingleWindowPayloadFileNameHelper.ResolveDocType(attachment.FileName)
                                    : attachment.DocType),
                            new XElement("FileContent", Convert.ToBase64String(File.ReadAllBytes(attachment.FilePath))),
                            new XElement("IsDelay", attachment.IsDelay ? "1" : "0")));

                    return new PayloadBuildResult
                    {
                        FileName = SingleWindowPayloadFileNameHelper.BuildBaseFileName(Path.GetFileNameWithoutExtension(attachment.FileName), "coo-attachment", ".xml"),
                        Content = xml.ToString(SaveOptions.DisableFormatting),
                        Warnings = document.Warnings
                    };
                })
                .ToList();
        }

        private static XElement CreateCertificateHead(XNamespace ns, CooMappedDocument document)
        {
            return new XElement(ns + "CertificateHead",
                new XElement(ns + "CertNo", document.CertNo),
                new XElement(ns + "ApplyType", document.ApplyType),
                new XElement(ns + "CertStatus", document.CertStatus),
                new XElement(ns + "CertType", document.CertType),
                new XElement(ns + "EntMgrNo", document.EntMgrNo),
                new XElement(ns + "CiqRegNo", document.CiqRegNo),
                new XElement(ns + "AplRegNo", document.AplRegNo),
                new XElement(ns + "EtpsName", document.EtpsName),
                new XElement(ns + "ApplName", document.ApplName),
                new XElement(ns + "Applicant", document.Applicant),
                new XElement(ns + "ApplTel", document.ApplTel),
                new XElement(ns + "OrgCode", document.OrgCode),
                new XElement(ns + "FetchPlace", document.FetchPlace),
                new XElement(ns + "AplAdd", document.AplAdd),
                new XElement(ns + "InvDate", document.InvDate),
                new XElement(ns + "InvNo", document.InvNo),
                new XElement(ns + "AplDate", document.AplDate),
                new XElement(ns + "DestCountry", document.DestCountry),
                new XElement(ns + "DestCountryCode", document.DestCountryCode),
                new XElement(ns + "DestCountryName", document.DestCountryName),
                new XElement(ns + "Exporter", CustomsCooTextFormatter.EncodeXmlMultiline(document.Exporter)),
                new XElement(ns + "Consignee", CustomsCooTextFormatter.EncodeXmlMultiline(document.Consignee)),
                new XElement(ns + "GoodsSpecClause", CustomsCooTextFormatter.EncodeXmlMultiline(document.GoodsSpecClause)),
                new XElement(ns + "Mark", CustomsCooTextFormatter.EncodeXmlMultiline(document.Mark)),
                new XElement(ns + "LoadPort", document.LoadPort),
                new XElement(ns + "UnloadPort", document.UnloadPort),
                new XElement(ns + "TransMeans", document.TransMeans),
                new XElement(ns + "TransName", document.TransName),
                new XElement(ns + "TransCountryCode", document.TransCountryCode),
                new XElement(ns + "TransCountryName", document.TransCountryName),
                new XElement(ns + "TransPort", document.TransPort),
                new XElement(ns + "DestPort", document.DestPort),
                new XElement(ns + "TransDetails", CustomsCooTextFormatter.EncodeXmlMultiline(document.TransDetails)),
                new XElement(ns + "IntendExpDate", document.IntendExpDate),
                new XElement(ns + "TradeModeCode", document.TradeModeCode),
                new XElement(ns + "FOBValue", document.FobValue),
                new XElement(ns + "TotalAmt", document.TotalAmt),
                new XElement(ns + "Note", CustomsCooTextFormatter.EncodeXmlMultiline(document.Note)),
                new XElement(ns + "ContractNo", document.ContractNo),
                new XElement(ns + "LcNo", document.LcNo),
                new XElement(ns + "SpecInvTerms", CustomsCooTextFormatter.EncodeXmlMultiline(document.SpecInvTerms)),
                new XElement(ns + "PriceTerms", document.PriceTerms),
                new XElement(ns + "Curr", document.Curr),
                new XElement(ns + "Remark", CustomsCooTextFormatter.EncodeXmlMultiline(document.Remark)),
                new XElement(ns + "ProducerSertFlag", document.ProducerSertFlag),
                new XElement(ns + "ExhibitFlag", document.ExhibitFlag),
                new XElement(ns + "ThirdPartyInvFlag", document.ThirdPartyInvFlag),
                new XElement(ns + "ExporterTel", document.ExporterTel),
                new XElement(ns + "ExporterFax", document.ExporterFax),
                new XElement(ns + "ExporterEmail", document.ExporterEmail),
                new XElement(ns + "ConsigneeTel", document.ConsigneeTel),
                new XElement(ns + "ConsigneeFax", document.ConsigneeFax),
                new XElement(ns + "ConsigneeEmail", document.ConsigneeEmail),
                new XElement(ns + "PredictFlag", document.PredictFlag),
                new XElement(ns + "ExpDeclDate", document.ExpDeclDate),
                new XElement(ns + "OriCountryCode", document.OriCountryCode),
                new XElement(ns + "OriCountry", document.OriCountry),
                new XElement(ns + "ChkValidDate", document.ChkValidDate),
                new XElement(ns + "EtpsConcEr", document.EtpsConcEr),
                new XElement(ns + "EtpsTel", document.EtpsTel),
                new XElement(ns + "Producer", CustomsCooTextFormatter.EncodeXmlMultiline(document.Producer)),
                new XElement(ns + "PrcsAssembly", CustomsCooTextFormatter.EncodeXmlMultiline(document.PrcsAssembly)),
                new XElement(ns + "EntryId", document.EntryId));
        }

        private static XElement CreateOptionalModCertificate(XNamespace ns, CooMappedDocument document)
        {
            bool shouldEmit = IsModificationStatus(document.CertStatus) ||
                              HasValue(document.OldCertNo) ||
                              HasValue(document.ModReason) ||
                              HasValue(document.ModColm) ||
                              HasValue(document.OldSituDesc) ||
                              HasValue(document.ModSituDesc) ||
                              HasValue(document.OldDeclDate) ||
                              HasValue(document.OldIssueDate);

            if (!shouldEmit)
            {
                return null;
            }

            return new XElement(ns + "ModCertificate",
                new XElement(ns + "OldCertNo", document.OldCertNo),
                new XElement(ns + "ModReason", CustomsCooTextFormatter.EncodeXmlMultiline(document.ModReason)),
                new XElement(ns + "ModColm", document.ModColm),
                new XElement(ns + "OldSituDesc", CustomsCooTextFormatter.EncodeXmlMultiline(document.OldSituDesc)),
                new XElement(ns + "ModSituDesc", CustomsCooTextFormatter.EncodeXmlMultiline(document.ModSituDesc)),
                new XElement(ns + "OldDeclDate", document.OldDeclDate),
                new XElement(ns + "OldIssueDate", document.OldIssueDate));
        }

        private static XElement CreateOptionalOriInvs(XNamespace ns, CooMappedDocument document)
        {
            if (!CustomsCooRuleCatalog.UsesRcepInvoiceInfo(document.CertType))
            {
                return null;
            }

            return new XElement(ns + "OriInvs",
                new XElement(ns + "OriInv",
                    new XElement(ns + "CertNo", document.CertNo),
                    new XElement(ns + "InvNo", document.InvNo),
                    CreateOptionalElement(ns, "ContractNo", document.ContractNo),
                    CreateOptionalElement(ns, "LcNo", document.LcNo),
                    CreateOptionalElement(ns, "Value", document.TotalAmt),
                    new XElement(ns + "Curr", document.Curr),
                    new XElement(ns + "PriceClause", document.PriceTerms),
                    CreateOptionalElement(ns, "SpecInvTerms", CustomsCooTextFormatter.EncodeXmlMultiline(document.SpecInvTerms)),
                    new XElement(ns + "InvDate", document.InvDate)));
        }

        private static XElement CreateOptionalNonpartyCorpList(XNamespace ns, CooMappedDocument document)
        {
            if (document.NonpartyCorps == null || document.NonpartyCorps.Count == 0)
            {
                return null;
            }

            return new XElement(ns + "NonpartyCorpList",
                document.NonpartyCorps
                    .OrderBy(item => item.SortNo)
                    .Select(item => new XElement(ns + "NonpartyCorp",
                        new XElement(ns + "SortNo", item.SortNo),
                        new XElement(ns + "EntName", CustomsCooTextFormatter.EncodeXmlMultiline(item.EntName)),
                        new XElement(ns + "EntAddr", CustomsCooTextFormatter.EncodeXmlMultiline(item.EntAddr)),
                        new XElement(ns + "EntCountryCode", item.EntCountryCode),
                        new XElement(ns + "EntCountryName", item.EntCountryName))));
        }

        private static XElement CreateGoods(XNamespace ns, CooMappedGoodsItem item)
        {
            return new XElement(ns + "Goods",
                new XElement(ns + "GoodsItemFlag", item.GoodsItemFlag),
                new XElement(ns + "GNo", item.GNo),
                new XElement(ns + "HSCode", item.HSCode),
                new XElement(ns + "GoodsName", item.GoodsName),
                new XElement(ns + "GoodsNameE", item.GoodsNameE),
                new XElement(ns + "PackQty", item.PackQty),
                new XElement(ns + "PackUnit", item.PackUnit),
                new XElement(ns + "GoodsQty", item.GoodsQty),
                new XElement(ns + "GoodsUnitE", item.GoodsUnitE),
                new XElement(ns + "GoodsUnit", item.GoodsUnit),
                new XElement(ns + "GoodsQtyRef", item.GoodsQtyRef),
                new XElement(ns + "GoodsUnitRef", item.GoodsUnitRef),
                new XElement(ns + "SecdGoodsQtyRef", item.SecdGoodsQtyRef),
                new XElement(ns + "SecdGoodsUnitRef", item.SecdGoodsUnitRef),
                new XElement(ns + "GrossWt", item.GrossWt),
                new XElement(ns + "NetWt", item.NetWt),
                new XElement(ns + "WtUnit", item.WtUnit),
                new XElement(ns + "InvPrice", item.InvPrice),
                new XElement(ns + "InvValue", item.InvValue),
                new XElement(ns + "FOBValue", item.FobValue),
                new XElement(ns + "ICompPrpr", item.ICompPrpr),
                new XElement(ns + "OriCriteria", item.OriCriteria),
                new XElement(ns + "OriCriteriaRef", item.OriCriteriaRef),
                new XElement(ns + "Producer", CustomsCooTextFormatter.EncodeXmlMultiline(item.Producer)),
                new XElement(ns + "ProducerTel", item.ProducerTel),
                new XElement(ns + "ProducerFax", item.ProducerFax),
                new XElement(ns + "ProducerEmail", item.ProducerEmail),
                new XElement(ns + "CiqRegNo", item.CiqRegNo),
                new XElement(ns + "PrdcEtpsName", item.PrdcEtpsName),
                new XElement(ns + "PrdcEtpsConcEr", item.PrdcEtpsConcEr),
                new XElement(ns + "PrdcEtpsTel", item.PrdcEtpsTel),
                new XElement(ns + "ProducerSertFlag", item.ProducerSertFlag),
                new XElement(ns + "GoodsDesc", CustomsCooTextFormatter.EncodeXmlMultiline(item.GoodsDesc)),
                new XElement(ns + "OriCriteriaSub", item.OriCriteriaSub),
                new XElement(ns + "GoodsOriginCountry", item.GoodsOriginCountry),
                new XElement(ns + "GoodsOriginCountryEn", item.GoodsOriginCountryEn),
                new XElement(ns + "GoodsTaxRate", item.GoodsTaxRate),
                new XElement(ns + "InvNo", item.InvNo),
                new XElement(ns + "PackType", item.PackType));
        }

        private static bool HasValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static XElement CreateOptionalElement(XNamespace ns, string name, string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : new XElement(ns + name, value);
        }

        private static bool IsModificationStatus(string certStatus)
        {
            return string.Equals(certStatus, "1", StringComparison.Ordinal) ||
                   string.Equals(certStatus, "2", StringComparison.Ordinal) ||
                   string.Equals(certStatus, "3", StringComparison.Ordinal);
        }
    }

    public sealed class AgentConsignmentXmlPayloadGenerator : IAgentConsignmentPayloadGenerator
    {
        public PayloadBuildResult BuildRequestXml(AcdMappedDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("ImportAgrRequest",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "noNamespaceSchemaLocation", "AcdAgrInfo_ImportSave.xsd"),
                    new XElement("OperInfo",
                        new XElement("CopCusCode", document.CopCusCode),
                        new XElement("Sign", document.Sign),
                        new XElement("OperType", document.OperType)),
                    new XElement("ImportInfo",
                        new XElement("GName", document.GName),
                        new XElement("CodeTS", document.CodeTS),
                        new XElement("DeclTotal", document.DeclTotal),
                        new XElement("IEDate", document.IEDate),
                        new XElement("ListNo", document.ListNo),
                        new XElement("TradeMode", document.TradeMode),
                        new XElement("OriCountry", document.OriCountry),
                        new XElement("TradeCode", document.TradeCode),
                        new XElement("AgentCode", document.AgentCode),
                        new XElement("Curr", document.Curr),
                        new XElement("QtyOrWeight", document.QtyOrWeight),
                        new XElement("PackingCondition", document.PackingCondition),
                        new XElement("OtherNote", document.OtherNote),
                        new XElement("ConsignTele", document.ConsignTele),
                        new XElement("EntryID", document.EntryId),
                        new XElement("ReceiveDate", document.ReceiveDate),
                        new XElement("PaperInfo", document.PaperInfo),
                        new XElement("OtherRecInfo", document.OtherRecInfo),
                        new XElement("DeclarePrice", document.DeclarePrice),
                        new XElement("PromiseNote", document.PromiseNote),
                        new XElement("DeclTele", document.DeclTele))));

            return new PayloadBuildResult
            {
                FileName = SingleWindowPayloadFileNameHelper.BuildBaseFileName(document.ListNo, "acd", ".xml"),
                Content = xml.ToString(SaveOptions.DisableFormatting),
                Warnings = document.Warnings
            };
        }
    }

}
