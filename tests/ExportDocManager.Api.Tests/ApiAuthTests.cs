using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http;

namespace ExportDocManager.Api.Tests
{
    public class ApiAuthTests
    {
        [Fact]
        public void SessionTokenService_ShouldIssueValidateAndRevokeToken()
        {
            var service = new InMemoryApiSessionTokenService();
            var user = new User
            {
                Id = 7,
                Username = "operator",
                PasswordHash = "secret-hash",
                FullName = "Operator",
                Role = "User",
                DepartmentId = "D1",
                CompanyScope = "C1",
                IsActive = true
            };

            var issued = service.Issue(user, TimeSpan.FromMinutes(5));
            var validated = service.Validate(issued.AccessToken);

            Assert.False(string.IsNullOrWhiteSpace(issued.AccessToken));
            Assert.NotNull(validated);
            Assert.Equal(7, validated.Id);
            Assert.Equal("operator", validated.Username);
            Assert.Equal(string.Empty, validated.PasswordHash);
            Assert.True(service.Revoke(issued.AccessToken));
            Assert.Null(service.Validate(issued.AccessToken));
        }

        [Fact]
        public void SessionTokenService_ShouldRevokeAllSessionsForUser()
        {
            var service = new InMemoryApiSessionTokenService();
            var first = service.Issue(new User { Id = 7, Username = "operator", Role = "User", IsActive = true });
            var second = service.Issue(new User { Id = 7, Username = "operator", Role = "User", IsActive = true });
            var other = service.Issue(new User { Id = 8, Username = "other", Role = "User", IsActive = true });

            Assert.Equal(2, service.RevokeUserSessions(7));
            Assert.Null(service.Validate(first.AccessToken));
            Assert.Null(service.Validate(second.AccessToken));
            Assert.NotNull(service.Validate(other.AccessToken));
        }

        [Fact]
        public void SessionTokenService_ShouldRejectExpiredToken()
        {
            var service = new InMemoryApiSessionTokenService();
            var issued = service.Issue(
                new User { Id = 1, Username = "admin", PasswordHash = string.Empty, Role = "Admin", IsActive = true },
                TimeSpan.FromMilliseconds(-1));

            Assert.Null(service.Validate(issued.AccessToken));
        }

        [Fact]
        public async Task CurrentUserContext_ShouldResolveBearerToken()
        {
            var httpContext = new DefaultHttpContext();
            var tokenService = new InMemoryApiSessionTokenService();
            var issued = tokenService.Issue(
                new User { Id = 1, Username = "admin", PasswordHash = string.Empty, Role = "Admin", IsActive = true });
            httpContext.Request.Headers.Authorization = $"Bearer {issued.AccessToken}";

            var accessor = new HttpContextAccessor
            {
                HttpContext = httpContext
            };
            var resolver = new ApiCurrentUserResolver(tokenService);
            await resolver.ResolveAsync(httpContext);
            var context = new ApiCurrentUserContext(accessor, resolver);

            Assert.Equal("admin", context.CurrentUser.Username);
            Assert.Equal(issued.AccessToken, ApiCurrentUserContext.GetBearerToken(httpContext));
        }

        [Fact]
        public async Task CurrentUserResolver_ShouldKeepUsersSeparatedByHttpContext()
        {
            var tokenService = new InMemoryApiSessionTokenService();
            var adminToken = tokenService.Issue(
                new User { Id = 1, Username = "admin", PasswordHash = string.Empty, Role = "Admin", IsActive = true });
            var operatorToken = tokenService.Issue(
                new User { Id = 2, Username = "operator", PasswordHash = string.Empty, Role = "User", IsActive = true });
            var adminContext = CreateHttpContextWithBearerToken(adminToken.AccessToken);
            var operatorContext = CreateHttpContextWithBearerToken(operatorToken.AccessToken);
            var resolver = new ApiCurrentUserResolver(tokenService);

            var admin = await resolver.ResolveAsync(adminContext);
            var operatorUser = await resolver.ResolveAsync(operatorContext);

            Assert.Equal("admin", admin.Username);
            Assert.Equal("operator", operatorUser.Username);
            Assert.Equal("admin", Assert.IsType<User>(adminContext.Items[ApiEndpointAuth.AuthenticatedUserItemKey]).Username);
            Assert.Equal("operator", Assert.IsType<User>(operatorContext.Items[ApiEndpointAuth.AuthenticatedUserItemKey]).Username);
        }

        [Fact]
        public async Task CurrentUserContext_ShouldResolveActiveHttpContextWithoutCrossRequestState()
        {
            var tokenService = new InMemoryApiSessionTokenService();
            var adminToken = tokenService.Issue(
                new User { Id = 1, Username = "admin", PasswordHash = string.Empty, Role = "Admin", IsActive = true });
            var operatorToken = tokenService.Issue(
                new User { Id = 2, Username = "operator", PasswordHash = string.Empty, Role = "User", IsActive = true });
            var adminContext = CreateHttpContextWithBearerToken(adminToken.AccessToken);
            var operatorContext = CreateHttpContextWithBearerToken(operatorToken.AccessToken);
            var accessor = new HttpContextAccessor();
            var resolver = new ApiCurrentUserResolver(tokenService);
            await resolver.ResolveAsync(adminContext);
            await resolver.ResolveAsync(operatorContext);
            var currentUserContext = new ApiCurrentUserContext(accessor, resolver);

            accessor.HttpContext = adminContext;
            Assert.Equal("admin", currentUserContext.CurrentUser.Username);

            accessor.HttpContext = operatorContext;
            Assert.Equal("operator", currentUserContext.CurrentUser.Username);

            accessor.HttpContext = adminContext;
            Assert.Equal("admin", currentUserContext.CurrentUser.Username);
        }

