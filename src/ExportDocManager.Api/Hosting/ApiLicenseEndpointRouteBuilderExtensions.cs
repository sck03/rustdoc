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
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var status = await licenseService.GetStatusAsync(cancellationToken);
                return Results.Ok(ApiLicenseDtoFactory.FromStatus(status));
            })
            .WithName("GetLicenseStatus");

            endpoints.MapPost("/api/system/license/register", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ILicenseService licenseService,
                ApiLicenseRegisterRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null || string.IsNullOrWhiteSpace(request.LicenseKey))
                {
                    return Results.BadRequest(new ApiErrorResponse("注册码不能为空。"));
                }

                var result = await licenseService.RegisterAsync(request.LicenseKey, cancellationToken);
                if (!result.Success)
                {
                    return Results.BadRequest(ApiLicenseDtoFactory.FromResult(result));
                }

                return Results.Ok(ApiLicenseDtoFactory.FromResult(result));
            })
            .WithName("RegisterLicense");
        }
    }
}
