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
                ApiAuthorizationService authorizationService,
                ApiLoginAttemptService loginAttempts,
                ILogger<ApiLoginAttemptService> logger) =>
            {
                if (string.IsNullOrWhiteSpace(request?.Username))
                {
                    return Results.BadRequest(new ApiErrorResponse("用户名不能为空。"));
                }

                string username = request.Username.Trim();
                string password = request.Password ?? string.Empty;
                string remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var attemptDecision = loginAttempts.Evaluate(username, remoteAddress);
                if (!attemptDecision.Allowed)
                {
                    SetRetryAfter(context, attemptDecision.RetryAfter);
                    return Results.Json(
                        new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                        statusCode: StatusCodes.Status429TooManyRequests);
                }

                string bootstrapToken = context.Request.Headers[ApiRuntimeOptions.BootstrapTokenHeaderName].ToString();
                var initializationResult = await databaseInitializationService.InitializeAsync(
                    username,
                    password,
                    bootstrapToken);
                if (!initializationResult.IsSuccess)
                {
                    if (initializationResult.IsAuthenticationFailure)
                    {
                        var failureDecision = loginAttempts.RecordFailure(username, remoteAddress);
                        logger.LogWarning(
                            "首次管理员初始化令牌校验失败。Username={Username}; RemoteAddress={RemoteAddress}; Locked={Locked}",
                            username,
                            remoteAddress,
                            !failureDecision.Allowed);
                        if (!failureDecision.Allowed)
                        {
                            SetRetryAfter(context, failureDecision.RetryAfter);
                            return Results.Json(
                                new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                                statusCode: StatusCodes.Status429TooManyRequests);
                        }
                    }

                    return Results.Json(
                        new ApiErrorResponse(initializationResult.ErrorMessage),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var user = await userService.AuthenticateAsync(username, password);
                if (user == null)
                {
                    var failureDecision = loginAttempts.RecordFailure(username, remoteAddress);
                    logger.LogWarning(
                        "登录失败。Username={Username}; RemoteAddress={RemoteAddress}; Locked={Locked}",
                        username,
                        remoteAddress,
                        !failureDecision.Allowed);
                    if (!failureDecision.Allowed)
                    {
                        SetRetryAfter(context, failureDecision.RetryAfter);
                        return Results.Json(
                            new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                            statusCode: StatusCodes.Status429TooManyRequests);
                    }
                    return Results.Unauthorized();
                }

                loginAttempts.RecordSuccess(username, remoteAddress);
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

        private static void SetRetryAfter(HttpContext context, TimeSpan retryAfter)
        {
            int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
