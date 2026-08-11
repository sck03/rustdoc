using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using ExportDocManager.Services.Errors;
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

        public async Task<Item> GetItemByIdAsync(
            int itemId,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
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

        public async Task<List<Item>> GetItemsByInvoiceIdAsync(
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Items
                .AsNoTracking()
                .Where(i => i.InvoiceId == invoiceId)
                .OrderBy(i => i.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Item>> GetItemsByInvoiceIdsAsync(
            IEnumerable<int> invoiceIds,
            CancellationToken cancellationToken = default)
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

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Items
                .AsNoTracking()
                .Where(i => normalizedInvoiceIds.Contains(i.InvoiceId))
                .OrderBy(i => i.InvoiceId)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> SaveItemsAsync(
            int invoiceId,
            List<Item> items,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await SaveItemsAsync(context, invoiceId, items, cancellationToken);
        }

        public async Task<bool> SaveItemsAsync(
            AppDbContext context,
            int invoiceId,
            List<Item> items,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    .ToListAsync(cancellationToken);

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
                        await context.Items.AddAsync(item, cancellationToken);
                    }
                    else
                    {
                        context.Attach(item);
                        context.Entry(item).State = EntityState.Modified;
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("商品明细已被其他用户修改，请刷新后重试。", ex);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("商品明细保存服务暂时不可用，请稍后重试。", ex);
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
