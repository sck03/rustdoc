using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowClientEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/client-profile/default", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowClientProfileService profileService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var profile = await profileService.GetDefaultAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromClientProfile(profile));
            })
            .WithName("GetSingleWindowDefaultClientProfile");

            endpoints.MapPut("/api/single-window/client-profile/default", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowClientProfileService profileService,
                ApiSingleWindowClientProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await SaveSingleWindowClientProfileAsync(
                    profileService,
                    request,
                    cancellationToken);
            })
            .WithName("SaveSingleWindowDefaultClientProfile");

            endpoints.MapPost("/api/single-window/client/dispatch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowClientBridge clientBridge,
                ISingleWindowClientProfileService profileService,
                ISingleWindowOperationCenterService operationCenterService,
                ApiSingleWindowClientDispatchRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await DispatchSingleWindowBatchToClientAsync(
                    clientBridge,
                    profileService,
                    operationCenterService,
                    request,
                    cancellationToken);
            })
            .WithName("DispatchSingleWindowBatchToClient");

            endpoints.MapPost("/api/single-window/client/collect-receipts", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISingleWindowClientBridge clientBridge,
                ISingleWindowClientProfileService profileService,
                ISingleWindowOperationCenterService operationCenterService,
                ApiSingleWindowReceiptCollectionRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                return await CollectSingleWindowReceiptFilesAsync(
                    clientBridge,
                    profileService,
                    operationCenterService,
                    request,
                    cancellationToken);
            })
            .WithName("CollectSingleWindowClientReceipts");
        }
    }
}
