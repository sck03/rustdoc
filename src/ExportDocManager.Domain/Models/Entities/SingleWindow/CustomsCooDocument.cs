using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    public class CustomsCooDocument
    {
        public int Id { get; set; }

        public int SourceInvoiceId { get; set; }

        [MaxLength(80)]
        public string InvoiceNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ContractNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(80)]
        public string CertNo { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ApplyType { get; set; } = string.Empty;

        [MaxLength(10)]
        public string CertStatus { get; set; } = string.Empty;

        [MaxLength(10)]
        public string CertType { get; set; } = string.Empty;

        [MaxLength(40)]
        public string EntMgrNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string CiqRegNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string AplRegNo { get; set; } = string.Empty;

        [MaxLength(400)]
        public string EtpsName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string ApplName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string Applicant { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ApplTel { get; set; } = string.Empty;

        [MaxLength(80)]
        public string OrgCode { get; set; } = string.Empty;

        [MaxLength(80)]
        public string FetchPlace { get; set; } = string.Empty;

        [MaxLength(300)]
        public string AplAdd { get; set; } = string.Empty;

        [MaxLength(30)]
        public string InvDate { get; set; } = string.Empty;

        [MaxLength(80)]
        public string InvNo { get; set; } = string.Empty;

        [MaxLength(30)]
        public string AplDate { get; set; } = string.Empty;

        [MaxLength(150)]
        public string DestCountry { get; set; } = string.Empty;

        [MaxLength(20)]
        public string DestCountryCode { get; set; } = string.Empty;

        [MaxLength(150)]
        public string DestCountryName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Exporter { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Consignee { get; set; } = string.Empty;

        public string GoodsSpecClause { get; set; } = string.Empty;

        public string Mark { get; set; } = string.Empty;

        [MaxLength(150)]
        public string LoadPort { get; set; } = string.Empty;

        [MaxLength(150)]
        public string UnloadPort { get; set; } = string.Empty;

        [MaxLength(150)]
        public string TransMeans { get; set; } = string.Empty;

        [MaxLength(150)]
        public string TransName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string TransCountryCode { get; set; } = string.Empty;

        [MaxLength(150)]
        public string TransCountryName { get; set; } = string.Empty;

        [MaxLength(150)]
        public string TransPort { get; set; } = string.Empty;

        [MaxLength(150)]
        public string DestPort { get; set; } = string.Empty;

        public string TransDetails { get; set; } = string.Empty;

        [MaxLength(30)]
        public string IntendExpDate { get; set; } = string.Empty;

        [MaxLength(20)]
        public string TradeModeCode { get; set; } = string.Empty;

        [MaxLength(40)]
        public string FobValue { get; set; } = string.Empty;

        [MaxLength(40)]
        public string TotalAmt { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        [MaxLength(80)]
        public string LcNo { get; set; } = string.Empty;

        public string SpecInvTerms { get; set; } = string.Empty;

        [MaxLength(40)]
        public string PriceTerms { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Curr { get; set; } = string.Empty;

        public string Remark { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Producer { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ProducerSertFlag { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ExhibitFlag { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ThirdPartyInvFlag { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ExporterTel { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ExporterFax { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ExporterEmail { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ConsigneeTel { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ConsigneeFax { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ConsigneeEmail { get; set; } = string.Empty;

        [MaxLength(10)]
        public string PredictFlag { get; set; } = string.Empty;

        [MaxLength(30)]
        public string ExpDeclDate { get; set; } = string.Empty;

        [MaxLength(20)]
        public string OriCountryCode { get; set; } = string.Empty;

        [MaxLength(150)]
        public string OriCountry { get; set; } = string.Empty;

        [MaxLength(30)]
        public string ChkValidDate { get; set; } = string.Empty;

        [MaxLength(100)]
        public string EtpsConcEr { get; set; } = string.Empty;

        [MaxLength(80)]
        public string EtpsTel { get; set; } = string.Empty;

        [MaxLength(120)]
        public string EntryId { get; set; } = string.Empty;

        public string PrcsAssembly { get; set; } = string.Empty;

        [MaxLength(80)]
        public string OldCertNo { get; set; } = string.Empty;

        [MaxLength(600)]
        public string ModReason { get; set; } = string.Empty;

        [MaxLength(260)]
        public string ModColm { get; set; } = string.Empty;

        public string OldSituDesc { get; set; } = string.Empty;

        public string ModSituDesc { get; set; } = string.Empty;

        [MaxLength(30)]
        public string OldDeclDate { get; set; } = string.Empty;

        [MaxLength(30)]
        public string OldIssueDate { get; set; } = string.Empty;

        [MaxLength(20)]
        public string AplPromiseCode { get; set; } = string.Empty;

        public int WarningCount { get; set; }

        public string WarningSummary { get; set; } = string.Empty;

        public int DraftRevision { get; set; }

        public string ManualLockedFieldsJson { get; set; } = string.Empty;

        public string SourceBaselineJson { get; set; } = string.Empty;

        [MaxLength(80)]
        public string SourceBaselineHash { get; set; } = string.Empty;

        public DateTime LastGeneratedAt { get; set; } = DateTime.MinValue;

        public List<CustomsCooItem> Items { get; set; } = [];

        public List<CustomsCooNonpartyCorp> NonpartyCorps { get; set; } = [];

        public List<CustomsCooAttachment> Attachments { get; set; } = [];

        [NotMapped]
        public int SourceDiffCount { get; set; }

        [NotMapped]
        public string SourceDiffSummary { get; set; } = string.Empty;

        [NotMapped]
        public int ManualLockedFieldCount { get; set; }
    }
}
