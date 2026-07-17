namespace ExportDocManager.Services.Reporting
{
    public sealed class HtmlToPdfRenderOptions
    {
        public string DocumentTitle { get; init; } = string.Empty;

        public string BaseDirectory { get; init; } = string.Empty;
    }

    public sealed class HtmlToPdfRenderResult
    {
        public string DestinationPath { get; init; } = string.Empty;

        public string RendererPath { get; init; } = string.Empty;
    }

    public interface IHtmlToPdfService
    {
        Task<HtmlToPdfRenderResult> RenderAsync(
            string html,
            string destinationPath,
            HtmlToPdfRenderOptions options = null,
            CancellationToken cancellationToken = default);
    }
}
