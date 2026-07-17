namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSingleWindowLockedFieldDto(
        string Key,
        string DisplayName,
        string CurrentValue,
        string SuggestedValue);

    public sealed record ApiSingleWindowLockedFieldsResponse(
        int Count,
        IReadOnlyList<ApiSingleWindowLockedFieldDto> Fields);

    public sealed class ApiSingleWindowUnlockFieldsRequest
    {
        public IReadOnlyList<string> FieldKeys { get; set; } = Array.Empty<string>();
    }

    public sealed record ApiCustomsCooUnlockFieldsResponse(
        bool Success,
        int ChangedCount,
        ApiCustomsCooDocumentDto Document,
        IReadOnlyList<ApiSingleWindowLockedFieldDto> LockedFields,
        string Message);

    public sealed record ApiAgentConsignmentUnlockFieldsResponse(
        bool Success,
        int ChangedCount,
        ApiAgentConsignmentDocumentDto Document,
        IReadOnlyList<ApiSingleWindowLockedFieldDto> LockedFields,
        string Message);
}
