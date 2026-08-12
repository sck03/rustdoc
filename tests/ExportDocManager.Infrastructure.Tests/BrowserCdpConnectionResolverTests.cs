using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ExportDocManager.Services.BrowserRuntime;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BrowserCdpConnectionResolverTests
{
    [Theory]
    [InlineData("http://browser:9222", true)]
    [InlineData("http://localhost:9222", false)]
    [InlineData("http://127.0.0.1:9222", false)]
    [InlineData("https://browser.example.com:9443", false)]
    public void ServiceNameResolution_ShouldOnlyRewriteTrustedHttpContainerNames(
        string value,
        bool expected)
    {
        Assert.Equal(
            expected,
            BrowserCdpEndpointPolicy.ShouldResolveServiceName(new Uri(value)));
    }

    [Theory]
    [InlineData("172.20.0.2", true)]
    [InlineData("::ffff:172.20.0.2", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void ResolvedServiceAddress_ShouldStayOnTrustedPrivateNetworks(
        string value,
        bool expected)
    {
        Assert.Equal(
            expected,
            BrowserCdpEndpointPolicy.IsTrustedHttpAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public async Task Discovery_ShouldRewriteLoopbackWebSocketToTheReachableAuthority()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<string> server = ServeDiscoveryAsync(
            listener,
            "ws://127.0.0.1:9223/devtools/browser/test-browser-id");

        Uri result = await BrowserCdpConnectionResolver.ResolveWebSocketEndpointAsync(
            new Uri($"http://127.0.0.1:{port}"));

        Assert.Equal(
            $"ws://127.0.0.1:{port}/devtools/browser/test-browser-id",
            result.ToString().TrimEnd('/'));
        Assert.Equal($"127.0.0.1:{port}", await server);
    }

    [Fact]
    public async Task Discovery_ShouldRejectNonBrowserWebSocketPaths()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<string> server = ServeDiscoveryAsync(
            listener,
            "ws://127.0.0.1:9223/devtools/page/not-a-browser");

        await Assert.ThrowsAsync<IOException>(() =>
            BrowserCdpConnectionResolver.ResolveWebSocketEndpointAsync(
                new Uri($"http://127.0.0.1:{port}")));
        await server;
    }

    private static async Task<string> ServeDiscoveryAsync(
        TcpListener listener,
        string webSocketDebuggerUrl)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        string host = string.Empty;
        _ = await reader.ReadLineAsync();
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
            {
                break;
            }
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                host = line["Host:".Length..].Trim();
            }
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Browser = "Chromium/Test",
            webSocketDebuggerUrl
        });
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=UTF-8\r\n" +
            $"Content-Length: {content.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
        await stream.WriteAsync(content);
        await stream.FlushAsync();
        return host;
    }
}
