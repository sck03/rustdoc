using System.Net;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class AiEndpointPolicyTests
{
    [Theory]
    [InlineData("https://api.deepseek.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/api/chat")]
    [InlineData("http://127.0.0.1:11434/api/chat")]
    [InlineData("http://[::1]:11434/api/chat")]
    public void Normalize_AcceptsPublicHttpsAndExplicitLoopback(string endpoint)
    {
        Assert.Equal(endpoint, AiEndpointPolicy.Normalize(endpoint).AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("http://api.example.com/v1/chat/completions")]
    [InlineData("https://10.0.0.8/v1/chat/completions")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://user:secret@api.example.com/v1/chat/completions")]
    public void Normalize_RejectsUnsafeEndpoint(string endpoint)
    {
        Assert.Throws<ServiceValidationException>(() => AiEndpointPolicy.Normalize(endpoint));
    }

    [Fact]
    public async Task ValidateAllowedHostAsync_RejectsDnsRebindingToPrivateAddress()
    {
        Uri endpoint = AiEndpointPolicy.Normalize("https://ai.example.test/v1/chat/completions");

        await Assert.ThrowsAsync<ServiceValidationException>(() =>
            AiEndpointPolicy.ValidateAllowedHostAsync(
                endpoint,
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.168.1.10") }),
                CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAllowedHostAsync_AllowsPublicAndExplicitLoopbackAddresses()
    {
        await AiEndpointPolicy.ValidateAllowedHostAsync(
            AiEndpointPolicy.Normalize("https://ai.example.test/v1/chat/completions"),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            CancellationToken.None);
        await AiEndpointPolicy.ValidateAllowedHostAsync(
            AiEndpointPolicy.Normalize("http://localhost:11434/api/chat"),
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }),
            CancellationToken.None);
    }
}
