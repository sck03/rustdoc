using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooProducerProfileService
    {
        Task<List<CustomsCooProducerProfile>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomsCooProducerProfile>> SearchAsync(string keyword, CancellationToken cancellationToken = default);

        Task<CustomsCooProducerProfile> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<CustomsCooProducerProfile> SaveOrUpdateAsync(CustomsCooProducerProfileInput input, CancellationToken cancellationToken = default);

        Task<int> SaveAsync(CustomsCooProducerProfileInput input, int? profileId = null, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

        Task<int> RememberProfilesAsync(IEnumerable<CustomsCooProducerProfileInput> inputs, CancellationToken cancellationToken = default);
    }
}
