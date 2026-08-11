using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Tools
{
    public interface IContainerLoadingService
    {
        Task<List<ContainerProject>> GetRecentProjectsAsync(
            int limit,
            CancellationToken cancellationToken = default);

        Task<ContainerProject?> GetProjectAsync(
            int projectId,
            CancellationToken cancellationToken = default);

        Task<List<ContainerProjectItem>> GetProjectItemsAsync(
            int projectId,
            CancellationToken cancellationToken = default);

        Task SaveProjectAsync(
            ContainerProject project,
            List<ContainerProjectItem> items,
            CancellationToken cancellationToken = default);

        Task DeleteProjectAsync(
            int projectId,
            CancellationToken cancellationToken = default);

        Task<List<ContainerTypeDefinition>> GetContainerTypesAsync(
            CancellationToken cancellationToken = default);

        Task SaveContainerTypeAsync(
            ContainerTypeDefinition typeDef,
            CancellationToken cancellationToken = default);

        Task DeleteContainerTypeAsync(
            int id,
            CancellationToken cancellationToken = default);
    }
}
