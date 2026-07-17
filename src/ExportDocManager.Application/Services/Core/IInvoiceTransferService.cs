using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Core
{
    public interface IInvoiceTransferService
    {
        Task<string> ExportAsync(int invoiceId, string savePath, CancellationToken cancellationToken = default);

        Task<InvoiceTransferReadResult> ReadPackageAsync(string filePath, CancellationToken cancellationToken = default);

        Task<InvoiceTransferPreview> PreviewAsync(InvoiceTransferPackage pkg, CancellationToken cancellationToken = default);

        Task<InvoiceImportResult> ImportAsync(
            InvoiceTransferPackage pkg,
            InvoiceImportConflictAction action,
            string newInvoiceNo = null,
            CancellationToken cancellationToken = default);
    }
}
