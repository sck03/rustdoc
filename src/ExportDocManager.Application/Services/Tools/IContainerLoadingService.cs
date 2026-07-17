using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Tools
{
    public interface IContainerLoadingService
    {
        Task<List<ContainerProject>> GetAllProjectsAsync();

        Task<ContainerProject> GetProjectAsync(int projectId);

        Task<List<ContainerProjectItem>> GetProjectItemsAsync(int projectId);

        Task SaveProjectAsync(ContainerProject project, List<ContainerProjectItem> items);

        Task DeleteProjectAsync(int projectId);

        Task<List<ContainerTypeDefinition>> GetContainerTypesAsync();

        Task SaveContainerTypeAsync(ContainerTypeDefinition typeDef);

        Task DeleteContainerTypeAsync(int id);
    }
}
