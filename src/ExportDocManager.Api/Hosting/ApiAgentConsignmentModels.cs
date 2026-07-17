namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiAgentConsignmentDocumentDto
    {
        public int Id { get; set; }
        public int SourceInvoiceId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public string ContractNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CounterpartyStatus { get; set; } = string.Empty;
        public string CopCusCode { get; set; } = string.Empty;
        public string Sign { get; set; } = string.Empty;
        public string OperType { get; set; } = string.Empty;
        public string GName { get; set; } = string.Empty;
        public string CodeTS { get; set; } = string.Empty;
        public string DeclTotal { get; set; } = string.Empty;
        public string IEDate { get; set; } = string.Empty;
        public string ListNo { get; set; } = string.Empty;
        public string TradeMode { get; set; } = string.Empty;
        public string OriCountry { get; set; } = string.Empty;
        public string TradeCode { get; set; } = string.Empty;
        public string AgentCode { get; set; } = string.Empty;
        public string Curr { get; set; } = string.Empty;
        public string QtyOrWeight { get; set; } = string.Empty;
        public string PackingCondition { get; set; } = string.Empty;
        public string OtherNote { get; set; } = string.Empty;
        public string ConsignTele { get; set; } = string.Empty;
        public string EntryId { get; set; } = string.Empty;
        public string ReceiveDate { get; set; } = string.Empty;
        public string PaperInfo { get; set; } = string.Empty;
        public string OtherRecInfo { get; set; } = string.Empty;
        public string DeclarePrice { get; set; } = string.Empty;
        public string PromiseNote { get; set; } = string.Empty;
        public string DeclTele { get; set; } = string.Empty;
        public string ConsignNo { get; set; } = string.Empty;
        public int WarningCount { get; set; }
        public string WarningSummary { get; set; } = string.Empty;
        public int DraftRevision { get; set; }
        public DateTime LastGeneratedAt { get; set; } = DateTime.MinValue;
        public int SourceDiffCount { get; set; }
        public string SourceDiffSummary { get; set; } = string.Empty;
        public int ManualLockedFieldCount { get; set; }
    }

    public sealed record ApiAgentConsignmentDocumentSaveResponse(
        bool Success,
        int Id,
        ApiAgentConsignmentDocumentDto Document,
        string Message);
}
