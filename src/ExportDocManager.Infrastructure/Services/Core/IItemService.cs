using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public interface IItemService
    {
        Item GetItemById(int itemId);
        Task<Item> GetItemByIdAsync(int itemId);
        List<Item> GetItemsByInvoiceId(int invoiceId);
        Task<List<Item>> GetItemsByInvoiceIdAsync(int invoiceId);
        Task<List<Item>> GetItemsByInvoiceIdsAsync(IEnumerable<int> invoiceIds);
        Task<bool> SaveItemsAsync(int invoiceId, List<Item> items);
        Task<bool> SaveItemsAsync(AppDbContext context, int invoiceId, List<Item> items);
    }
}
