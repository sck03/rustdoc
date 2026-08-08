namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateDocumentsPaths()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateReportDocumentPaths());
            AddOpenApiEntries(result, CreateInvoiceDocumentPaths());
            AddOpenApiEntries(result, CreatePaymentDocumentPaths());
            return result;
        }
    }
}
