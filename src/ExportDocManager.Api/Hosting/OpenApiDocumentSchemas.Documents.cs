namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateDocumentsSchemas()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateReportDocumentSchemas());
            AddOpenApiEntries(result, CreateInvoicePaymentDocumentSchemas());
            AddOpenApiEntries(result, CreateHsCodeDocumentSchemas());
            return result;
        }
    }
}
