using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public class ItemService : IItemService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public ItemService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public Item GetItemById(int itemId)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Items.AsNoTracking().FirstOrDefault(i => i.Id == itemId);
        }

        public async Task<Item> GetItemByIdAsync(int itemId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
        }

        public List<Item> GetItemsByInvoiceId(int invoiceId)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Items
                .AsNoTracking()
                .Where(i => i.InvoiceId == invoiceId)
                .OrderBy(i => i.Id)
                .ToList();
        }

        public async Task<List<Item>> GetItemsByInvoiceIdAsync(int invoiceId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Items
                .AsNoTracking()
                .Where(i => i.InvoiceId == invoiceId)
                .OrderBy(i => i.Id)
                .ToListAsync();
        }

        public async Task<List<Item>> GetItemsByInvoiceIdsAsync(IEnumerable<int> invoiceIds)
        {
            ArgumentNullException.ThrowIfNull(invoiceIds);

            var normalizedInvoiceIds = invoiceIds
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
            if (normalizedInvoiceIds.Length == 0)
            {
                return [];
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Items
                .AsNoTracking()
                .Where(i => normalizedInvoiceIds.Contains(i.InvoiceId))
                .OrderBy(i => i.InvoiceId)
                .ThenBy(i => i.Id)
                .ToListAsync();
        }

        public async Task<bool> SaveItemsAsync(int invoiceId, List<Item> items)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await SaveItemsAsync(context, invoiceId, items);
        }

        public async Task<bool> SaveItemsAsync(AppDbContext context, int invoiceId, List<Item> items)
        {
            try
            {
                items ??= new List<Item>();

                var seenIds = new HashSet<int>();
                var normalizedItems = new List<Item>(items.Count);
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Id > 0 && !seenIds.Add(item.Id))
                    {
                        item.Id = 0;
                    }

                    normalizedItems.Add(item);
                }

                var existingIdSet = await context.Items
                    .Where(x => x.InvoiceId == invoiceId)
                    .Select(x => x.Id)
                    .ToListAsync();

                var existingIdHashSet = existingIdSet.ToHashSet();

                var inputIdSet = normalizedItems
                    .Where(x => x.Id > 0)
                    .Select(x => x.Id)
                    .ToHashSet();

                var toDeleteIds = existingIdHashSet
                    .Where(id => !inputIdSet.Contains(id))
                    .ToList();

                foreach (var id in toDeleteIds)
                {
                    context.Items.Remove(new Item { Id = id });
                }

                foreach (var item in normalizedItems)
                {
                    if (item.Id > 0 && !existingIdHashSet.Contains(item.Id))
                    {
                        item.Id = 0;
                    }

                    NormalizeItem(item);
                    item.InvoiceId = invoiceId;
                    if (item.Id == 0)
                    {
                        await context.Items.AddAsync(item);
                    }
                    else
                    {
                        context.Attach(item);
                        context.Entry(item).State = EntityState.Modified;
                    }
                }

                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"保存商品明细失败: {ex.Message}", ex);
            }
        }

        private static void NormalizeItem(Item item)
        {
            item.PoNumber = TextSearchHelper.NormalizeValue(item.PoNumber);
            item.StyleNo = TextSearchHelper.NormalizeValue(item.StyleNo);
            item.StyleName = TextSearchHelper.NormalizeValue(item.StyleName);
            item.FabricComposition = TextSearchHelper.NormalizeValue(item.FabricComposition);
            item.StyleNameCN = TextSearchHelper.NormalizeValue(item.StyleNameCN);
            item.Brand = TextSearchHelper.NormalizeValue(item.Brand);
            item.HSCode = TextSearchHelper.NormalizeValue(item.HSCode);
            item.Origin = TextSearchHelper.NormalizeValue(item.Origin);
            item.UnitEN = TextSearchHelper.NormalizeValue(item.UnitEN);
            item.UnitCN = TextSearchHelper.NormalizeValue(item.UnitCN);
            item.CtnUnitEN = TextSearchHelper.NormalizeValue(item.CtnUnitEN);
            item.CtnUnitCN = TextSearchHelper.NormalizeValue(item.CtnUnitCN);
            item.Spare1 = TextSearchHelper.NormalizeValue(item.Spare1);
            item.Spare2 = TextSearchHelper.NormalizeValue(item.Spare2);
            item.Spare3 = TextSearchHelper.NormalizeValue(item.Spare3);
            item.CustomFieldsJson = TextSearchHelper.NormalizeValue(item.CustomFieldsJson);
        }
    }
}
