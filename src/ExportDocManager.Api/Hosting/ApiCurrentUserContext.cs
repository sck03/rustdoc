using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiCurrentUserContext : ICurrentUserContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApiBackgroundJobExecutionUserAccessor _backgroundJobUser;

        public ApiCurrentUserContext(
            IHttpContextAccessor httpContextAccessor,
            ApiBackgroundJobExecutionUserAccessor backgroundJobUser)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _backgroundJobUser = backgroundJobUser ?? throw new ArgumentNullException(nameof(backgroundJobUser));
        }

        public User? CurrentUser
        {
            get
            {
                if (_backgroundJobUser.CurrentUser != null)
                {
                    return _backgroundJobUser.CurrentUser;
                }

                var context = _httpContextAccessor.HttpContext;
                if (context?.Items.TryGetValue(ApiEndpointAuth.AuthenticatedUserItemKey, out var item) == true &&
                    item is User cachedUser)
                {
                    return cachedUser;
                }

                return null;
            }
        }

        public static string GetBearerToken(HttpContext context)
        {
            return ApiCurrentUserResolver.GetBearerToken(context);
        }
    }

    public sealed class ApiBackgroundJobExecutionUserAccessor
    {
        private readonly AsyncLocal<User?> _currentUser = new();

        public User? CurrentUser => _currentUser.Value;

        public IDisposable Push(User? user)
        {
            User? previous = _currentUser.Value;
            _currentUser.Value = user;
            return new BackgroundUserScope(this, previous);
        }

        private sealed class BackgroundUserScope : IDisposable
        {
            private readonly ApiBackgroundJobExecutionUserAccessor _owner;
            private readonly User? _previous;
            private bool _disposed;

            public BackgroundUserScope(
                ApiBackgroundJobExecutionUserAccessor owner,
                User? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _owner._currentUser.Value = _previous;
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
