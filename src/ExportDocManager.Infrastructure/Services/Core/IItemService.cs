using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public interface IItemService
    {
        Item GetItemById(int itemId);
        Task<Item> GetItemByIdAsync(int itemId, CancellationToken cancellationToken = default);
        List<Item> GetItemsByInvoiceId(int invoiceId);
        Task<List<Item>> GetItemsByInvoiceIdAsync(int invoiceId, CancellationToken cancellationToken = default);
        Task<List<Item>> GetItemsByInvoiceIdsAsync(
            IEnumerable<int> invoiceIds,
            CancellationToken cancellationToken = default);
        Task<bool> SaveItemsAsync(
            int invoiceId,
            List<Item> items,
            CancellationToken cancellationToken = default);
        Task<bool> SaveItemsAsync(
            AppDbContext context,
            int invoiceId,
            List<Item> items,
            CancellationToken cancellationToken = default);
    }
}
