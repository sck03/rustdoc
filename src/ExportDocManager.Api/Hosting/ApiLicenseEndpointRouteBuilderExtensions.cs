using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/system/license", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ILicenseService licenseService,
                ApiDesktopAccessOptions desktopAccessOptions,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var status = await licenseService.GetStatusAsync(cancellationToken);
                return Results.Ok(ApiLicenseDtoFactory.FromStatus(
                    status,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithName("GetLicenseStatus");

            endpoints.MapPost("/api/system/license/register", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ILicenseService licenseService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiLicenseRegisterRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return Results.Json(
                        new ApiErrorResponse("只有管理员可以注册或更换系统授权。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request == null || string.IsNullOrWhiteSpace(request.LicenseKey))
                {
                    return Results.BadRequest(new ApiErrorResponse("注册码不能为空。"));
                }

                var result = await licenseService.RegisterAsync(request.LicenseKey, cancellationToken);
                if (!result.Success)
                {
                    return Results.BadRequest(ApiLicenseDtoFactory.FromResult(
                        result,
                        ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
                }

                return Results.Ok(ApiLicenseDtoFactory.FromResult(
                    result,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithName("RegisterLicense");
        }
    }
}
