namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCustomOptionListResponse
    {
        public string OptionType { get; init; } = string.Empty;

        public IReadOnlyList<string> PredefinedOptions { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> CustomOptions { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

        public bool AllowCustomValues { get; init; }

        public string StoragePolicy { get; init; } = string.Empty;
    }

    public sealed class ApiCustomOptionSaveRequest
    {
        public string Value { get; init; } = string.Empty;
    }
}
