using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/auth/login", async Task<Results<
                Ok<ApiLoginResponse>,
                BadRequest<ApiErrorResponse>,
                JsonHttpResult<ApiErrorResponse>>>(
                HttpContext context,
                ApiLoginRequest request,
                IDatabaseInitializationService databaseInitializationService,
                IUserService userService,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiLoginAttemptService loginAttempts,
                ApiDownloadTicketService downloadTicketService,
                ApiDesktopAccessOptions desktopAccessOptions,
                [FromHeader(Name = ApiRuntimeOptions.BootstrapTokenHeaderName)] string? bootstrapToken,
                ILogger<ApiLoginAttemptService> logger) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Username))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("用户名不能为空。"));
                }

                string username = request.Username.Trim();
                string password = request.Password ?? string.Empty;
                string remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var attemptDecision = loginAttempts.Evaluate(username, remoteAddress);
                if (!attemptDecision.Allowed)
                {
                    SetRetryAfter(context, attemptDecision.RetryAfter);
                    return TypedResults.Json(
                        new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                        statusCode: StatusCodes.Status429TooManyRequests);
                }

                var initializationResult = await databaseInitializationService.InitializeAsync(
                    username,
                    password,
                    bootstrapToken ?? string.Empty);
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
                            return TypedResults.Json(
                                new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                                statusCode: StatusCodes.Status429TooManyRequests);
                        }

                        return TypedResults.Json(
                            new ApiErrorResponse(initializationResult.ErrorMessage),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    return TypedResults.Json(
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
                        return TypedResults.Json(
                            new ApiErrorResponse("登录尝试过于频繁，请稍后再试。"),
                            statusCode: StatusCodes.Status429TooManyRequests);
                    }
                    return TypedResults.Json(
                        new ApiErrorResponse("用户名或密码错误。"),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                loginAttempts.RecordSuccess(username, remoteAddress);
                downloadTicketService.ResetSession(
                    context,
                    revokeUnboundDesktopTickets: ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions));
                var token = await tokenService.IssueAsync(user, cancellationToken: context.RequestAborted);
                return TypedResults.Ok(new ApiLoginResponse(
                    "Bearer",
                    token.AccessToken,
                    token.ExpiresAt,
                    ApiUserDtoFactory.FromUser(user, authorizationService)));
            })
            .WithApiAccess(false, true, false)
            .WithName("Login")
            .Produces<ApiErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapGet("/api/auth/me", Results<Ok<ApiUserDto>, UnauthorizedHttpResult> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                return user == null
                    ? TypedResults.Unauthorized()
                    : TypedResults.Ok(ApiUserDtoFactory.FromUser(user, authorizationService));
            })
            .WithName("getCurrentUser")
            .WithApiAccess(true, true, false);

            endpoints.MapPost("/api/auth/renew", async Task<Results<Ok<ApiLoginResponse>, UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                string currentToken = ApiCurrentUserContext.GetBearerToken(context);
                if (user == null || string.IsNullOrWhiteSpace(currentToken))
                {
                    return TypedResults.Unauthorized();
                }

                var renewed = await tokenService.IssueAsync(
                    user,
                    cancellationToken: context.RequestAborted);
                if (!await tokenService.RevokeAsync(currentToken, context.RequestAborted))
                {
                    await tokenService.RevokeAsync(renewed.AccessToken, context.RequestAborted);
                    return TypedResults.Unauthorized();
                }

                return TypedResults.Ok(new ApiLoginResponse(
                    "Bearer",
                    renewed.AccessToken,
                    renewed.ExpiresAt,
                    ApiUserDtoFactory.FromUser(user, authorizationService)));
            })
            .WithName("RenewSession")
            .WithApiAccess(true, true, false);

            endpoints.MapPost("/api/auth/logout", async Task<Ok<ApiLogoutResponse>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDownloadTicketService downloadTicketService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                bool revoked = await tokenService.RevokeAsync(
                    ApiCurrentUserContext.GetBearerToken(context),
                    context.RequestAborted);
                if (user != null)
                {
                    downloadTicketService.RevokeSubject(context, user.Id.ToString());
                }
                else
                {
                    downloadTicketService.ResetSession(context);
                }
                return TypedResults.Ok(new ApiLogoutResponse(revoked));
            })
            .WithName("Logout")
            .WithApiAccess(true, true, false);
        }

        private static void SetRetryAfter(HttpContext context, TimeSpan retryAfter)
        {
            int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
