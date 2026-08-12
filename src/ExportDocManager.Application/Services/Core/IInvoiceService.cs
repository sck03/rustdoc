using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;

namespace ExportDocManager.Services.Core
{
    public enum SaveFailureKind
    {
        None,
        Validation,
        Conflict,
        Forbidden,
        Infrastructure,
        Unexpected
    }

    public class SaveResult
    {
        public bool Success { get; set; }
        public bool IsUpdate { get; set; }
        public string? ErrorMessage { get; set; }
        public Invoice? SavedInvoice { get; set; }
        public SaveFailureKind FailureKind { get; set; }
    }

    public sealed record InvoiceStatusTransitionRequest(
        int InvoiceId,
        string TargetStatus,
        byte[] ExpectedRowVersion,
        string Note);

    public interface IInvoiceService
    {
        Task<SaveResult> SaveInvoiceWithAutoCreationAsync(
            Invoice invoice,
            List<Item>? items,
            Customer? customer,
            Exporter? exporter,
            IReadOnlyList<HsCodeKnowledgeFeedbackInput>? pendingHsFeedback = null,
            CancellationToken cancellationToken = default);
        Task<bool> SaveInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default);
        Task<bool> DeleteInvoiceAsync(int id, CancellationToken cancellationToken = default);
        Task<Invoice?> GetInvoiceByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Invoice?> GetInvoiceByInvoiceNoAndTypeAsync(
            string companyScope,
            string invoiceNo,
            string type,
            CancellationToken cancellationToken = default);
        Task<bool> InvoiceNoExistsAsync(
            string companyScope,
            string invoiceNo,
            CancellationToken cancellationToken = default);
        Task<Invoice?> CopyInvoiceAsync(
            int originalId,
            string newInvoiceNo,
            InvoiceCloneOptions? options = null,
            CancellationToken cancellationToken = default);
        Task<Invoice?> CopyInvoiceAsTypeAsync(
            int originalId,
            string targetType,
            InvoiceCloneOptions? options = null,
            CancellationToken cancellationToken = default);
        Task<Invoice?> TransitionInvoiceStatusAsync(
            InvoiceStatusTransitionRequest request,
            CancellationToken cancellationToken = default);
        Task<Invoice?> UnverifyInvoiceAsync(
            int id,
            byte[] expectedRowVersion,
            string note,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<InvoiceStatusHistory>> ListInvoiceStatusHistoryAsync(
            int invoiceId,
            CancellationToken cancellationToken = default);
        Task<Invoice?> GetLatestInvoiceByPartiesAsync(
            int? customerId,
            int? exporterId,
            CancellationToken cancellationToken = default);
        Task<Invoice?> GetLastInvoiceAsync(CancellationToken cancellationToken = default);
    }
}
