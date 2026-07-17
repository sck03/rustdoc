using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCurrentUserResolver
    {
        private readonly IApiSessionTokenService _tokenService;

        public ApiCurrentUserResolver(IApiSessionTokenService tokenService)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        public User Resolve(HttpContext context)
        {
            return Resolve(context, _tokenService);
        }

        internal static User Resolve(HttpContext context, IApiSessionTokenService tokenService)
        {
            ArgumentNullException.ThrowIfNull(tokenService);

            if (context?.Items.TryGetValue(ApiEndpointAuth.AuthenticatedUserItemKey, out var item) == true &&
                item is User cachedUser)
            {
                return cachedUser;
            }

            var user = tokenService.Validate(GetBearerToken(context));
            if (context != null && user != null)
            {
                context.Items[ApiEndpointAuth.AuthenticatedUserItemKey] = user;
            }

            return user;
        }

        public static string GetBearerToken(HttpContext context)
        {
            string authorization = context?.Request.Headers.Authorization.ToString() ?? string.Empty;
            const string bearerPrefix = "Bearer ";

            return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorization[bearerPrefix.Length..].Trim()
                : string.Empty;
        }
    }
}
