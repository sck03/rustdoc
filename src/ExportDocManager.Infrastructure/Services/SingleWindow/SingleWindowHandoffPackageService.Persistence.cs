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
            try
            {
                return await _singleWindowDocumentPersistenceService.UpsertCustomsCooDocumentAsync(
                    source,
                    mapped,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Persisting customs coo document failed for invoice {InvoiceId}", source.Invoice?.Id);
                return 0;
            }
        }

        private async Task<int> TryPersistAgentConsignmentDocumentAsync(
            AcdSourceSnapshot source,
            AcdMappedDocument mapped,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _singleWindowDocumentPersistenceService.UpsertAgentConsignmentDocumentAsync(
                    source,
                    mapped,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Persisting agent consignment document failed for invoice {InvoiceId}", source.Invoice?.Id);
                return 0;
            }
        }
    }
}
