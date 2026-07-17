namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapReportEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapReportTemplateEndpoints();
            endpoints.MapUserReportTemplateEndpoints();
            endpoints.MapInvoiceReportHtmlPreviewEndpoints();
            endpoints.MapInvoiceDocumentPackageHtmlPreviewEndpoints();
            endpoints.MapPaymentDraftReportHtmlPreviewEndpoints();
            endpoints.MapPaymentReportHtmlPreviewEndpoints();
            endpoints.MapInvoiceReportPdfEndpoint();
            endpoints.MapPaymentReportPdfEndpoint();
            endpoints.MapInvoiceReportPdfZipEndpoint();
            endpoints.MapInvoiceDocumentPackageEndpoint();
            endpoints.MapInvoiceDocumentEmailEndpoint();
        }
    }
}
