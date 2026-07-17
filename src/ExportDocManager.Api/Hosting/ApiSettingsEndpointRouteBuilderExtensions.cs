using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/settings", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                await settingsService.LoadAsync();
                return Results.Ok(ApiSettingsDtoFactory.FromSettings(settingsService.Settings));
            })
            .WithName("GetSettings");

            endpoints.MapPost("/api/settings/validate", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ApiSettingsValidationRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return Results.Json(
                        new ApiErrorResponse("只有管理员可以校验系统设置。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request?.Settings == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("设置校验请求体不能为空。"));
                }

                await settingsService.LoadAsync();
                return Results.Ok(ApiSettingsDtoFactory.ValidateDraft(
                    request.Settings,
                    settingsService.Settings,
                    request.UpdateSecrets));
            })
            .WithName("ValidateSettings");

            endpoints.MapPut("/api/settings", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ApiSettingsSaveRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return Results.Json(
                        new ApiErrorResponse("只有管理员可以保存系统设置。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request?.Settings == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("设置请求体不能为空。"));
                }

                try
                {
                    await settingsService.LoadAsync();
                    bool requiresRestart = ApiSettingsDtoFactory.RequiresRestartForSystemSettingsChange(
                        settingsService.Settings.System,
                        request.Settings.System);
                    var prepared = ApiSettingsDtoFactory.PrepareForSave(
                        request.Settings,
                        settingsService.Settings,
                        request.UpdateSecrets);

                    await settingsService.UpdateAsync(current =>
                    {
                        ApiSettingsDtoFactory.CopyInto(current, prepared);
                        return true;
                    });

                    return Results.Ok(ApiSettingsDtoFactory.FromSavedSettings(
                        settingsService.Settings,
                        requiresRestart,
                        requiresRestart
                            ? "设置已保存，数据库连接变更需要重启 sidecar 后生效。"
                            : "设置已保存。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateSettings");
        }
    }
}
