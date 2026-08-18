using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/system/license", async Task<Results<
                Ok<ApiLicenseStatusResponse>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                ILicenseService licenseService,
                ApiDesktopAccessOptions desktopAccessOptions,
                CancellationToken cancellationToken) =>
            {

                var status = await licenseService.GetStatusAsync(cancellationToken);
                return TypedResults.Ok(ApiLicenseDtoFactory.FromStatus(
                    status,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithApiAccess(true, true, false)
            .WithName("GetLicenseStatus");

            endpoints.MapPost("/api/system/license/register", async Task<Results<
                Ok<ApiLicenseRegisterResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>> (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ILicenseService licenseService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiLicenseRegisterRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return TypedResults.Json(
                        new ApiErrorResponse("只有管理员可以注册或更换系统授权。"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request is null || string.IsNullOrWhiteSpace(request.LicenseKey))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("注册码不能为空。"));
                }

                var result = await licenseService.RegisterAsync(request.LicenseKey, cancellationToken);
                if (!result.Success)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(result.Message));
                }

                return TypedResults.Ok(ApiLicenseDtoFactory.FromResult(
                    result,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithApiAccess(true, true, false)
            .WithName("RegisterLicense")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);
        }
    }
}
