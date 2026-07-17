using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IShutdownMaintenanceService
    {
        Task<ShutdownMaintenanceResult> RunAsync(CancellationToken cancellationToken = default);
    }
}
