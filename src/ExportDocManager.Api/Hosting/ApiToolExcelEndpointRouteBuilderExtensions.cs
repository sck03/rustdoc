namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExcelToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapExcelImportPreviewEndpoint();
            endpoints.MapExcelTemplateExportEndpoint();
            endpoints.MapExcelBookingSheetEndpoints();
        }
    }
}
