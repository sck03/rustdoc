using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class WebDavEndpointPolicyTests
    {
        [Theory]
        [InlineData("https://dav.example.com/backups/", "https://dav.example.com/backups")]
        [InlineData("http://localhost:8080/dav", "http://localhost:8080/dav")]
        [InlineData("http://127.0.0.1:8080/dav", "http://127.0.0.1:8080/dav")]
        [InlineData("http://[::1]:8080/dav", "http://[::1]:8080/dav")]
        public void TryNormalize_ShouldAcceptHttpsAndLoopbackHttp(string input, string expected)
        {
            Assert.True(WebDavEndpointPolicy.TryNormalize(input, out var normalized, out var error), error);
            Assert.Equal(expected, normalized);
        }

        [Theory]
        [InlineData("http://dav.example.com/backups")]
        [InlineData("https://user:password@dav.example.com/backups")]
        [InlineData("https://dav.example.com/backups?token=secret")]
        [InlineData("https://dav.example.com/backups#fragment")]
        [InlineData("file:///tmp/backups")]
        public void TryNormalize_ShouldRejectUnsafeEndpoints(string input)
        {
            Assert.False(WebDavEndpointPolicy.TryNormalize(input, out _, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }
}
