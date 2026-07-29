namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowEndpoints(this IEndpointRouteBuilder endpoints)
        {
            MapSingleWindowReferenceCatalogEndpoints(endpoints);
            MapSingleWindowIssuingAuthorityEndpoints(endpoints);
            MapSingleWindowProducerProfileEndpoints(endpoints);
            MapSingleWindowDocumentEndpoints(endpoints);
            MapSingleWindowPackageEndpoints(endpoints);
            MapSingleWindowClientEndpoints(endpoints);
            MapSingleWindowExportReviewEndpoints(endpoints);
            MapSingleWindowOperationCenterEndpoints(endpoints);
        }
    }
}
