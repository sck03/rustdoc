namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportPdfRenderRequest
    {
        public int SourceId { get; init; }

        public ReportDocumentType ReportType { get; init; }

        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSeal { get; init; } = true;

        public string DestinationPath { get; init; } = string.Empty;

        public string DocumentTitle { get; init; } = string.Empty;
    }

    public sealed class ReportPdfRenderResult
    {
        public int SourceId { get; init; }

        public ReportDocumentType ReportType { get; init; }

        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSeal { get; init; }

        public string DestinationPath { get; init; } = string.Empty;

        public string RendererKind { get; init; } = string.Empty;

        public string RendererPath { get; init; } = string.Empty;
    }

    public interface IReportPdfRenderService
    {
        Task<ReportPdfRenderResult> RenderInvoicePdfAsync(
            ReportPdfRenderRequest request,
            CancellationToken cancellationToken = default);

        Task<ReportPdfRenderResult> RenderPaymentVoucherPdfAsync(
            ReportPdfRenderRequest request,
            CancellationToken cancellationToken = default);
    }
}
