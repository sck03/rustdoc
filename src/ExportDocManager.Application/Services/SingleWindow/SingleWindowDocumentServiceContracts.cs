using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooDocumentService
    {
        Task<CustomsCooDocument> GetOrCreateAsync(int invoiceId, CancellationToken cancellationToken = default);

        Task<CustomsCooDocument> BuildDefaultsAsync(int invoiceId, CancellationToken cancellationToken = default);

        Task<int> SaveAsync(CustomsCooDocument document, CancellationToken cancellationToken = default);
    }

    public interface IAgentConsignmentDocumentService
    {
        Task<AgentConsignmentDocument> GetOrCreateAsync(int invoiceId, CancellationToken cancellationToken = default);

        Task<AgentConsignmentDocument> BuildDefaultsAsync(int invoiceId, CancellationToken cancellationToken = default);

        Task<int> SaveAsync(AgentConsignmentDocument document, CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowDocumentPersistenceService
    {
        Task<int> UpsertCustomsCooDocumentAsync(
            CooSourceSnapshot snapshot,
            CooMappedDocument document,
            CancellationToken cancellationToken = default);

        Task<int> UpsertAgentConsignmentDocumentAsync(
            AcdSourceSnapshot snapshot,
            AcdMappedDocument document,
            CancellationToken cancellationToken = default);
    }
}
