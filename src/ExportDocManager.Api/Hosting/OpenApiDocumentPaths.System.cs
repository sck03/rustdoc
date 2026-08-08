namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSystemPaths()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateRuntimeAccessSystemPaths());
            AddOpenApiEntries(result, CreateBackupMigrationSystemPaths());
            AddOpenApiEntries(result, CreateJobsAuditSystemPaths());
            return result;
        }
    }
}
