using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Infrastructure
{
    public interface ISharedDatabaseCapabilityService
    {
        SharedDatabaseCapabilityProfile GetCurrentProfile();
    }
}
