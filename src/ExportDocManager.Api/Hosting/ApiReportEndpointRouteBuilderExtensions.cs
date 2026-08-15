using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapReportEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentReports)
                .MapReportTemplateEndpoints();
            endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentReports)
                .MapUserReportTemplateEndpoints();
            var invoiceReports = endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentInvoiceReports);
            invoiceReports.MapInvoiceReportHtmlPreviewEndpoints();
            invoiceReports.MapInvoiceDocumentPackageHtmlPreviewEndpoints();
            invoiceReports.MapInvoiceReportPdfEndpoint();
            invoiceReports.MapInvoiceReportPdfZipEndpoint();
            invoiceReports.MapInvoiceDocumentPackageEndpoint();
            invoiceReports.MapInvoiceDocumentEmailEndpoint();
            var paymentReports = endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentPaymentReports);
            paymentReports.MapPaymentDraftReportHtmlPreviewEndpoints();
            paymentReports.MapPaymentReportHtmlPreviewEndpoints();
            paymentReports.MapPaymentReportPdfEndpoint();
        }
    }
}
