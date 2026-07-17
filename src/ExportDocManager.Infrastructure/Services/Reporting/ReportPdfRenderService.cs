namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportPdfRenderService : IReportPdfRenderService
    {
        private const string HtmlChromiumRendererKind = "HTML+Chromium";

        private readonly IReportHtmlService _reportHtmlService;
        private readonly IHtmlToPdfService _htmlToPdfService;

        public ReportPdfRenderService(
            IReportHtmlService reportHtmlService,
            IHtmlToPdfService htmlToPdfService)
        {
            _reportHtmlService = reportHtmlService ?? throw new ArgumentNullException(nameof(reportHtmlService));
            _htmlToPdfService = htmlToPdfService ?? throw new ArgumentNullException(nameof(htmlToPdfService));
        }

        public async Task<ReportPdfRenderResult> RenderInvoicePdfAsync(
            ReportPdfRenderRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ReportType != ReportDocumentType.ExportDocument)
            {
                throw new ArgumentException("发票 PDF 生成仅支持 ExportDocument 报表类型。", nameof(request));
            }

            var htmlResult = await _reportHtmlService.RenderInvoiceReportAsync(
                    request.SourceId,
                    request.ReportType,
                    request.TemplatePath,
                    request.WithSeal,
                    cancellationToken)
                .ConfigureAwait(false);

            return await RenderPdfAsync(htmlResult, request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ReportPdfRenderResult> RenderPaymentVoucherPdfAsync(
            ReportPdfRenderRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ReportType != ReportDocumentType.PaymentVoucher)
            {
                throw new ArgumentException("付款/报销单 PDF 生成仅支持 PaymentVoucher 报表类型。", nameof(request));
            }

            var htmlResult = await _reportHtmlService.RenderPaymentVoucherAsync(
                    request.SourceId,
                    request.TemplatePath,
                    request.WithSeal,
                    cancellationToken)
                .ConfigureAwait(false);

            return await RenderPdfAsync(htmlResult, request, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ReportPdfRenderResult> RenderPdfAsync(
            ReportHtmlRenderResult htmlResult,
            ReportPdfRenderRequest request,
            CancellationToken cancellationToken)
        {
            string documentTitle = string.IsNullOrWhiteSpace(request.DocumentTitle)
                ? $"{htmlResult.ReportType}-{htmlResult.SourceId}"
                : request.DocumentTitle.Trim();

            var pdfResult = await _htmlToPdfService.RenderAsync(
                    htmlResult.Html,
                    request.DestinationPath,
                    new HtmlToPdfRenderOptions
                    {
                        DocumentTitle = documentTitle,
                        BaseDirectory = Path.GetDirectoryName(htmlResult.TemplatePath) ?? string.Empty
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return new ReportPdfRenderResult
            {
                SourceId = htmlResult.SourceId,
                ReportType = htmlResult.ReportType,
                TemplatePath = htmlResult.TemplatePath,
                WithSeal = htmlResult.WithSeal,
                DestinationPath = pdfResult.DestinationPath,
                RendererKind = HtmlChromiumRendererKind,
                RendererPath = pdfResult.RendererPath
            };
        }
    }
}
