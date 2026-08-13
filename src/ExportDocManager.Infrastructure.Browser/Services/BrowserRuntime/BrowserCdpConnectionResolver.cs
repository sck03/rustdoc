using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ExportDocManager.Services.BrowserRuntime;

public static class BrowserCdpConnectionResolver
{
    private const int MaximumDiscoveryPayloadBytes = 64 * 1024;
    private const string BrowserWebSocketPathPrefix = "/devtools/browser/";
    private static readonly HttpClient DirectHttpClient = CreateDirectHttpClient();

    public static async Task<Uri> ResolveWebSocketEndpointAsync(
        Uri discoveryEndpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveryEndpoint);
        if ((discoveryEndpoint.Scheme != Uri.UriSchemeHttp &&
             discoveryEndpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(discoveryEndpoint.Host))
        {
            throw new ArgumentException("CDP discovery endpoint must use HTTP(S).", nameof(discoveryEndpoint));
        }

        Uri connectionEndpoint = await ResolveConnectionEndpointAsync(discoveryEndpoint, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(connectionEndpoint, "/json/version"));
            using HttpResponseMessage response = await DirectHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new IOException(
                    $"Chromium CDP discovery returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is > MaximumDiscoveryPayloadBytes)
            {
                throw new IOException("Chromium CDP discovery response exceeded the allowed size.");
            }

            await using Stream content = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var payload = new MemoryStream();
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (payload.Length + read > MaximumDiscoveryPayloadBytes)
                {
                    throw new IOException("Chromium CDP discovery response exceeded the allowed size.");
                }
                payload.Write(buffer, 0, read);
            }

            payload.Position = 0;
            using JsonDocument document = await JsonDocument
                .ParseAsync(payload, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("webSocketDebuggerUrl", out JsonElement property) ||
                property.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(property.GetString(), UriKind.Absolute, out Uri? discoveredWebSocket) ||
                discoveredWebSocket.Scheme is not ("ws" or "wss") ||
                !string.IsNullOrEmpty(discoveredWebSocket.UserInfo) ||
                !string.IsNullOrEmpty(discoveredWebSocket.Query) ||
                !string.IsNullOrEmpty(discoveredWebSocket.Fragment) ||
                !discoveredWebSocket.AbsolutePath.StartsWith(
                    BrowserWebSocketPathPrefix,
                    StringComparison.Ordinal) ||
                discoveredWebSocket.AbsolutePath.Length == BrowserWebSocketPathPrefix.Length)
            {
                throw new IOException("Chromium CDP discovery returned an invalid WebSocket endpoint.");
            }

            return new UriBuilder(connectionEndpoint)
            {
                Scheme = connectionEndpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                Path = discoveredWebSocket.AbsolutePath,
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            throw new IOException("Chromium CDP discovery failed.", ex);
        }
    }

    internal static async Task<Uri> ResolveConnectionEndpointAsync(
        Uri discoveryEndpoint,
        CancellationToken cancellationToken)
    {
        if (!BrowserCdpEndpointPolicy.ShouldResolveServiceName(discoveryEndpoint))
        {
            return discoveryEndpoint;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns
                .GetHostAddressesAsync(discoveryEndpoint.DnsSafeHost, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new IOException("Chromium container service name could not be resolved.", ex);
        }

        IPAddress? address = addresses
            .Where(BrowserCdpEndpointPolicy.IsTrustedHttpAddress)
            .Distinct()
            .OrderBy(candidate => candidate.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .FirstOrDefault();
        if (address is null)
        {
            throw new IOException("Chromium container service did not resolve to a trusted private address.");
        }

        return new UriBuilder(discoveryEndpoint)
        {
            Host = address.ToString()
        }.Uri;
    }

    private static HttpClient CreateDirectHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseProxy = false
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
