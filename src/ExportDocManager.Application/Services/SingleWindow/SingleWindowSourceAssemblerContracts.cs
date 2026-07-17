using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooSourceAssembler
    {
        Task<CooSourceSnapshot> BuildAsync(int invoiceId, CancellationToken cancellationToken = default);
    }

    public interface IAgentConsignmentSourceAssembler
    {
        Task<AcdSourceSnapshot> BuildAsync(int invoiceId, CancellationToken cancellationToken = default);
    }
}
