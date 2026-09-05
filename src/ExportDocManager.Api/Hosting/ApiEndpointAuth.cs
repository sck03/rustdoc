using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiEndpointAuth
    {
        public const string AuthenticatedUserItemKey = "__ExportDocManagerApiUser";

        public static User? RequireUser(HttpContext context, IApiSessionTokenService tokenService)
        {
            ArgumentNullException.ThrowIfNull(tokenService);
            return ApiCurrentUserResolver.ResolveCachedUser(context);
        }

        public static User GetRequiredUser(HttpContext context) =>
            ApiCurrentUserResolver.ResolveCachedUser(context)
            ?? throw new InvalidOperationException("认证中间件未提供当前用户。");

        public static bool HasValidDesktopAccess(HttpContext context, ApiDesktopAccessOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            string submittedToken = context.Request.Headers[ApiDesktopAccessOptions.HeaderName].ToString();
            return options.IsValid(submittedToken);
        }

        public static bool RequiresDocumentationAuthentication(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            return runtimeOptions.NetworkMode;
        }
    }

    public sealed class ApiAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ApiCurrentUserResolver currentUserResolver)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(currentUserResolver);

            if (!RequiresAuthentication(context))
            {
                await _next(context);
                return;
            }

            if (await currentUserResolver.ResolveAsync(context, context.RequestAborted).ConfigureAwait(false) == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
        }

        public static bool RequiresAuthentication(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            return endpoint?.GetApiAccessMetadata()?.RequiresAuthentication ?? false;
        }
    }

    public sealed class ApiDesktopAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiDesktopAccessMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ApiDesktopAccessOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            if (!options.IsEnabled || !RequiresDesktopAccess(context))
            {
                await _next(context);
                return;
            }

            string submittedToken = context.Request.Headers[ApiDesktopAccessOptions.HeaderName].ToString();
            if (!options.IsValid(submittedToken))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await _next(context);
        }

        public static bool RequiresDesktopAccess(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            return endpoint?.GetApiAccessMetadata()?.RequiresDesktopAccess ?? false;
        }
    }

    public sealed class ApiLicenseRequirementMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiLicenseRequirementMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ILicenseService licenseService)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(licenseService);

            if (!RequiresValidLicense(context))
            {
                await _next(context);
                return;
            }

            var status = await licenseService.GetStatusAsync(context.RequestAborted).ConfigureAwait(false);
            if (!status.IsTrialExpired)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(string.IsNullOrWhiteSpace(status.Message)
                        ? "试用期已过，请先注册授权。"
                        : status.Message),
                    cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
        }

        public static bool RequiresValidLicense(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            return endpoint?.GetApiAccessMetadata()?.RequiresLicense ?? false;
        }
    }

    public sealed class ApiWorkspaceAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiWorkspaceAccessMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(
            HttpContext context,
            ApiCurrentUserResolver currentUserResolver,
            ApiAuthorizationService authorizationService)
        {
            var endpoint = context.GetEndpoint();
            // Anonymous/system endpoints do not participate in capability
            // evaluation.  Every authenticated API endpoint must, however,
            // carry either an explicit capability policy or an explicit
            // handler-owned bypass marker.  A missing policy is a deployment
            // mistake and must fail closed rather than accidentally granting
            // access to a newly added route.
            if (endpoint == null ||
                !(endpoint.GetApiAccessMetadata()?.RequiresAuthentication ?? false) ||
                endpoint.HasExplicitPermissionBypass())
            {
                await _next(context);
                return;
            }

            var capabilities = endpoint.GetApiCapabilityMetadata();
            var permission = endpoint.GetApiPermissionMetadata();
            if (capabilities == null && permission == null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                        new ApiErrorResponse("该接口未声明权限能力，访问已被拒绝。"),
                        cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            var user = currentUserResolver.ResolveCached(context);
            // A concrete capability contract is the sole authorization fact
            // for migrated endpoints. Legacy module/HTTP-method inference is
            // retained only for endpoints that have not yet declared action
            // metadata; evaluating both would create two diverging policies.
            bool allowed = user != null && (capabilities != null
                ? capabilities.Requirements.All(requirement => authorizationService.CanUsePermission(
                    user, requirement.ResourceKey, requirement.Action))
                : permission != null && CanUseLegacyModulePermission(
                    context, user, permission, authorizationService));
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await _next(context);
        }

        private static bool CanUseLegacyModulePermission(
            HttpContext context,
            User user,
            ApiEndpointPermissionMetadata permission,
            ApiAuthorizationService authorizationService)
        {
            var requirement = permission.Resolve(context);
            return authorizationService.CanUseModule(user, requirement.Module, requirement.AccessLevel);
        }
    }

    public static class ApiAuthenticationApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseExportDocManagerDesktopAccess(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiDesktopAccessMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerApiAuthentication(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiAuthenticationMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerLicenseRequirement(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiLicenseRequirementMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerWorkspaceAccess(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<ApiWorkspaceAccessMiddleware>();
        }
    }
}
