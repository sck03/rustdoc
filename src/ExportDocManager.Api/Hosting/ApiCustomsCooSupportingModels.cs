namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCustomsCooNonpartyCorpDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int SortNo { get; set; }
        public string EntName { get; set; } = string.Empty;
        public string EntAddr { get; set; } = string.Empty;
        public string EntCountryCode { get; set; } = string.Empty;
        public string EntCountryName { get; set; } = string.Empty;
    }

    public sealed class ApiCustomsCooAttachmentDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string CertNo { get; set; } = string.Empty;
        public string CertType { get; set; } = string.Empty;
        public string AplRegNo { get; set; } = string.Empty;
        public string CiqRegNo { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DocType { get; set; } = string.Empty;
        public bool IsDelay { get; set; }
        public bool FileExistsAtBuild { get; set; }
        public int SortOrder { get; set; }
    }
}
