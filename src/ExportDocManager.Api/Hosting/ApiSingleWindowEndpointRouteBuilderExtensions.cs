using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowEndpoints(this IEndpointRouteBuilder endpoints)
        {
            MapSingleWindowReferenceCatalogEndpoints(endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentDeclarationDictionary));
            var singleWindow = endpoints.MapPermissionGroup(
                PermissionModuleCatalog.DocumentSingleWindow);
            MapSingleWindowIssuingAuthorityEndpoints(singleWindow);
            MapSingleWindowProducerProfileEndpoints(singleWindow);
            MapSingleWindowDocumentEndpoints(singleWindow);
            MapSingleWindowPackageEndpoints(singleWindow);
            MapSingleWindowClientEndpoints(singleWindow);
            MapSingleWindowExportReviewEndpoints(singleWindow);
            MapSingleWindowOperationCenterEndpoints(singleWindow);
        }
    }
}