        [Fact]
        public void AuthenticationMiddleware_ShouldRequireTokenForApiExceptLogin()
        {
            Assert.False(ApiAuthenticationMiddleware.RequiresAuthentication("/healthz"));
            Assert.False(ApiAuthenticationMiddleware.RequiresAuthentication("/readyz"));
            Assert.False(ApiAuthenticationMiddleware.RequiresAuthentication("/openapi/v1.json"));
            Assert.False(ApiAuthenticationMiddleware.RequiresAuthentication("/api/auth/login"));
            Assert.False(ApiAuthenticationMiddleware.RequiresAuthentication("/api/system/shutdown-maintenance"));
            Assert.True(ApiAuthenticationMiddleware.RequiresAuthentication("/api/auth/me"));
            Assert.True(ApiAuthenticationMiddleware.RequiresAuthentication("/api/invoices"));
        }

        [Fact]
        public void DesktopAccessMiddleware_ShouldRequireDesktopTokenForApiWhenEnabled()
        {
            Assert.False(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/healthz"));
            Assert.False(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/readyz"));
            Assert.False(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/openapi/v1.json"));
            Assert.True(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/api/auth/login"));
            Assert.True(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/api/invoices"));
            Assert.True(ApiDesktopAccessMiddleware.RequiresDesktopAccess("/api/system/shutdown-maintenance"));
        }

        [Fact]
        public void LicenseRequirementMiddleware_ShouldAllowOnlyBootstrapAndLicenseApis()
        {
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/healthz"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/readyz"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/openapi/v1.json"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/auth/login"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/auth/me"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/auth/logout"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/system/license"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/system/license/register"));
            Assert.False(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/system/shutdown-maintenance"));
            Assert.True(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/invoices"));
            Assert.True(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/dashboard"));
            Assert.True(ApiLicenseRequirementMiddleware.RequiresValidLicense("/api/system/update"));
        }

        [Fact]
        public async Task AuthenticationMiddleware_ShouldRejectProtectedApiWithoutToken()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/invoices";

            bool nextCalled = false;
            var middleware = new ApiAuthenticationMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                new ApiCurrentUserResolver(new InMemoryApiSessionTokenService()));

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }

        [Fact]
        public async Task AuthenticationMiddleware_ShouldCacheValidatedUserForProtectedApi()
        {
            var tokenService = new InMemoryApiSessionTokenService();
            var issued = tokenService.Issue(
                new User { Id = 2, Username = "operator", PasswordHash = string.Empty, Role = "User", IsActive = true });
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/invoices";
            context.Request.Headers.Authorization = $"Bearer {issued.AccessToken}";

            bool nextCalled = false;
            var middleware = new ApiAuthenticationMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, new ApiCurrentUserResolver(tokenService));

            Assert.True(nextCalled);
            var cachedUser = Assert.IsType<User>(context.Items[ApiEndpointAuth.AuthenticatedUserItemKey]);
            Assert.Equal("operator", cachedUser.Username);
        }

        [Fact]
        public async Task DesktopAccessMiddleware_ShouldAllowApiWhenDisabled()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/auth/login";

            bool nextCalled = false;
            var middleware = new ApiDesktopAccessMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, new ApiDesktopAccessOptions());

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task DesktopAccessMiddleware_ShouldRejectApiWhenEnabledAndHeaderMissing()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/auth/login";

            bool nextCalled = false;
            var middleware = new ApiDesktopAccessMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, new ApiDesktopAccessOptions { Token = "desktop-secret" });

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [Fact]
        public async Task DesktopAccessMiddleware_ShouldAllowApiWhenHeaderMatches()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/auth/login";
            context.Request.Headers[ApiDesktopAccessOptions.HeaderName] = "desktop-secret";

            bool nextCalled = false;
            var middleware = new ApiDesktopAccessMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, new ApiDesktopAccessOptions { Token = "desktop-secret" });

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task LicenseRequirementMiddleware_ShouldRejectBusinessApiWhenTrialExpired()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/invoices";

            bool nextCalled = false;
            var middleware = new ApiLicenseRequirementMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                new StubLicenseService(new LicenseStatus
                {
                    IsTrialExpired = true,
                    Message = "试用期已过，请注册。"
                }));

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);
        }

        [Fact]
        public async Task LicenseRequirementMiddleware_ShouldAllowLicenseApiWhenTrialExpired()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/system/license";

            bool nextCalled = false;
            var middleware = new ApiLicenseRequirementMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(
                context,
                new StubLicenseService(new LicenseStatus
                {
                    IsTrialExpired = true,
                    Message = "试用期已过，请注册。"
                }));

            Assert.True(nextCalled);
        }

        private static DefaultHttpContext CreateHttpContextWithBearerToken(string accessToken)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = $"Bearer {accessToken}";
            return context;
        }

        private sealed class StubLicenseService : ILicenseService
        {
            private readonly LicenseStatus _status;

            public StubLicenseService(LicenseStatus status)
            {
                _status = status;
            }

            public Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_status);
            }

            public Task<LicenseRegistrationResult> RegisterAsync(
                string licenseKey,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new LicenseRegistrationResult
                {
                    Success = !_status.IsTrialExpired,
                    Message = _status.Message,
                    Status = _status
                });
            }
        }
    }
}
