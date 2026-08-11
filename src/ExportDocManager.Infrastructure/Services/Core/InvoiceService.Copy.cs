using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService
    {
        public async Task<Invoice> CopyInvoiceAsync(
            int originalId,
            string newInvoiceNo,
            InvoiceCloneOptions options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(newInvoiceNo);

            try
            {
                var cloneOptions = options ?? new InvoiceCloneOptions();
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var originalInvoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .FirstOrDefaultAsync(x => x.Id == originalId, token);
                        if (originalInvoice == null)
                        {
                            return null;
                        }

                        var newInvoice = CreateInvoiceClone(originalInvoice, newInvoiceNo, cloneOptions);
                        newInvoice.OwnerUserId = null;
                        _businessDataAccessScope.ApplyOwner(newInvoice);
                        if (cloneOptions.CopyItems)
                        {
                            newInvoice.Items = await CreateItemClonesAsync(
                                context,
                                originalId,
                                cloneOptions,
                                token);
                            if (cloneOptions.ClearAmounts)
                            {
                                newInvoice.CalculateTotals();
                            }
                        }

                        await context.Invoices.AddAsync(newInvoice, token);
                        await context.SaveChangesAsync(token);

                        return newInvoice;
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "复制发票流程失败");
                throw new InfrastructureServiceException("发票复制服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Invoice> CopyInvoiceAsTypeAsync(
            int originalId,
            string targetType,
            InvoiceCloneOptions options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

            var normalizedTargetType = targetType.Trim();

            try
            {
                var cloneOptions = options ?? new InvoiceCloneOptions();
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var originalInvoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .FirstOrDefaultAsync(x => x.Id == originalId, token);
                        if (originalInvoice == null)
                        {
                            return null;
                        }

                        if (string.Equals(originalInvoice.Type?.Trim(), normalizedTargetType, StringComparison.Ordinal))
                        {
                            throw new ServiceValidationException("目标发票类型必须与源发票类型不同。");
                        }

                        var newInvoice = CreateInvoiceClone(originalInvoice, originalInvoice.InvoiceNo, cloneOptions);
                        newInvoice.Type = normalizedTargetType;
                        // The actual/customs pair is one logical invoice and
                        // must stay in the source company's ownership scope,
                        // even when an administrator creates the other type.
                        newInvoice.OwnerUserId = originalInvoice.OwnerUserId;
                        newInvoice.DepartmentId = originalInvoice.DepartmentId;
                        newInvoice.CompanyScope = originalInvoice.CompanyScope;

                        var targetExists = await context.Invoices
                            .AsNoTracking()
                            .AnyAsync(x => x.CompanyScope == newInvoice.CompanyScope &&
                                x.InvoiceNo == newInvoice.InvoiceNo &&
                                x.Type == newInvoice.Type,
                                token);
                        if (targetExists)
                        {
                            throw new ResourceConflictException($"同一发票号的{normalizedTargetType}已存在，未覆盖。");
                        }

                        if (cloneOptions.CopyItems)
                        {
                            newInvoice.Items = await CreateItemClonesAsync(
                                context,
                                originalId,
                                cloneOptions,
                                token);
                            if (cloneOptions.ClearAmounts)
                            {
                                newInvoice.CalculateTotals();
                            }
                        }

                        await context.Invoices.AddAsync(newInvoice, token);
                        await context.SaveChangesAsync(token);

                        return newInvoice;
                    },
                    cancellationToken);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "生成另一发票类型流程失败");
                throw new InfrastructureServiceException("发票类型转换服务暂时不可用，请稍后重试。", ex);
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

            // A clone is a new business document and must always re-enter the
            // auditable workflow from Draft, regardless of the source status.
            newInvoice.Status = InvoiceStatusCatalog.Draft;

            if (options.ClearAmounts)
            {
                ClearInvoiceAmounts(newInvoice);
            }

            return newInvoice;
        }

        private static async Task<List<Item>> CreateItemClonesAsync(
            DbContext context,
            int originalInvoiceId,
            InvoiceCloneOptions options,
            CancellationToken cancellationToken)
        {
            var originalItems = await context.Set<Item>()
                .AsNoTracking()
                .Where(x => x.InvoiceId == originalInvoiceId)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

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
            // 清空金额后，复制出的空白行应回到“单价驱动”默认模式；否则原单据
            // 若曾以行金额为准，用户重新填写数量/单价时会继续误保留旧核算语义。
            item.PriceCalculationMode = ItemPriceCalculationModeCatalog.UnitPriceDriven;
            item.UnitPrice = 0;
            item.TotalPrice = 0;
            item.PurchasePrice = 0;
            item.PurchaseTotal = 0;
            item.TaxRebateRate = 0;
        }
    }
}
