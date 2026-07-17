namespace ExportDocManager.Services.Infrastructure
{
    public sealed class FileDialogOptions
    {
        public string Filter { get; init; } = "All Files (*.*)|*.*";

        public string Title { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string InitialDirectory { get; init; } = string.Empty;

        public string PreferenceKey { get; init; } = string.Empty;

        public string DefaultExt { get; init; } = string.Empty;

        public bool AddExtension { get; init; }

        public string Description { get; init; } = string.Empty;
    }
}
