using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowReferenceCatalogService
    {
        Task<SingleWindowReferenceCatalogModel> LoadEffectiveCatalogAsync(CancellationToken cancellationToken = default);

        Task SaveOverrideCatalogAsync(SingleWindowReferenceCatalogModel catalog, CancellationToken cancellationToken = default);

        Task ImportCatalogAsync(string filePath, CancellationToken cancellationToken = default);

        Task ExportCatalogAsync(SingleWindowReferenceCatalogModel catalog, string filePath, CancellationToken cancellationToken = default);

        Task ResetToBundledCatalogAsync(CancellationToken cancellationToken = default);

        string GetOverrideCatalogPath();
    }
}
