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

        public User ResolveCached(HttpContext context)
        {
            return ResolveCachedUser(context);
        }

        public async Task<User> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            var cachedUser = ResolveCachedUser(context);
            if (cachedUser != null)
            {
                return cachedUser;
            }

            var user = await _tokenService.ValidateAsync(GetBearerToken(context), cancellationToken)
                .ConfigureAwait(false);
            if (context != null && user != null)
            {
                context.Items[ApiEndpointAuth.AuthenticatedUserItemKey] = user;
            }

            return user;
        }

        internal static User ResolveCachedUser(HttpContext context)
        {
            return context?.Items.TryGetValue(ApiEndpointAuth.AuthenticatedUserItemKey, out var item) == true
                ? item as User
                : null;
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
