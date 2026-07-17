using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService
    {
        public async Task<Invoice> CopyInvoiceAsync(int originalId, string newInvoiceNo, InvoiceCloneOptions options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(newInvoiceNo);

            try
            {
                var cloneOptions = options ?? new InvoiceCloneOptions();
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        var originalInvoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .FirstOrDefaultAsync(x => x.Id == originalId);
                        if (originalInvoice == null)
                        {
                            return null;
                        }

                        var newInvoice = CreateInvoiceClone(originalInvoice, newInvoiceNo, cloneOptions);
                        newInvoice.OwnerUserId = null;
                        _businessDataAccessScope.ApplyOwner(newInvoice);
                        if (cloneOptions.CopyItems)
                        {
                            newInvoice.Items = await CreateItemClonesAsync(context, originalId, cloneOptions);
                            if (cloneOptions.ClearAmounts)
                            {
                                newInvoice.CalculateTotals();
                            }
                        }

                        await context.Invoices.AddAsync(newInvoice);
                        await context.SaveChangesAsync();

                        return newInvoice;
                    });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "";
                Log.Error(ex, $"复制发票失败: {ex.Message} {innerMsg}");
                throw new Exception($"复制发票失败: {ex.Message} \n详细错误: {innerMsg}", ex);
            }
        }

        public async Task<Invoice> CopyInvoiceAsTypeAsync(int originalId, string targetType, InvoiceCloneOptions options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

            var normalizedTargetType = targetType.Trim();

            try
            {
                var cloneOptions = options ?? new InvoiceCloneOptions();
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        var originalInvoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .FirstOrDefaultAsync(x => x.Id == originalId);
                        if (originalInvoice == null)
                        {
                            return null;
                        }

                        if (string.Equals(originalInvoice.Type?.Trim(), normalizedTargetType, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("目标发票类型必须与源发票类型不同。");
                        }

                        var targetExists = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .AnyAsync(x => x.InvoiceNo == originalInvoice.InvoiceNo && x.Type == normalizedTargetType);
                        if (targetExists)
                        {
                            throw new InvalidOperationException($"同一发票号的{normalizedTargetType}已存在，未覆盖。");
                        }

                        var newInvoice = CreateInvoiceClone(originalInvoice, originalInvoice.InvoiceNo, cloneOptions);
                        newInvoice.Type = normalizedTargetType;
                        newInvoice.OwnerUserId = null;
                        _businessDataAccessScope.ApplyOwner(newInvoice);
                        if (cloneOptions.CopyItems)
                        {
                            newInvoice.Items = await CreateItemClonesAsync(context, originalId, cloneOptions);
                            if (cloneOptions.ClearAmounts)
                            {
                                newInvoice.CalculateTotals();
                            }
                        }

                        await context.Invoices.AddAsync(newInvoice);
                        await context.SaveChangesAsync();

                        return newInvoice;
                    });
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "";
                Log.Error(ex, $"生成另一发票类型失败: {ex.Message} {innerMsg}");
                throw new Exception($"生成另一发票类型失败: {ex.Message} \n详细错误: {innerMsg}", ex);
            }
        }

        private static Invoice CreateInvoiceClone(
            Invoice originalInvoice,
            string newInvoiceNo,
            InvoiceCloneOptions options)
        {
            var newInvoice = options.CopyHeader
                ? originalInvoice.CloneHeader()
                : new Invoice();

            newInvoice.Id = 0;
            newInvoice.InvoiceNo = newInvoiceNo;
            newInvoice.RowVersion = null;
            newInvoice.Items = new List<Item>();

            if (options.ResetDates || !options.CopyHeader)
            {
                var today = DateTime.Now;
                newInvoice.InvoiceDate = today;
                newInvoice.ShipmentDate = today;
            }

            if (options.ResetStatus)
            {
                newInvoice.Status = InvoiceStatusCatalog.Draft;
            }

            if (options.ClearAmounts)
            {
                ClearInvoiceAmounts(newInvoice);
            }

            return newInvoice;
        }

        private static async Task<List<Item>> CreateItemClonesAsync(
            DbContext context,
            int originalInvoiceId,
            InvoiceCloneOptions options)
        {
            var originalItems = await context.Set<Item>()
                .AsNoTracking()
                .Where(x => x.InvoiceId == originalInvoiceId)
                .OrderBy(x => x.Id)
                .ToListAsync();

            return originalItems
                .Select(item => CreateItemClone(item, options))
                .ToList();
        }

        private static Item CreateItemClone(Item item, InvoiceCloneOptions options)
        {
            var newItem = item.Clone();
            newItem.Id = 0;
            newItem.InvoiceId = 0;

            if (options.ClearAmounts)
            {
                ClearItemAmounts(newItem);
            }

            return newItem;
        }

        private static void ClearInvoiceAmounts(Invoice invoice)
        {
            invoice.TotalCartons = 0;
            invoice.TotalQuantity = 0;
            invoice.TotalGrossWeight = 0;
            invoice.TotalNetWeight = 0;
            invoice.TotalVolume = 0;
            invoice.TotalAmount = 0;
            invoice.TotalPurchaseAmount = 0;
            invoice.TotalTaxRefundAmount = 0;
            invoice.TotalProfit = 0;
        }

        private static void ClearItemAmounts(Item item)
        {
            item.UnitPrice = 0;
            item.TotalPrice = 0;
            item.PurchasePrice = 0;
            item.PurchaseTotal = 0;
            item.TaxRebateRate = 0;
        }
    }
}
