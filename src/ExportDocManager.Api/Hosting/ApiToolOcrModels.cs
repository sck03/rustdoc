namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiOcrRecognizeImageRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public sealed class ApiOcrRecognizeImageContentRequest
    {
        public string ImageContentBase64 { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public string SourceMimeType { get; set; } = string.Empty;
    }

    public sealed class ApiOcrRecognizeImageResponse
    {
        public string SourcePath { get; set; } = string.Empty;

        public string FullText { get; set; } = string.Empty;

        public IReadOnlyList<ApiOcrLineDto> Lines { get; set; } = Array.Empty<ApiOcrLineDto>();

        public string StoragePolicy { get; set; } = string.Empty;
    }

    public sealed class ApiOcrLineDto
    {
        public string Text { get; set; } = string.Empty;

        public int X { get; set; }

        public int Y { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }
}
