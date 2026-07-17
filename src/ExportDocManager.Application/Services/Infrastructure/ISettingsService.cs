using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public interface ISettingsService
    {
        AppSettings Settings { get; }

        Task LoadAsync();

        Task SaveAsync();

        Task<bool> UpdateAsync(Func<AppSettings, bool> update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);

            if (!update(Settings))
            {
                return Task.FromResult(false);
            }

            return PersistAsync();

            async Task<bool> PersistAsync()
            {
                await SaveAsync().ConfigureAwait(false);
                return true;
            }
        }
    }
}
