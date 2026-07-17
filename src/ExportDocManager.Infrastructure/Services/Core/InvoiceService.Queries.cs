using System;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService
    {
        public async Task<Invoice> GetLatestInvoiceByPartiesAsync(int? customerId, int? exporterId)
        {
            if ((!customerId.HasValue || customerId.Value <= 0) &&
                (!exporterId.HasValue || exporterId.Value <= 0))
            {
                return null;
            }

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var query = _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking());

                if (customerId.HasValue && customerId.Value > 0)
                {
                    query = query.Where(x => x.CustomerId == customerId.Value);
                }

                if (exporterId.HasValue && exporterId.Value > 0)
                {
                    query = query.Where(x => x.ExporterId == exporterId.Value);
                }

                return await query
                    .OrderByDescending(x => x.InvoiceDate)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取默认单证参数失败: {ex.Message}", ex);
            }
        }

        public async Task<Invoice> GetInvoiceByIdAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var invoice = await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .Include(i => i.Items)
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (invoice == null)
                {
                    return null;
                }

                await PopulateMissingInvoiceSnapshotsAsync(context, invoice);
                return invoice;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取发票详情失败: {ex.Message}", ex);
            }
        }

        public async Task<Invoice> GetInvoiceByInvoiceNoAndTypeAsync(string invoiceNo, string type)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .FirstOrDefaultAsync(x => x.InvoiceNo == invoiceNo && x.Type == type);
            }
            catch (Exception ex)
            {
                throw new Exception($"根据发票号和类型获取发票失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> InvoiceNoExistsAsync(string invoiceNo)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .AnyAsync(x => x.InvoiceNo == invoiceNo);
            }
            catch (Exception ex)
            {
                throw new Exception($"检查发票号是否存在失败: {ex.Message}", ex);
            }
        }

        public async Task<Invoice> GetLastInvoiceAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var invoice = await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .OrderByDescending(i => i.Id)
                    .FirstOrDefaultAsync();

                if (invoice != null)
                {
                    invoice.Items = await context.Items
                        .AsNoTracking()
                        .Where(x => x.InvoiceId == invoice.Id)
                        .ToListAsync();
                }

                return invoice;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取最新发票失败: {ex.Message}", ex);
            }
        }
    }
}
