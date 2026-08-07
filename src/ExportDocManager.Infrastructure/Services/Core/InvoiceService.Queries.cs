using System;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票默认参数服务暂时不可用，请稍后重试。", ex);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票详情服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Invoice> GetInvoiceByInvoiceNoAndTypeAsync(string companyScope, string invoiceNo, string type)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                string normalizedCompanyScope = companyScope?.Trim() ?? string.Empty;
                string normalizedInvoiceNo = invoiceNo?.Trim() ?? string.Empty;
                string normalizedType = InvoiceTypeCatalog.Normalize(type);
                return await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .FirstOrDefaultAsync(x =>
                        x.CompanyScope == normalizedCompanyScope &&
                        x.InvoiceNo == normalizedInvoiceNo &&
                        x.Type == normalizedType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票编号查询服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<bool> InvoiceNoExistsAsync(string companyScope, string invoiceNo)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                string normalizedCompanyScope = companyScope?.Trim() ?? string.Empty;
                string normalizedInvoiceNo = invoiceNo?.Trim() ?? string.Empty;
                return await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .AnyAsync(x =>
                        x.CompanyScope == normalizedCompanyScope &&
                        x.InvoiceNo == normalizedInvoiceNo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票编号校验服务暂时不可用，请稍后重试。", ex);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("最新发票查询服务暂时不可用，请稍后重试。", ex);
            }
        }
    }
}
