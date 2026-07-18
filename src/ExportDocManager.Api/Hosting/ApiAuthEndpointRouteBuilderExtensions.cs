using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/auth/login", async (
                HttpContext context,
                ApiLoginRequest request,
                IDatabaseInitializationService databaseInitializationService,
                IUserService userService,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService) =>
            {
                if (string.IsNullOrWhiteSpace(request?.Username))
                {
                    return Results.BadRequest(new ApiErrorResponse("用户名不能为空。"));
                }

                string username = request.Username.Trim();
                string password = request.Password ?? string.Empty;
                var initializationResult = await databaseInitializationService.InitializeAsync(username, password);
                if (!initializationResult.IsSuccess)
                {
                    return Results.Json(
                        new ApiErrorResponse(initializationResult.ErrorMessage),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var user = await userService.AuthenticateAsync(username, password);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var token = await tokenService.IssueAsync(user, cancellationToken: context.RequestAborted);
                return Results.Ok(new ApiLoginResponse(
                    "Bearer",
                    token.AccessToken,
                    token.ExpiresAt,
                    ApiUserDtoFactory.FromUser(user, authorizationService)));
            })
            .WithName("Login");

            endpoints.MapGet("/api/auth/me", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                return user == null
                    ? Results.Unauthorized()
                    : Results.Ok(ApiUserDtoFactory.FromUser(user, authorizationService));
            })
            .WithName("CurrentUser");

            endpoints.MapPost("/api/auth/logout", async (HttpContext context, IApiSessionTokenService tokenService) =>
            {
                bool revoked = await tokenService.RevokeAsync(
                    ApiCurrentUserContext.GetBearerToken(context),
                    context.RequestAborted);
                return Results.Ok(new ApiLogoutResponse(revoked));
            })
            .WithName("Logout");
        }
    }
}
