namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCustomsCooDocumentDto
    {
        public int Id { get; set; }
        public int SourceInvoiceId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public string ContractNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CertNo { get; set; } = string.Empty;
        public string ApplyType { get; set; } = string.Empty;
        public string CertStatus { get; set; } = string.Empty;
        public string CertType { get; set; } = string.Empty;
        public string EntMgrNo { get; set; } = string.Empty;
        public string CiqRegNo { get; set; } = string.Empty;
        public string AplRegNo { get; set; } = string.Empty;
        public string EtpsName { get; set; } = string.Empty;
        public string ApplName { get; set; } = string.Empty;
        public string Applicant { get; set; } = string.Empty;
        public string ApplTel { get; set; } = string.Empty;
        public string OrgCode { get; set; } = string.Empty;
        public string FetchPlace { get; set; } = string.Empty;
        public string AplAdd { get; set; } = string.Empty;
        public string InvDate { get; set; } = string.Empty;
        public string InvNo { get; set; } = string.Empty;
        public string AplDate { get; set; } = string.Empty;
        public string DestCountry { get; set; } = string.Empty;
        public string DestCountryCode { get; set; } = string.Empty;
        public string DestCountryName { get; set; } = string.Empty;
        public string Exporter { get; set; } = string.Empty;
        public string Consignee { get; set; } = string.Empty;
        public string GoodsSpecClause { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string LoadPort { get; set; } = string.Empty;
        public string UnloadPort { get; set; } = string.Empty;
        public string TransMeans { get; set; } = string.Empty;
        public string TransName { get; set; } = string.Empty;
        public string TransCountryCode { get; set; } = string.Empty;
        public string TransCountryName { get; set; } = string.Empty;
        public string TransPort { get; set; } = string.Empty;
        public string DestPort { get; set; } = string.Empty;
        public string TransDetails { get; set; } = string.Empty;
        public string IntendExpDate { get; set; } = string.Empty;
        public string TradeModeCode { get; set; } = string.Empty;
        public string FobValue { get; set; } = string.Empty;
        public string TotalAmt { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string LcNo { get; set; } = string.Empty;
        public string SpecInvTerms { get; set; } = string.Empty;
        public string PriceTerms { get; set; } = string.Empty;
        public string Curr { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string ExhibitFlag { get; set; } = string.Empty;
        public string ThirdPartyInvFlag { get; set; } = string.Empty;
        public string ExporterTel { get; set; } = string.Empty;
        public string ExporterFax { get; set; } = string.Empty;
        public string ExporterEmail { get; set; } = string.Empty;
        public string ConsigneeTel { get; set; } = string.Empty;
        public string ConsigneeFax { get; set; } = string.Empty;
        public string ConsigneeEmail { get; set; } = string.Empty;
        public string PredictFlag { get; set; } = string.Empty;
        public string ExpDeclDate { get; set; } = string.Empty;
        public string OriCountryCode { get; set; } = string.Empty;
        public string OriCountry { get; set; } = string.Empty;
        public string ChkValidDate { get; set; } = string.Empty;
        public string EtpsConcEr { get; set; } = string.Empty;
        public string EtpsTel { get; set; } = string.Empty;
        public string EntryId { get; set; } = string.Empty;
        public string PrcsAssembly { get; set; } = string.Empty;
        public string OldCertNo { get; set; } = string.Empty;
        public string ModReason { get; set; } = string.Empty;
        public string ModColm { get; set; } = string.Empty;
        public string OldSituDesc { get; set; } = string.Empty;
        public string ModSituDesc { get; set; } = string.Empty;
        public string OldDeclDate { get; set; } = string.Empty;
        public string OldIssueDate { get; set; } = string.Empty;
        public string AplPromiseCode { get; set; } = string.Empty;
        public int WarningCount { get; set; }
        public string WarningSummary { get; set; } = string.Empty;
        public int DraftRevision { get; set; }
        public DateTime LastGeneratedAt { get; set; } = DateTime.MinValue;
        public int SourceDiffCount { get; set; }
        public string SourceDiffSummary { get; set; } = string.Empty;
        public int ManualLockedFieldCount { get; set; }
        public IReadOnlyList<ApiCustomsCooItemDto> Items { get; set; } = Array.Empty<ApiCustomsCooItemDto>();
        public IReadOnlyList<ApiCustomsCooNonpartyCorpDto> NonpartyCorps { get; set; } = Array.Empty<ApiCustomsCooNonpartyCorpDto>();
        public IReadOnlyList<ApiCustomsCooAttachmentDto> Attachments { get; set; } = Array.Empty<ApiCustomsCooAttachmentDto>();
    }

    public sealed record ApiCustomsCooDocumentSaveResponse(
        bool Success,
        int Id,
        ApiCustomsCooDocumentDto Document,
        string Message);
}
