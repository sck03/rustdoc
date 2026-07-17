using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Infrastructure
{
    public interface ISystemLogCleanupService
    {
        Task<SystemLogCleanupResult> CleanAsync(CancellationToken cancellationToken = default);
    }
}
