namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiLetterOfCreditImportRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public sealed class ApiLetterOfCreditImportResponse
    {
        public ApiLetterOfCreditImportResponse(
            string sourcePath,
            string sourceDescription,
            string extractedText,
            string storagePolicy)
        {
            SourcePath = sourcePath ?? string.Empty;
            SourceDescription = sourceDescription ?? string.Empty;
            ExtractedText = extractedText ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public string SourcePath { get; }

        public string SourceDescription { get; }

        public string ExtractedText { get; }

        public string StoragePolicy { get; }
    }

    public sealed record ApiLetterOfCreditReviewRequest(
        ApiInvoiceDetailDto Invoice);

    public sealed record ApiLetterOfCreditReviewResponse(
        string ReportText,
        string ContextSummary,
        bool LetterOfCreditContentTruncated,
        string StoragePolicy);
}
