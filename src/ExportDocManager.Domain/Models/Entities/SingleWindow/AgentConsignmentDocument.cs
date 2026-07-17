using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    public class AgentConsignmentDocument
    {
        public int Id { get; set; }

        public int SourceInvoiceId { get; set; }

        [MaxLength(80)]
        public string InvoiceNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ContractNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(40)]
        public string CounterpartyStatus { get; set; } = string.Empty;

        [MaxLength(40)]
        public string CopCusCode { get; set; } = string.Empty;

        [MaxLength(520)]
        public string Sign { get; set; } = string.Empty;

        [MaxLength(10)]
        public string OperType { get; set; } = string.Empty;

        [MaxLength(255)]
        public string GName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string CodeTS { get; set; } = string.Empty;

        [MaxLength(40)]
        public string DeclTotal { get; set; } = string.Empty;

        [MaxLength(20)]
        public string IEDate { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ListNo { get; set; } = string.Empty;

        [MaxLength(20)]
        public string TradeMode { get; set; } = string.Empty;

        [MaxLength(40)]
        public string OriCountry { get; set; } = string.Empty;

        [MaxLength(40)]
        public string TradeCode { get; set; } = string.Empty;

        [MaxLength(40)]
        public string AgentCode { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Curr { get; set; } = string.Empty;

        [MaxLength(80)]
        public string QtyOrWeight { get; set; } = string.Empty;

        [MaxLength(255)]
        public string PackingCondition { get; set; } = string.Empty;

        public string OtherNote { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ConsignTele { get; set; } = string.Empty;

        [MaxLength(40)]
        public string EntryId { get; set; } = string.Empty;

        [MaxLength(20)]
        public string ReceiveDate { get; set; } = string.Empty;

        [MaxLength(80)]
        public string PaperInfo { get; set; } = string.Empty;

        public string OtherRecInfo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string DeclarePrice { get; set; } = string.Empty;

        public string PromiseNote { get; set; } = string.Empty;

        [MaxLength(80)]
        public string DeclTele { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ConsignNo { get; set; } = string.Empty;

        public int WarningCount { get; set; }

        public string WarningSummary { get; set; } = string.Empty;

        public int DraftRevision { get; set; }

        public string ManualLockedFieldsJson { get; set; } = string.Empty;

        public string SourceBaselineJson { get; set; } = string.Empty;

        [MaxLength(80)]
        public string SourceBaselineHash { get; set; } = string.Empty;

        public DateTime LastGeneratedAt { get; set; } = DateTime.MinValue;

        [NotMapped]
        public int SourceDiffCount { get; set; }

        [NotMapped]
        public string SourceDiffSummary { get; set; } = string.Empty;

        [NotMapped]
        public int ManualLockedFieldCount { get; set; }
    }
}
