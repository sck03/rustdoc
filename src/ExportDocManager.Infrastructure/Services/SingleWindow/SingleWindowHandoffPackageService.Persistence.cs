using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private async Task<int> TryPersistCustomsCooDocumentAsync(
            CooSourceSnapshot source,
            CooMappedDocument mapped,
            CancellationToken cancellationToken)
        {
            return await _singleWindowDocumentPersistenceService.UpsertCustomsCooDocumentAsync(
                source,
                mapped,
                cancellationToken);
        }

        private async Task<int> TryPersistAgentConsignmentDocumentAsync(
            AcdSourceSnapshot source,
            AcdMappedDocument mapped,
            CancellationToken cancellationToken)
        {
            return await _singleWindowDocumentPersistenceService.UpsertAgentConsignmentDocumentAsync(
                source,
                mapped,
                cancellationToken);
        }
    }
}
