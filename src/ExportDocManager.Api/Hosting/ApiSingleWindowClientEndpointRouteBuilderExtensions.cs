using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
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
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机客户端档案仅允许受控 Tauri 操作机访问。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteConflict("持卡操作机仅支持独立 SQLite 单机版。");
                }

                var profiles = await profileService.ListAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromClientProfiles(profiles));
            })
            .WithName("GetSingleWindowClientProfiles");

            endpoints.MapPut("/api/single-window/client-profiles", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientProfileService profileService,
                ApiSingleWindowClientProfileSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机客户端档案仅允许受控 Tauri 操作机修改。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteConflict("持卡操作机仅支持独立 SQLite 单机版。");
                }

                return await SaveSingleWindowClientProfileAsync(
                    profileService,
                    request,
                    cancellationToken);
            })
            .WithName("SaveSingleWindowClientProfile");

            endpoints.MapPost("/api/single-window/client-profiles/{profileKey}/activate", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientProfileService profileService,
                string profileKey,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("单一窗口本机操作档案仅允许受控 Tauri 操作机切换。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteConflict("持卡操作机仅支持独立 SQLite 单机版。");
                }

                return await ActivateSingleWindowClientProfileAsync(
                    profileService,
                    profileKey,
                    cancellationToken);
            })
            .WithName("ActivateSingleWindowClientProfile");

            endpoints.MapPost("/api/single-window/client/dispatch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientBridge clientBridge,
                ApiSingleWindowClientDispatchRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("发送官方客户端目录只允许受控 Tauri 操作机执行。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteConflict("官方单一窗口客户端只能由独立 SQLite 持卡机操作。");
                }

                return await DispatchSingleWindowBatchToClientAsync(
                    clientBridge,
                    request,
                    cancellationToken);
            })
            .WithName("DispatchSingleWindowBatchToClient");

            endpoints.MapPost("/api/single-window/client/collect-receipts", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                DatabaseConnectionSettings databaseSettings,
                ISingleWindowClientBridge clientBridge,
                ApiSingleWindowReceiptCollectionRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("收集官方客户端回执只允许受控 Tauri 操作机执行。");
                }

                if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
                {
                    return WriteConflict("官方单一窗口客户端只能由独立 SQLite 持卡机操作。");
                }

                return await CollectSingleWindowReceiptFilesAsync(
                    clientBridge,
                    request,
                    cancellationToken);
            })
            .WithName("CollectSingleWindowClientReceipts");
        }
    }
}
