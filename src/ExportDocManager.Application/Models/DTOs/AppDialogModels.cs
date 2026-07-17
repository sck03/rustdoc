namespace ExportDocManager.Models.DTOs
{
    public sealed class EmailSendRequest
    {
        public string ToAddress { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public IReadOnlyList<string> AttachmentPaths { get; set; } = Array.Empty<string>();
    }

    public sealed class EmailSendResult
    {
        public string ToAddress { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }
}
