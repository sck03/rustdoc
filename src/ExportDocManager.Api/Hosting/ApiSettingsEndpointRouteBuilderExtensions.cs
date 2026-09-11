using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/settings", async Task<Results<
                Ok<ApiSettingsResponse>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISettingsService settingsService) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                await settingsService.LoadAsync(context.RequestAborted);
                return TypedResults.Ok(ApiSettingsDtoFactory.FromSettingsForUser(
                    settingsService.Settings,
                    authorizationService.CanManageSettings(user),
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithName("GetSettings")
            // Every authenticated client needs the sanitized runtime settings
            // projection. The handler only reveals secrets and local paths to
            // administrators, so this is an intentional authenticated bypass,
            // not a missing permission declaration.
            .AllowApiWithoutPermission();

            endpoints.MapPost("/api/settings/validate", async Task<Results<
                Ok<ApiSettingsValidationResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>> (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ApiSettingsValidationRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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

                await settingsService.LoadAsync(context.RequestAborted);
                return TypedResults.Ok(ApiSettingsDtoFactory.ValidateDraft(
                    request.Settings,
                    settingsService.Settings,
                    request.UpdateSecrets,
                    revealLocalPaths: false));
            })
            .WithName("ValidateSettings")
            .WithApiCapability(PermissionResourceCatalog.SystemSettings, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/settings", async Task<Results<
                Ok<ApiSettingsSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>> (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISettingsService settingsService,
                ApiSettingsSaveRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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

                await settingsService.LoadAsync(context.RequestAborted);
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

                bool requiresRestart = false;
                await settingsService.UpdateAsync(current =>
                {
                    if (current.Revision != request.Settings.Revision)
                        throw new ServiceConcurrencyException("系统设置已被其他操作更新，请重新加载最新设置后再保存。");
                    var prepared = ApiSettingsDtoFactory.PrepareForSave(request.Settings, current, request.UpdateSecrets);
                    requiresRestart = ApiSettingsDtoFactory.RequiresRestartForSystemSettingsChange(current.System, prepared.System);
                    ApiSettingsDtoFactory.CopyInto(current, prepared);
                    return true;
                }, context.RequestAborted);

                return TypedResults.Ok(ApiSettingsDtoFactory.FromSavedSettings(
                    settingsService.Settings,
                    requiresRestart,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions),
                    requiresRestart
                        ? "设置已保存，数据库连接变更需要重启 sidecar 后生效。"
                        : "设置已保存。"));
            })
            .WithName("UpdateSettings")
            .WithApiCapability(PermissionResourceCatalog.SystemSettings, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}
