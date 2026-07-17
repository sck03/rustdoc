namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiAuditLogDto
    {
        public int Id { get; init; }
        public string EntityName { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string EntityId { get; init; } = string.Empty;
        public string OldValues { get; init; } = string.Empty;
        public string NewValues { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string OldValuesPreview { get; init; } = string.Empty;
        public string NewValuesPreview { get; init; } = string.Empty;
    }

    public class ApiAuditLogFilterRequest
    {
        public string InvoiceKeyword { get; init; } = string.Empty;
        public string EntityName { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public DateTime? StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public string Keyword { get; init; } = string.Empty;
        public int MaxCount { get; init; } = 50000;
    }

    public sealed class ApiAuditLogPathExportRequest : ApiAuditLogFilterRequest
    {
        public string DestinationPath { get; init; } = string.Empty;
    }

    public sealed class ApiAuditLogDeleteRequest : ApiAuditLogFilterRequest
    {
        public bool Confirmed { get; init; }
    }

    public sealed class ApiAuditLogCleanupRequest
    {
        public int DaysToKeep { get; init; } = 180;
        public int MaxCount { get; init; } = 200000;
        public bool Confirmed { get; init; }
    }

    public sealed record ApiAuditLogCommandResponse(
        bool Success,
        string Message,
        int AffectedCount,
        string DestinationPath,
        string StoragePolicy);
}
