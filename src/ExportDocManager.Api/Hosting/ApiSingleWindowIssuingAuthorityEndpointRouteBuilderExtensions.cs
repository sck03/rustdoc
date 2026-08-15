using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowIssuingAuthorityEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/coo/issuing-authorities", Results<
                Ok<ApiSingleWindowIssuingAuthorityCatalogResponse>,
                UnauthorizedHttpResult> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowReferenceCatalogSnapshotProvider catalogProvider) =>
            {

                return TypedResults.Ok(ApiSingleWindowDtoFactory.FromIssuingAuthorityCatalog(
                    catalogProvider.Current.IssuingAuthorities));
            })
            .WithName("GetCustomsCooIssuingAuthorities");

            endpoints.MapGet("/api/single-window/coo/editor-options", Results<
                Ok<ApiCustomsCooEditorOptionsResponse>,
                UnauthorizedHttpResult> (
                HttpContext context,
                IApiSessionTokenService tokenService) =>
            {

                return TypedResults.Ok(ApiSingleWindowDtoFactory.FromCustomsCooEditorOptions());
            })
            .WithName("GetCustomsCooEditorOptions");
        }
    }
}
