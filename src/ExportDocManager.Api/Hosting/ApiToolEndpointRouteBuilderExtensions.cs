using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var reports = endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentReports);
            MapPdfToolEndpoints(reports);
            MapLetterOfCreditToolEndpoints(reports);
            MapOcrToolEndpoints(endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentOcr));
            MapExchangeRateToolEndpoints(endpoints.MapPermissionGroup(PermissionModuleCatalog.CommonExchangeRates));
            MapEmailToolEndpoints(endpoints.MapPermissionGroup(PermissionModuleCatalog.CommonEmail));
            var containerPacking = endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentContainerPacking);
            MapContainerPackingToolEndpoints(containerPacking);
            MapContainerPackingPdfEndpoints(containerPacking);
            MapExcelToolEndpoints(endpoints.MapPermissionGroup(PermissionModuleCatalog.DocumentExcel));
        }
    }
}
