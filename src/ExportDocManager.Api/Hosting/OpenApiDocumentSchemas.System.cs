namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSystemSchemas()
        {
            var result = new Dictionary<string, object>();
            AddOpenApiEntries(result, CreateAccessLicenseSystemSchemas());
            AddOpenApiEntries(result, CreateBackupMigrationSystemSchemas());
            AddOpenApiEntries(result, CreateJobsSettingsSystemSchemas());
            return result;
        }
    }
}
