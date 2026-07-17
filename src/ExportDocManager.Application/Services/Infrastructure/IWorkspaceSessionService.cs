using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IWorkspaceSessionService
    {
        Invoice CurrentInvoice { get; }

        int ActiveInvoiceId { get; }

        void UpdateCurrentInvoice(Invoice invoice);

        void RegisterSaveHandler(Func<Task<bool>> saveHandler);

        void ClearSaveHandler(Func<Task<bool>> saveHandler = null);

        Task<bool> RequestSaveAsync();
    }
}
