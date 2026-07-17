namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreatePaths()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateSystemPaths());
            AddOpenApiEntries(result, CreateToolsPaths());
            AddOpenApiEntries(result, CreateDocumentsPaths());
            AddOpenApiEntries(result, CreateSingleWindowPaths());
            AddOpenApiEntries(result, CreateSalesPaths());
            AddOpenApiEntries(result, CreateMasterDataPaths());
            AddOpenApiEntries(result, CreateOtherPaths());
            return result;
        }
    }
}