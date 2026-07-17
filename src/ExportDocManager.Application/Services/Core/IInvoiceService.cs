using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public class SaveResult
    {
        public bool Success { get; set; }
        public bool IsUpdate { get; set; }
        public string ErrorMessage { get; set; }
        public Invoice SavedInvoice { get; set; }
    }

    public interface IInvoiceService
    {
        Task<SaveResult> SaveInvoiceWithAutoCreationAsync(Invoice invoice, List<Item> items, Customer customer, Exporter exporter);
        Task<bool> SaveInvoiceAsync(Invoice invoice);
        Task<bool> DeleteInvoiceAsync(int id);
        Task<Invoice> GetInvoiceByIdAsync(int id);
        Task<Invoice> GetInvoiceByInvoiceNoAndTypeAsync(string invoiceNo, string type);
        Task<bool> InvoiceNoExistsAsync(string invoiceNo);
        Task<Invoice> CopyInvoiceAsync(int originalId, string newInvoiceNo, InvoiceCloneOptions options = null);
        Task<Invoice> CopyInvoiceAsTypeAsync(int originalId, string targetType, InvoiceCloneOptions options = null);
        Task<Invoice> UnverifyInvoiceAsync(int id);
        Task<Invoice> GetLatestInvoiceByPartiesAsync(int? customerId, int? exporterId);
        Task<Invoice> GetLastInvoiceAsync();
    }
}
