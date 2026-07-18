using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCurrentUserContext : ICurrentUserContext
    {
        private static readonly AsyncLocal<User> BackgroundCurrentUser = new();
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApiCurrentUserResolver _currentUserResolver;

        public ApiCurrentUserContext(
            IHttpContextAccessor httpContextAccessor,
            ApiCurrentUserResolver currentUserResolver)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _currentUserResolver = currentUserResolver ?? throw new ArgumentNullException(nameof(currentUserResolver));
        }

        public User CurrentUser
        {
            get
            {
                if (BackgroundCurrentUser.Value != null)
                {
                    return BackgroundCurrentUser.Value;
                }

                var context = _httpContextAccessor.HttpContext;
                if (context?.Items.TryGetValue(ApiEndpointAuth.AuthenticatedUserItemKey, out var item) == true &&
                    item is User cachedUser)
                {
                    return cachedUser;
                }

                return _currentUserResolver.ResolveCached(context);
            }
        }

        public static IDisposable UseBackgroundUser(User user)
        {
            var previous = BackgroundCurrentUser.Value;
            BackgroundCurrentUser.Value = user;
            return new BackgroundUserScope(previous);
        }

        public static string GetBearerToken(HttpContext context)
        {
            return ApiCurrentUserResolver.GetBearerToken(context);
        }

        private sealed class BackgroundUserScope : IDisposable
        {
            private readonly User _previous;
            private bool _disposed;

            public BackgroundUserScope(User previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                BackgroundCurrentUser.Value = _previous;
                _disposed = true;
            }
        }
    }

    public sealed class ApiAuditUserProvider : IAuditUserProvider
    {
        private readonly ICurrentUserContext _currentUserContext;

        public ApiAuditUserProvider(ICurrentUserContext currentUserContext)
        {
            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        }

        public string GetCurrentUserName()
        {
            return _currentUserContext.CurrentUser?.Username ?? "Api";
        }
    }
}
