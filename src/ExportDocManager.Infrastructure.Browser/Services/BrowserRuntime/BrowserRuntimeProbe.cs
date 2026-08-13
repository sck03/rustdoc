using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime;

public sealed class BrowserRuntimeProbe : IBrowserRuntimeProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!BrowserCdpEndpointPolicy.TryResolve(out Uri? endpoint))
        {
            return true;
        }

        try
        {
            await BrowserCdpConnectionResolver
                .ResolveWebSocketEndpointAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
