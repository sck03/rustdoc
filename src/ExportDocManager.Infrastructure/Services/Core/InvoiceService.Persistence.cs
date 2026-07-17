using System;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService
    {
        public async Task<bool> DeleteInvoiceAsync(int id)
        {
            if (id <= 0)
            {
                return false;
            }

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        var invoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(
                                context.Invoices.Include(item => item.Items))
                            .FirstOrDefaultAsync(item => item.Id == id);
                        if (invoice == null)
                        {
                            return false;
                        }

                        await TrackSingleWindowWorkspaceDeletionAsync(context, id);

                        context.Invoices.Remove(invoice);
                        await context.SaveChangesAsync();
                        return true;
                    });
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("删除失败: 该发票数据已被其他用户修改或删除，请刷新后重试。");
            }
            catch (Exception ex)
            {
                throw new Exception($"删除发票失败: {ex.Message}", ex);
            }
        }

        public async Task<Invoice> UnverifyInvoiceAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, _) =>
                    {
                        var invoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.Include(item => item.Items))
                            .FirstOrDefaultAsync(item => item.Id == id);
                        if (invoice == null)
                        {
                            return null;
                        }

                        if (!InvoiceStatusCatalog.CanUnverify(invoice.Status))
                        {
                            throw new InvalidOperationException("当前发票不是已锁定状态，无需反审核。");
                        }

                        invoice.Status = InvoiceStatusCatalog.Draft;
                        await PopulateMissingInvoiceSnapshotsAsync(context, invoice);
                        await context.SaveChangesAsync();

                        return invoice;
                    });
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("反审核失败: 该发票数据已被其他用户修改或删除，请刷新后重试。");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"反审核发票失败: {ex.Message}", ex);
            }
        }

        private async Task SaveInvoiceCoreAsync(AppDbContext context, Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(invoice);

            NormalizeInvoiceDates(invoice);
            var items = invoice.Items;
            invoice.Items = null;

            try
            {
                await PopulateMissingInvoiceSnapshotsAsync(context, invoice);
                _businessDataAccessScope.ApplyOwner(invoice);

                if (invoice.Id > 0)
                {
                    if (!await _businessDataAccessScope.CanAccessInvoiceAsync(
                            context,
                            invoice.Id).ConfigureAwait(false))
                    {
                        throw new UnauthorizedAccessException("无权限修改该发票。");
                    }

                    var existingStatus = await _businessDataAccessScope
                        .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                        .Where(item => item.Id == invoice.Id)
                        .Select(item => item.Status)
                        .FirstOrDefaultAsync();
                    if (!InvoiceStatusCatalog.IsEditable(existingStatus))
                    {
                        throw new InvalidOperationException("当前发票已锁定，请先反审核后再编辑。");
                    }

                    context.Invoices.Update(invoice);
                }
                else
                {
                    await context.Invoices.AddAsync(invoice);
                }

                await context.SaveChangesAsync();

                if (items != null)
                {
                    await _itemService.SaveItemsAsync(context, invoice.Id, items);
                }
            }
            finally
            {
                invoice.Items = items;
            }
        }

        private static async Task PopulateMissingInvoiceSnapshotsAsync(AppDbContext context, Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(invoice);

            if (invoice.CustomerId > 0 && HasMissingCustomerSnapshot(invoice))
            {
                var customer = await context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.CustomerId);

                if (customer != null)
                {
                    invoice.CustomerNameEN = PreferExistingValue(invoice.CustomerNameEN, customer.CustomerNameEN);
                    invoice.CustomerAddressEN = PreferExistingValue(invoice.CustomerAddressEN, customer.AddressEN);
                    invoice.NotifyPartyName = PreferExistingValue(invoice.NotifyPartyName, customer.NotifyPartyName);
                    invoice.NotifyPartyAddress = PreferExistingValue(invoice.NotifyPartyAddress, customer.NotifyPartyAddress);
                }
            }

            if (invoice.ExporterId > 0 && HasMissingExporterSnapshot(invoice))
            {
                var exporter = await context.Exporters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.ExporterId);

                if (exporter != null)
                {
                    invoice.ExporterNameEN = PreferExistingValue(invoice.ExporterNameEN, exporter.ExporterNameEN);
                    invoice.ExporterNameCN = PreferExistingValue(invoice.ExporterNameCN, exporter.ExporterNameCN);
                    invoice.ExporterAddressEN = PreferExistingValue(invoice.ExporterAddressEN, exporter.AddressEN);
                    invoice.ExporterAddressCN = PreferExistingValue(invoice.ExporterAddressCN, exporter.AddressCN);
                    invoice.ExporterCreditCode = PreferExistingValue(invoice.ExporterCreditCode, exporter.CreditCode);
                    invoice.ExporterCustomsCode = PreferExistingValue(invoice.ExporterCustomsCode, exporter.CustomsCode);
                    invoice.BankName = PreferExistingValue(invoice.BankName, exporter.BankName);
                    invoice.BankAccount = PreferExistingValue(invoice.BankAccount, exporter.BankAccount);
                    invoice.SwiftCode = PreferExistingValue(invoice.SwiftCode, exporter.SwiftCode);
                }
            }
        }

        private static bool HasMissingCustomerSnapshot(Invoice invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.CustomerNameEN) ||
                   string.IsNullOrWhiteSpace(invoice.CustomerAddressEN) ||
                   string.IsNullOrWhiteSpace(invoice.NotifyPartyName) ||
                   string.IsNullOrWhiteSpace(invoice.NotifyPartyAddress);
        }

        private static bool HasMissingExporterSnapshot(Invoice invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.ExporterNameEN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterNameCN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterAddressEN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterAddressCN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterCreditCode) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterCustomsCode) ||
                   string.IsNullOrWhiteSpace(invoice.BankName) ||
                   string.IsNullOrWhiteSpace(invoice.BankAccount) ||
                   string.IsNullOrWhiteSpace(invoice.SwiftCode);
        }

        private static string PreferExistingValue(string currentValue, string fallbackValue)
        {
            return string.IsNullOrWhiteSpace(currentValue) ? fallbackValue : currentValue;
        }

        private static void NormalizeInvoiceDates(Invoice invoice)
        {
            invoice.InvoiceDate = DateTimeValueHelper.NormalizeBusinessDate(invoice.InvoiceDate);
            invoice.ShipmentDate = DateTimeValueHelper.NormalizeBusinessDate(invoice.ShipmentDate, invoice.InvoiceDate);
        }

        private static async Task TrackSingleWindowWorkspaceDeletionAsync(AppDbContext context, int invoiceId)
        {
            ArgumentNullException.ThrowIfNull(context);

            var customsCooDocument = await context.CustomsCooDocuments
                .FirstOrDefaultAsync(document => document.SourceInvoiceId == invoiceId);
            if (customsCooDocument != null)
            {
                context.CustomsCooDocuments.Remove(customsCooDocument);
            }

            var agentConsignmentDocument = await context.AgentConsignmentDocuments
                .FirstOrDefaultAsync(document => document.SourceInvoiceId == invoiceId);
            if (agentConsignmentDocument != null)
            {
                context.AgentConsignmentDocuments.Remove(agentConsignmentDocument);
            }

            var operationTickets = await context.SwOperationTickets
                .Where(ticket => ticket.SourceInvoiceId == invoiceId)
                .ToListAsync();
            if (operationTickets.Count > 0)
            {
                context.SwOperationTickets.RemoveRange(operationTickets);
            }

            var handoffPackageRecords = await context.SwHandoffPackageRecords
                .Where(record => record.SourceInvoiceId == invoiceId)
                .ToListAsync();
            if (handoffPackageRecords.Count > 0)
            {
                context.SwHandoffPackageRecords.RemoveRange(handoffPackageRecords);
            }

            var submissionBatches = await context.SwSubmissionBatches
                .Where(batch => batch.SourceInvoiceId == invoiceId)
                .ToListAsync();
            if (submissionBatches.Count > 0)
            {
                context.SwSubmissionBatches.RemoveRange(submissionBatches);
            }
        }
    }
}
