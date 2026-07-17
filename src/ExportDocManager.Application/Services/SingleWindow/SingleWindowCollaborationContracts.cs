using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowCollaborationDataSource
    {
        Task<SingleWindowCollaborationPageResult> QueryPageAsync(
            SingleWindowCollaborationPageQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SingleWindowOperationTicketRow>> QueryTicketsAsync(
            SingleWindowCollaborationQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SingleWindowWorkstationRow>> QueryWorkstationsAsync(
            CancellationToken cancellationToken = default);
    }

    public interface ISingleWindowWorkstationRegistryService
    {
        Task EnsureCurrentWorkstationAsync(CancellationToken cancellationToken = default);
    }
}
