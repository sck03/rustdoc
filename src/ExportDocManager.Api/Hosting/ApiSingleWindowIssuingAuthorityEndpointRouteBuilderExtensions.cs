using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowIssuingAuthorityEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/coo/issuing-authorities", (
                HttpContext context,
                IApiSessionTokenService tokenService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(ApiSingleWindowDtoFactory.FromIssuingAuthorityCatalog());
            })
            .WithName("GetCustomsCooIssuingAuthorities");

            endpoints.MapGet("/api/single-window/coo/editor-options", (
                HttpContext context,
                IApiSessionTokenService tokenService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(ApiSingleWindowDtoFactory.FromCustomsCooEditorOptions());
            })
            .WithName("GetCustomsCooEditorOptions");
        }
    }
}
