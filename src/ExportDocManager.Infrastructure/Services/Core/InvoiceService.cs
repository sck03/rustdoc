using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService : IInvoiceService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IItemService _itemService;
        private readonly IInvoicePartyResolver _invoicePartyResolver;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public InvoiceService(
            IDbContextFactory<AppDbContext> contextFactory,
            IItemService itemService,
            IInvoicePartyResolver invoicePartyResolver,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _itemService = itemService ?? throw new ArgumentNullException(nameof(itemService));
            _invoicePartyResolver = invoicePartyResolver ?? throw new ArgumentNullException(nameof(invoicePartyResolver));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<SaveResult> SaveInvoiceWithAutoCreationAsync(
            Invoice invoice,
            List<Item> items,
            Customer customer,
            Exporter exporter)
        {
            var result = new SaveResult();

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        if (customer != null)
                        {
                            var customerName = string.IsNullOrWhiteSpace(customer.CustomerNameEN)
                                ? invoice.CustomerNameEN
                                : customer.CustomerNameEN;
                            customer.CustomerNameEN = customerName;

                            invoice.CustomerId = await _invoicePartyResolver.ResolveCustomerIdAsync(
                                context,
                                customer,
                                customerName);

                            if (invoice.CustomerId == 0)
                            {
                                throw new InvalidOperationException("保存或获取客户信息失败");
                            }
                        }

                        if (exporter != null)
                        {
                            var exporterName = string.IsNullOrWhiteSpace(exporter.ExporterNameEN)
                                ? invoice.ExporterNameEN
                                : exporter.ExporterNameEN;
                            exporter.ExporterNameEN = exporterName;

                            invoice.ExporterId = await _invoicePartyResolver.ResolveExporterIdAsync(
                                context,
                                exporter,
                                exporterName,
                                invoice.ExporterNameCN);

                            if (invoice.ExporterId == 0)
                            {
                                throw new InvalidOperationException("保存或获取出口商信息失败");
                            }
                        }

                        invoice.Items = items ?? invoice.Items ?? new List<Item>();

                        if (invoice.Id == 0)
                        {
                            invoice.Id = await _businessDataAccessScope
                                .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                                .Where(item => item.InvoiceNo == invoice.InvoiceNo && item.Type == invoice.Type)
                                .Select(item => item.Id)
                                .FirstOrDefaultAsync();
                        }

                        var saveResult = new SaveResult
                        {
                            IsUpdate = invoice.Id != 0
                        };
                        await SaveInvoiceCoreAsync(context, invoice);

                        saveResult.SavedInvoice = invoice;
                        saveResult.Success = true;
                        return saveResult;
                    });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Log.Error(ex, "保存发票流程失败");
                result.ErrorMessage = "保存失败: 该发票数据已被其他用户修改，请刷新后重试。";
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存发票流程失败");
                result.ErrorMessage = $"保存失败: {ex.Message}";
                return result;
            }
        }

        public async Task<bool> SaveInvoiceAsync(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            try
            {
                await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        await SaveInvoiceCoreAsync(context, invoice);
                    });
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("该发票数据已被其他用户修改，请刷新后重试。");
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? $"\n内部错误: {ex.InnerException.Message}" : "";
                throw new Exception($"保存发票信息失败: {ex.Message}{innerMsg}", ex);
            }
        }
    }
}
