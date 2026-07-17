namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCustomsCooProducerProfileDto
    {
        public int Id { get; set; }
        public string CiqRegNo { get; set; } = string.Empty;
        public string PrdcEtpsName { get; set; } = string.Empty;
        public string PrdcEtpsConcEr { get; set; } = string.Empty;
        public string PrdcEtpsTel { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerTel { get; set; } = string.Empty;
        public string ProducerFax { get; set; } = string.Empty;
        public string ProducerEmail { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string LastInvoiceNo { get; set; } = string.Empty;
        public string LastContractNo { get; set; } = string.Empty;
        public string LastSourceStyleNo { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }
    }

    public sealed record ApiCustomsCooProducerProfileResponse(
        ApiCustomsCooProducerProfileDto Profile,
        string StoragePolicy);

    public sealed record ApiCustomsCooProducerProfileListResponse(
        IReadOnlyList<ApiCustomsCooProducerProfileDto> Items,
        int TotalCount,
        string StoragePolicy);

    public sealed class ApiCustomsCooProducerProfileInputDto
    {
        public string CiqRegNo { get; set; } = string.Empty;
        public string PrdcEtpsName { get; set; } = string.Empty;
        public string PrdcEtpsConcEr { get; set; } = string.Empty;
        public string PrdcEtpsTel { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerTel { get; set; } = string.Empty;
        public string ProducerFax { get; set; } = string.Empty;
        public string ProducerEmail { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string LastInvoiceNo { get; set; } = string.Empty;
        public string LastContractNo { get; set; } = string.Empty;
        public string LastSourceStyleNo { get; set; } = string.Empty;
    }

    public sealed record ApiCustomsCooProducerProfileSaveRequest(
        ApiCustomsCooProducerProfileInputDto Profile);

    public sealed record ApiCustomsCooProducerProfileSaveResponse(
        bool Success,
        int Id,
        ApiCustomsCooProducerProfileDto Profile,
        string Message,
        string StoragePolicy);
}
