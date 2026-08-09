using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests
{
    public class ApiPublicHealthProbeMiddlewareTests
    {
        [Fact]
        public async Task ReadinessProbe_ShouldResolveAuthenticationServiceGraph()
        {
            int authenticationGraphResolutionCount = 0;
            using var services = new ServiceCollection()
                .AddSingleton<ApiCurrentUserResolver>(_ =>
                {
                    authenticationGraphResolutionCount++;
                    return new ApiCurrentUserResolver(new InMemoryApiSessionTokenService());
                })
                .AddSingleton<IApiReadinessProbe>(new StubReadinessProbe(ready: true))
                .BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            app.UseExportDocManagerReadiness();

            var context = CreateContext(HttpMethods.Get, "/readyz");
            context.RequestServices = services;
            await app.Build()(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal(1, authenticationGraphResolutionCount);
            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public async Task ReadinessProbe_WhenDependencyCheckFails_ShouldReturnServiceUnavailable()
        {
            using var services = new ServiceCollection()
                .AddSingleton<IApiReadinessProbe>(new StubReadinessProbe(ready: false))
                .BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            app.UseExportDocManagerReadiness();

            var context = CreateContext(HttpMethods.Get, "/readyz");
            context.RequestServices = services;
            await app.Build()(context);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("not_ready", document.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public async Task LivenessProbe_ShouldNotResolveDependencyProbe()
        {
            using var services = new ServiceCollection()
                .AddSingleton<IApiReadinessProbe>(new ThrowingReadinessProbe())
                .BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            app.UseExportDocManagerReadiness();

            var context = CreateContext(HttpMethods.Get, "/livez");
            context.RequestServices = services;
            await app.Build()(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("alive", document.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public async Task AnonymousHealthProbe_ShouldCompleteBeforeDownstreamPipeline()
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            bool downstreamInvoked = false;
            app.UseExportDocManagerReadiness(
                new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.PostgreSqlProvider
                },
                new ApiRuntimeOptions
                {
                    DesktopAccessToken = "desktop-health-token"
                });
            app.Run(_ =>
            {
                downstreamInvoked = true;
                return Task.CompletedTask;
            });

            var context = CreateContext(HttpMethods.Get, "/healthz");
            await app.Build()(context);

            Assert.False(downstreamInvoked);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
            Assert.Contains(
                "PostgreSQL",
                document.RootElement.GetProperty("databaseProvider").GetString() ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Empty(document.RootElement.GetProperty("runtimePaths").EnumerateArray());
        }

        [Theory]
        [InlineData("Authorization", "Bearer administrator-token")]
        [InlineData(ApiDesktopAccessOptions.HeaderName, "desktop-health-token")]
        public async Task ProtectedHealthDiagnostics_ShouldContinueToEndpointPipeline(
            string headerName,
            string headerValue)
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            bool downstreamInvoked = false;
            app.UseExportDocManagerReadiness(
                new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.PostgreSqlProvider
                },
                new ApiRuntimeOptions
                {
                    DesktopAccessToken = "desktop-health-token"
                });
            app.Run(context =>
            {
                downstreamInvoked = true;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });

            var context = CreateContext(HttpMethods.Get, "/healthz");
            context.Request.Headers[headerName] = headerValue;
            await app.Build()(context);

            Assert.True(downstreamInvoked);
            Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        }

        private static DefaultHttpContext CreateContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private sealed class StubReadinessProbe(bool ready) : IApiReadinessProbe
        {
            public Task<ApiReadinessSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new ApiReadinessSnapshot(
                    ready,
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string> { ["database"] = ready ? "ready" : "unavailable" }));
        }

        private sealed class ThrowingReadinessProbe : IApiReadinessProbe
        {
            public Task<ApiReadinessSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Liveness must not resolve readiness dependencies.");
        }
    }
}
