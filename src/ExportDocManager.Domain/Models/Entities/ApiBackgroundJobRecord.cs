namespace ExportDocManager.Models.Entities
{
    public sealed class ApiBackgroundJobRecord
    {
        public string JobId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? ProgressPercent { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public int RequestedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
        public bool CanRetry { get; set; }
        public string RetryOperation { get; set; } = string.Empty;
        public string RetryRequestJson { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
