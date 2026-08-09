namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowSchemas()
        {
            var schemas = new Dictionary<string, object>(StringComparer.Ordinal);
            AddOpenApiEntries(schemas, CreateSingleWindowDocumentSchemas());
            AddOpenApiEntries(schemas, CreateSingleWindowProfileSchemas());
            AddOpenApiEntries(schemas, CreateSingleWindowOperationCenterSchemas());
            return schemas;
        }
    }
}
