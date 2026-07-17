namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiEmailStatusResponse
    {
        public bool IsConfigured { get; init; }

        public string SmtpHost { get; init; } = string.Empty;

        public int SmtpPort { get; init; }

        public bool EnableSsl { get; init; }

        public string FromAddress { get; init; } = string.Empty;

        public string FromDisplayName { get; init; } = string.Empty;

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ApiEmailServerSuggestionRequest
    {
        public string EmailAddress { get; init; } = string.Empty;
    }

    public sealed class ApiEmailServerSuggestionResponse
    {
        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;

        public string EmailAddress { get; init; } = string.Empty;

        public string SmtpHost { get; init; } = string.Empty;

        public int SmtpPort { get; init; }

        public bool EnableSsl { get; init; }

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ApiEmailSendRequest
    {
        public string ToAddress { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public string Body { get; init; } = string.Empty;

        public IReadOnlyList<string> AttachmentPaths { get; init; } = Array.Empty<string>();
    }

    public sealed class ApiEmailSendResponse
    {
        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;

        public string ToAddress { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public int AttachmentCount { get; init; }

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ApiEmailTestResponse
    {
        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;

        public string FromAddress { get; init; } = string.Empty;

        public string SmtpHost { get; init; } = string.Empty;

        public string StoragePolicy { get; init; } = string.Empty;
    }
}
