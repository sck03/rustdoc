namespace ExportDocManager.Services.Infrastructure
{
    public interface ILocalPreferenceService
    {
        T Load<T>(string key) where T : class, new();

        Task SaveAsync<T>(string key, T state, CancellationToken cancellationToken = default) where T : class;
    }
}
