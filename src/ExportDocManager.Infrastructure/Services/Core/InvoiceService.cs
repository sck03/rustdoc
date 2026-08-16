using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService : IInvoiceService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IItemService _itemService;
        private readonly IInvoicePartyResolver _invoicePartyResolver;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly IBusinessClock _clock;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(
            IDbContextFactory<AppDbContext> contextFactory,
            IItemService itemService,
            IInvoicePartyResolver invoicePartyResolver,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope? businessDataAccessScope = null,
            IBusinessClock? clock = null,
            ILogger<InvoiceService>? logger = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _itemService = itemService ?? throw new ArgumentNullException(nameof(itemService));
            _invoicePartyResolver = invoicePartyResolver ?? throw new ArgumentNullException(nameof(invoicePartyResolver));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
            _clock = clock ?? BusinessClock.CreateSystem();
            _logger = logger ?? NullLogger<InvoiceService>.Instance;
        }

        public async Task<SaveResult> SaveInvoiceWithAutoCreationAsync(
            Invoice invoice,
            List<Item>? items,
            Customer? customer,
            Exporter? exporter,
            IReadOnlyList<HsCodeKnowledgeFeedbackInput>? pendingHsFeedback = null,
            CancellationToken cancellationToken = default)
        {
            var result = new SaveResult();

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        if (invoice.CustomerId > 0)
                        {
                            invoice.CustomerId = await _invoicePartyResolver.ResolveCustomerIdAsync(
                                context,
                                new Customer { Id = invoice.CustomerId },
                                invoice.CustomerNameEN,
                                token);
                        }
                        else if (customer != null)
                        {
                            var customerName = string.IsNullOrWhiteSpace(customer.CustomerNameEN)
                                ? invoice.CustomerNameEN
                                : customer.CustomerNameEN;
                            customer.CustomerNameEN = customerName;

                            invoice.CustomerId = await _invoicePartyResolver.ResolveCustomerIdAsync(
                                context,
                                customer,
                                customerName,
                                token);

                            if (invoice.CustomerId == 0)
                            {
                                throw new InfrastructureServiceException("保存或获取客户信息失败");
                            }
                        }

                        if (invoice.ExporterId > 0)
                        {
                            invoice.ExporterId = await _invoicePartyResolver.ResolveExporterIdAsync(
                                context,
                                new Exporter { Id = invoice.ExporterId },
                                invoice.ExporterNameEN,
                                invoice.ExporterNameCN,
                                token);
                        }
                        else if (exporter != null)
                        {
                            var exporterName = string.IsNullOrWhiteSpace(exporter.ExporterNameEN)
                                ? invoice.ExporterNameEN
                                : exporter.ExporterNameEN;
                            exporter.ExporterNameEN = exporterName;

                            invoice.ExporterId = await _invoicePartyResolver.ResolveExporterIdAsync(
                                context,
                                exporter,
                                exporterName,
                                invoice.ExporterNameCN,
                                token);

                            if (invoice.ExporterId == 0)
                            {
                                throw new InfrastructureServiceException("保存或获取出口商信息失败");
                            }
                        }

                        invoice.Items = items ?? invoice.Items ?? new List<Item>();
                        _businessDataAccessScope.ApplyOwner(invoice);

                        var saveResult = new SaveResult
                        {
                            IsUpdate = invoice.Id != 0
                        };
                        await SaveInvoiceCoreAsync(
                            context,
                            invoice,
                            pendingHsFeedback,
                            cancellationToken: token);

                        saveResult.SavedInvoice = invoice;
                        saveResult.Success = true;
                        return saveResult;
                    },
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "保存发票流程失败");
                result.ErrorMessage = "保存失败: 该发票数据已被其他用户修改，请刷新后重试。";
                result.FailureKind = SaveFailureKind.Conflict;
                return result;
            }
            catch (InvoiceValidationException ex)
            {
                result.ErrorMessage = ex.Message;
                result.FailureKind = SaveFailureKind.Validation;
                return result;
            }
            catch (InvoiceConflictException ex)
            {
                result.ErrorMessage = ex.Message;
                result.FailureKind = SaveFailureKind.Conflict;
                return result;
            }
            catch (PermissionDeniedException ex)
            {
                result.ErrorMessage = ex.Message;
                result.FailureKind = SaveFailureKind.Forbidden;
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.ErrorMessage = ex.Message;
                result.FailureKind = SaveFailureKind.Forbidden;
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "保存发票流程失败");
                result.ErrorMessage = $"保存失败: {ex.Message}";
                result.FailureKind = SaveFailureKind.Infrastructure;
                return result;
            }
        }

        public async Task<bool> SaveInvoiceAsync(
            Invoice invoice,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            try
            {
                await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        // This legacy application-service entry point has no request DTO
                        // carrying a concurrency token. Hydrate the token from the current
                        // row before delegating to the strict save path. HTTP callers use
                        // SaveInvoiceWithAutoCreationAsync and must still provide the token.
                        if (invoice.Id > 0 && (invoice.RowVersion == null || invoice.RowVersion.Length == 0))
                        {
                            invoice.RowVersion = await context.Invoices
                                .AsNoTracking()
                                .Where(item => item.Id == invoice.Id)
                                .Select(item => item.RowVersion)
                                .FirstOrDefaultAsync(token);
                        }
                        await SaveInvoiceCoreAsync(
                            context,
                            invoice,
                            pendingHsFeedback: null,
                            requireRowVersion: false,
                            cancellationToken: token);
                    },
                    cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new BusinessConcurrencyException("该发票数据已被其他用户修改，请刷新后重试。", ex);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new PermissionDeniedException("无权限保存该发票。", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票保存服务暂时不可用，请稍后重试。", ex);
            }
        }
    }
}
