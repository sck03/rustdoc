using ExportDocManager.Services.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/settings", async Task<Results<
                Ok<ApiSettingsResponse>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISettingsService settingsService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return TypedResults.Unauthorized();
                }

                await settingsService.LoadAsync();
                return TypedResults.Ok(ApiSettingsDtoFactory.FromSettingsForUser(
                    settingsService.Settings,
                    authorizationService.CanManageSettings(user),
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithName("GetSettings");

            endpoints.MapPost("/api/settings/validate", async Task<Results<
                Ok<ApiSettingsValidationResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ApiSettingsValidationRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return TypedResults.Json(
                        new ApiErrorResponse("只有管理员可以校验系统设置。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request is null || request.Settings == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("设置校验请求体不能为空。"));
                }

                await settingsService.LoadAsync();
                return TypedResults.Ok(ApiSettingsDtoFactory.ValidateDraft(
                    request.Settings,
                    settingsService.Settings,
                    request.UpdateSecrets,
                    revealLocalPaths: false));
            })
            .WithName("ValidateSettings")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/settings", async Task<Results<
                Ok<ApiSettingsSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISettingsService settingsService,
                ApiSettingsSaveRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return TypedResults.Json(
                        new ApiErrorResponse("只有管理员可以保存系统设置。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request is null || request.Settings == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("设置请求体不能为空。"));
                }

                await settingsService.LoadAsync();
                var validation = ApiSettingsDtoFactory.ValidateDraft(
                    request.Settings,
                    settingsService.Settings,
                    request.UpdateSecrets,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions));
                if (!validation.IsValid)
                {
                    string errors = string.Join(
                        "；",
                        validation.Messages
                            .Where(message => string.Equals(message.Level, "error", StringComparison.OrdinalIgnoreCase))
                            .Select(message => message.Message));
                    return TypedResults.BadRequest(new ApiErrorResponse(
                        string.IsNullOrWhiteSpace(errors)
                            ? "设置包含无效内容，请先运行设置校验。"
                            : errors));
                }

                var prepared = ApiSettingsDtoFactory.PrepareForSave(
                    request.Settings,
                    settingsService.Settings,
                    request.UpdateSecrets);
                bool requiresRestart = ApiSettingsDtoFactory.RequiresRestartForSystemSettingsChange(
                    settingsService.Settings.System,
                    prepared.System);

                await settingsService.UpdateAsync(current =>
                {
                    ApiSettingsDtoFactory.CopyInto(current, prepared);
                    return true;
                });

                return TypedResults.Ok(ApiSettingsDtoFactory.FromSavedSettings(
                    settingsService.Settings,
                    requiresRestart,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions),
                    requiresRestart
                        ? "设置已保存，数据库连接变更需要重启 sidecar 后生效。"
                        : "设置已保存。"));
            })
            .WithName("UpdateSettings")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}
