namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSchemas()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateSystemSchemas());
            AddOpenApiEntries(result, CreateToolsSchemas());
            AddOpenApiEntries(result, CreateDocumentsSchemas());
            AddOpenApiEntries(result, CreateSingleWindowSchemas());
            AddOpenApiEntries(result, CreateSalesSchemas());
            AddOpenApiEntries(result, CreateMasterDataSchemas());
            AddOpenApiEntries(result, CreateCommonSchemas());
            return result;
        }
    }
}