using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public interface ISettingsService
    {
        /// <summary>
        /// Returns an isolated snapshot. Mutating it never changes persisted settings;
        /// use <see cref="UpdateAsync"/> for every write.
        /// </summary>
        AppSettings Settings { get; }

        Task LoadAsync(CancellationToken cancellationToken = default);

        Task<bool> UpdateAsync(Func<AppSettings, bool> update, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This settings service does not support atomic updates.");
    }
}
