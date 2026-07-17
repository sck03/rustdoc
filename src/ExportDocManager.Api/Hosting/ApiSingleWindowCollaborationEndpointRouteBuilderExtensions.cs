using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowCollaborationEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/collaboration", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowCollaborationDataSource collaborationDataSource,
                string businessType,
                string status,
                string keyword,
                int? pageNumber,
                int? pageSize,
                bool? includeDisabledWorkstations,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await collaborationDataSource.QueryPageAsync(
                    new SingleWindowCollaborationPageQuery
                    {
                        BusinessType = businessType ?? string.Empty,
                        Status = status ?? string.Empty,
                        Keyword = keyword ?? string.Empty,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50,
                        IncludeDisabledWorkstations = includeDisabledWorkstations ?? false
                    },
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListSingleWindowCollaboration");

            endpoints.MapGet("/api/single-window/collaboration/workstations", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowCollaborationDataSource collaborationDataSource,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await collaborationDataSource.QueryWorkstationsAsync(cancellationToken);
                return Results.Ok(result);
            })
            .WithName("ListSingleWindowWorkstations");
        }
    }
}
