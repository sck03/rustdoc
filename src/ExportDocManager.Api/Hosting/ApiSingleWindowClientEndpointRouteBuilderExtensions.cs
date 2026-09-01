using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSingleWindowClientEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/client-profiles", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientProfileService profileService,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机客户端档案只能由受控操作机访问。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("持卡操作机仅支持独立 SQLite 单机版。");
                }

                var profiles = await profileService.ListAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromClientProfiles(profiles));
            })
            .WithName("GetSingleWindowClientProfiles")
            .Produces<ApiSingleWindowClientProfilesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/single-window/client-profiles", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientProfileService profileService,
                ApiSingleWindowClientProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机客户端档案只能由受控操作机修改。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("持卡操作机仅支持独立 SQLite 单机版。");
                }

                return await SaveSingleWindowClientProfileAsync(
                    profileService,
                    request,
                    cancellationToken);
            })
            .WithName("SaveSingleWindowClientProfile")
            .Produces<ApiSingleWindowClientProfilesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/client-profiles/{profileKey}/activate", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientProfileService profileService,
                string profileKey,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机操作档案只能由受控操作机切换。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("持卡操作机仅支持独立 SQLite 单机版。");
                }

                return await ActivateSingleWindowClientProfileAsync(
                    profileService,
                    profileKey,
                    cancellationToken);
            })
            .WithName("ActivateSingleWindowClientProfile")
            .Produces<ApiSingleWindowClientProfilesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/client/dispatch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientBridge clientBridge,
                ApiSingleWindowClientDispatchRequest request,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("写入本机交接目录只能由受控持卡机执行。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("交接 OutBox 和官方单一窗口客户端只能由独立 SQLite 持卡机操作。");
                }

                return await DispatchSingleWindowBatchToClientAsync(
                    clientBridge,
                    request,
                    cancellationToken);
            })
            .WithName("DispatchSingleWindowBatchToClient")
            .Produces<SingleWindowClientDispatchResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout);

            endpoints.MapPost("/api/single-window/client/collect-receipts", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientBridge clientBridge,
                ApiSingleWindowReceiptCollectionRequest request,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("收集官方客户端回执只能由受控操作机执行。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteValidation("本机回执目录和官方单一窗口客户端只能由独立 SQLite 持卡机操作。");
                }

                return await CollectSingleWindowReceiptFilesAsync(
                    clientBridge,
                    request,
                    cancellationToken);
            })
            .WithName("CollectSingleWindowClientReceipts")
            .Produces<SingleWindowReceiptCollectionResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout);
        }
    }
}
