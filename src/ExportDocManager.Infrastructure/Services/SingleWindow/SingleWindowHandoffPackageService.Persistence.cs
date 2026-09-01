using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService
    {
        private async Task<SingleWindowDocumentPersistenceResult> TryPersistCustomsCooDocumentAsync(
            CooSourceSnapshot source,
            CooMappedDocument mapped,
            CancellationToken cancellationToken)
        {
            return await _singleWindowDocumentPersistenceService.UpsertCustomsCooDocumentAsync(
                source,
                mapped,
                cancellationToken);
        }

        private async Task<SingleWindowDocumentPersistenceResult> TryPersistAgentConsignmentDocumentAsync(
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
