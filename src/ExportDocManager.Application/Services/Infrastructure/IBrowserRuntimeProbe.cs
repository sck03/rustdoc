namespace ExportDocManager.Services.Infrastructure;

public interface IBrowserRuntimeProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
