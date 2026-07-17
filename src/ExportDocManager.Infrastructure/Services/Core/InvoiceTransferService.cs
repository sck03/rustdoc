using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public class InvoiceTransferService : IInvoiceTransferService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IInvoicePartyResolver _invoicePartyResolver;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly IAppPathProvider _pathProvider;

        public InvoiceTransferService(
            IDbContextFactory<AppDbContext> contextFactory,
            IInvoicePartyResolver invoicePartyResolver,
            DatabaseConnectionSettings databaseSettings,
            IAppPathProvider pathProvider,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _invoicePartyResolver = invoicePartyResolver ?? throw new ArgumentNullException(nameof(invoicePartyResolver));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<string> ExportAsync(int invoiceId, string savePath, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var package = await BuildExportPackageAsync(context, invoiceId, cancellationToken);
            var json = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = false });
            var checksum = ComputeSha256(Encoding.UTF8.GetBytes(json));

            var tempDir = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "InvoiceTransfer",
                "edpkg");
            var dataJsonPath = Path.Combine(tempDir, "data.json");
            var metaJsonPath = Path.Combine(tempDir, "meta.json");
            var targetPath = PackagePathHelper.NormalizePackagePath(savePath, ".edpkg", nameof(savePath));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dataJsonPath, json, Encoding.UTF8, cancellationToken);
                await File.WriteAllTextAsync(
                    metaJsonPath,
                    JsonSerializer.Serialize(new { checksum }, new JsonSerializerOptions { WriteIndented = false }),
                    Encoding.UTF8,
                    cancellationToken);

                await ZipArchiveHelper.CreateFromFilesAsync(
                    new[]
                    {
                        (SourcePath: dataJsonPath, EntryName: "data.json"),
                        (SourcePath: metaJsonPath, EntryName: "meta.json")
                    },
                    targetPath,
                    cancellationToken);
                return targetPath;
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(tempDir);
            }
        }

        public async Task<InvoiceTransferReadResult> ReadPackageAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("文件不存在", filePath);
            }

            using var archive = ZipFile.OpenRead(filePath);
            var dataEntry = archive.GetEntry("data.json");
            var metaEntry = archive.GetEntry("meta.json");
            if (dataEntry == null || metaEntry == null)
            {
                throw new InvalidDataException("包格式不正确，缺少必要文件");
            }

            string dataJson;
            using (var stream = dataEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                dataJson = await reader.ReadToEndAsync(cancellationToken);
            }

            string metaJson;
            using (var stream = metaEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                metaJson = await reader.ReadToEndAsync(cancellationToken);
            }

            var package = JsonSerializer.Deserialize<InvoiceTransferPackage>(dataJson)
                ?? throw new InvalidDataException("单据包数据无效");
            EnsurePackageValid(package);

            var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
            var checksumValid = false;
            var checksumMessage = string.Empty;
            if (meta != null && meta.TryGetValue("checksum", out var checksum))
            {
                checksumValid = string.Equals(
                    checksum,
                    ComputeSha256(Encoding.UTF8.GetBytes(dataJson)),
                    StringComparison.OrdinalIgnoreCase);
                checksumMessage = checksumValid ? "校验通过" : "校验失败";
            }
            else
            {
                checksumMessage = "缺少校验信息";
            }

            return new InvoiceTransferReadResult
            {
                Package = package,
                ChecksumValid = checksumValid,
                ChecksumMessage = checksumMessage
            };
        }

        public async Task<InvoiceTransferPreview> PreviewAsync(InvoiceTransferPackage pkg, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await BuildPreviewAsync(context, pkg, cancellationToken);
        }

        public async Task<InvoiceImportResult> ImportAsync(InvoiceTransferPackage pkg, InvoiceImportConflictAction action, string newInvoiceNo = null, CancellationToken cancellationToken = default)
        {
            EnsurePackageValid(pkg);

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var preview = await BuildPreviewAsync(context, pkg, token);
                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.Skip)
                    {
                        return new InvoiceImportResult
                        {
                            Success = true,
                            Message = preview.InvoiceMatches ? "目标库中已存在完全相同的单据，已跳过导入。" : "已跳过导入",
                            ActionTaken = action,
                            InvoiceId = preview.ExistingInvoiceId > 0 ? preview.ExistingInvoiceId : null,
                            FinalInvoiceNo = preview.InvoiceNo
                        };
                    }

                    var customerId = await _invoicePartyResolver.ResolveCustomerIdAsync(
                        context,
                        CloneCustomer(pkg.Customer),
                        pkg.Invoice?.CustomerNameEN,
                        token);
                    var exporterId = await _invoicePartyResolver.ResolveExporterIdAsync(
                        context,
                        CloneExporter(pkg.Exporter),
                        pkg.Invoice?.ExporterNameEN,
                        pkg.Invoice?.ExporterNameCN,
                        token);
                    var importItems = CloneItems(pkg.Items);
                    var importInvoice = CloneInvoice(pkg.Invoice);

                    var targetInvoiceNo = importInvoice.InvoiceNo;
                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.NewInvoiceNo)
                    {
                        targetInvoiceNo = await ResolveInvoiceNoAsync(
                            context,
                            importInvoice.InvoiceNo,
                            importInvoice.Type,
                            newInvoiceNo,
                            token);
                    }

                    importInvoice.Id = 0;
                    importInvoice.RowVersion = null;
                    importInvoice.InvoiceNo = targetInvoiceNo;
                    importInvoice.CustomerId = customerId;
                    importInvoice.ExporterId = exporterId;
                    importInvoice.OwnerUserId = null;
                    _businessDataAccessScope.ApplyOwner(importInvoice);
                    importInvoice.Items = importItems;
                    importInvoice.CalculateTotals();
                    importInvoice.Items = null;

                    int finalInvoiceId;
                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.Overwrite)
                    {
                        importInvoice.Id = preview.ExistingInvoiceId;
                        context.Invoices.Update(importInvoice);
                        await context.SaveChangesAsync(token);
                        await ReplaceItemsAsync(context, importInvoice.Id, importItems, token);
                        finalInvoiceId = importInvoice.Id;
                    }
                    else if (preview.InvoiceExists && action == InvoiceImportConflictAction.AppendItems)
                    {
                        await AppendItemsAsync(context, preview.ExistingInvoiceId, importItems, token);
                        finalInvoiceId = preview.ExistingInvoiceId;
                    }
                    else
                    {
                        await context.Invoices.AddAsync(importInvoice, token);
                        await context.SaveChangesAsync(token);
                        await ReplaceItemsAsync(context, importInvoice.Id, importItems, token);
                        finalInvoiceId = importInvoice.Id;
                    }

                    return new InvoiceImportResult
                    {
                        Success = true,
                        Message = "导入成功",
                        InvoiceId = finalInvoiceId,
                        FinalInvoiceNo = targetInvoiceNo,
                        ActionTaken = action
                    };
                },
                cancellationToken);
        }

        private async Task<InvoiceTransferPackage> BuildExportPackageAsync(AppDbContext context, int invoiceId, CancellationToken cancellationToken)
        {
            var invoice = await _businessDataAccessScope
                .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
            {
                throw new Exception("未找到要导出的发票");
            }

            var items = await context.Items.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);

            Customer customer = null;
            Exporter exporter = null;
            if (invoice.CustomerId > 0)
            {
                customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);
            }

            if (invoice.ExporterId > 0)
            {
                exporter = await context.Exporters.AsNoTracking().FirstOrDefaultAsync(e => e.Id == invoice.ExporterId, cancellationToken);
            }

            return new InvoiceTransferPackage
            {
                SchemaVersion = "1.0",
                AppVersion = typeof(InvoiceTransferService).Assembly.GetName().Version?.ToString() ?? "1.0",
                CreatedAt = DateTime.Now,
                Invoice = CloneInvoice(invoice),
                Items = CloneItems(items),
                Customer = CloneCustomer(customer),
                Exporter = CloneExporter(exporter)
            };
        }

        private async Task<InvoiceTransferPreview> BuildPreviewAsync(AppDbContext context, InvoiceTransferPackage pkg, CancellationToken cancellationToken)
        {
            EnsurePackageValid(pkg);

            var preview = new InvoiceTransferPreview
            {
                InvoiceNo = pkg.Invoice.InvoiceNo,
                Type = pkg.Invoice.Type,
                ItemCount = pkg.Items?.Count ?? 0
            };

            if (pkg.Customer != null)
            {
                preview.CustomerExists = await context.Customers.AnyAsync(c =>
                    c.CustomerNameEN == pkg.Customer.CustomerNameEN ||
                    (!string.IsNullOrWhiteSpace(pkg.Customer.TaxId) && c.TaxId == pkg.Customer.TaxId), cancellationToken);
            }

            if (pkg.Exporter != null)
            {
                preview.ExporterExists = await context.Exporters.AnyAsync(e =>
                    e.ExporterNameEN == pkg.Exporter.ExporterNameEN ||
                    e.ExporterNameCN == pkg.Exporter.ExporterNameCN ||
                    (!string.IsNullOrWhiteSpace(pkg.Exporter.CreditCode) && e.CreditCode == pkg.Exporter.CreditCode), cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(preview.InvoiceNo))
            {
                var existing = await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .FirstOrDefaultAsync(i => i.InvoiceNo == preview.InvoiceNo && i.Type == preview.Type, cancellationToken);
                if (existing != null)
                {
                    preview.InvoiceExists = true;
                    preview.ExistingInvoiceId = existing.Id;
                    preview.InvoiceMatches = CompareInvoice(existing, pkg.Invoice) &&
                                             await CompareItemsAsync(context, existing.Id, pkg.Items, cancellationToken);
                }
            }

            return preview;
        }

        private async Task<string> ResolveInvoiceNoAsync(
            AppDbContext context,
            string baseInvoiceNo,
            string invoiceType,
            string requestedInvoiceNo,
            CancellationToken cancellationToken)
        {
            var seed = string.IsNullOrWhiteSpace(requestedInvoiceNo)
                ? $"{baseInvoiceNo}_IMPORTED"
                : requestedInvoiceNo.Trim();
            var candidate = seed;
            var counter = 1;

            while (await context.Invoices.AnyAsync(i => i.InvoiceNo == candidate && i.Type == invoiceType, cancellationToken))
            {
                candidate = seed + counter;
                counter++;
            }

            return candidate;
        }

        private static void EnsurePackageValid(InvoiceTransferPackage package)
        {
            if (package == null)
            {
                throw new InvalidDataException("单据包数据无效");
            }

            if (package.Invoice == null)
            {
                throw new InvalidDataException("单据包缺少发票数据");
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static Invoice CloneInvoice(Invoice invoice)
        {
            return invoice?.CloneHeader();
        }

        private static List<Item> CloneItems(IEnumerable<Item> items)
        {
            return items?
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList()
                ?? new List<Item>();
        }

        private static Customer CloneCustomer(Customer customer)
        {
            if (customer == null)
            {
                return null;
            }

            return new Customer
            {
                Id = customer.Id,
                CustomerNameEN = customer.CustomerNameEN,
                NotifyPartyName = customer.NotifyPartyName,
                AddressEN = customer.AddressEN,
                NotifyPartyAddress = customer.NotifyPartyAddress,
                ContactPerson = customer.ContactPerson,
                Phone = customer.Phone,
                Email = customer.Email,
                TaxId = customer.TaxId,
                Notes = customer.Notes,
                RowVersion = customer.RowVersion?.ToArray()
            };
        }

        private static Exporter CloneExporter(Exporter exporter)
        {
            if (exporter == null)
            {
                return null;
            }

            return new Exporter
            {
                Id = exporter.Id,
                ExporterNameEN = exporter.ExporterNameEN,
                ExporterNameCN = exporter.ExporterNameCN,
                AddressEN = exporter.AddressEN,
                AddressCN = exporter.AddressCN,
                ContactPerson = exporter.ContactPerson,
                CreditCode = exporter.CreditCode,
                CustomsCode = exporter.CustomsCode,
                Phone = exporter.Phone,
                BankName = exporter.BankName,
                BankAccount = exporter.BankAccount,
                SwiftCode = exporter.SwiftCode,
                Notes = exporter.Notes,
                DocSealPath = exporter.DocSealPath,
                CustomsSealPath = exporter.CustomsSealPath,
                RowVersion = exporter.RowVersion?.ToArray()
            };
        }

        private static bool CompareInvoice(Invoice a, Invoice b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.InvoiceNo == b.InvoiceNo &&
                   a.Type == b.Type &&
                   a.TotalAmount == b.TotalAmount &&
                   a.TotalQuantity == b.TotalQuantity &&
                   a.Currency == b.Currency;
        }

        private async Task<bool> CompareItemsAsync(AppDbContext context, int invoiceId, List<Item> items, CancellationToken cancellationToken)
        {
            var existing = await context.Items.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);
            var incoming = items ?? new List<Item>();
            if (existing.Count != incoming.Count)
            {
                return false;
            }

            var currentItems = existing.OrderBy(x => x.StyleNo).ThenBy(x => x.Quantity).ThenBy(x => x.UnitPrice).ToList();
            var importedItems = incoming.OrderBy(x => x.StyleNo).ThenBy(x => x.Quantity).ThenBy(x => x.UnitPrice).ToList();

            for (var i = 0; i < currentItems.Count; i++)
            {
                var currentItem = currentItems[i];
                var importedItem = importedItems[i];
                if (currentItem.StyleNo != importedItem.StyleNo ||
                    currentItem.Quantity != importedItem.Quantity ||
                    currentItem.UnitPrice != importedItem.UnitPrice)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task ReplaceItemsAsync(AppDbContext context, int invoiceId, List<Item> items, CancellationToken cancellationToken)
        {
            var existingItems = await context.Items.Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);
            context.Items.RemoveRange(existingItems);
            await context.SaveChangesAsync(cancellationToken);

            var normalizedItems = CloneItems(items);
            foreach (var item in normalizedItems)
            {
                item.Id = 0;
                item.InvoiceId = invoiceId;
                await context.Items.AddAsync(item, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await UpdateInvoiceTotalsAsync(context, invoiceId, normalizedItems, cancellationToken);
        }

        private async Task AppendItemsAsync(AppDbContext context, int invoiceId, List<Item> items, CancellationToken cancellationToken)
        {
            var normalizedItems = CloneItems(items);
            foreach (var item in normalizedItems)
            {
                item.Id = 0;
                item.InvoiceId = invoiceId;
                await context.Items.AddAsync(item, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await UpdateInvoiceTotalsAsync(context, invoiceId, cancellationToken: cancellationToken);
        }

        private async Task UpdateInvoiceTotalsAsync(AppDbContext context, int invoiceId, List<Item> items = null, CancellationToken cancellationToken = default)
        {
            var invoice = await _businessDataAccessScope
                .ApplyInvoiceScope(context.Invoices)
                .FirstOrDefaultAsync(x => x.Id == invoiceId, cancellationToken);
            if (invoice == null)
            {
                return;
            }

            var effectiveItems = items ??
                                 await context.Items.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);
            ApplyCalculatedTotals(invoice, effectiveItems);
            context.Invoices.Update(invoice);
            await context.SaveChangesAsync(cancellationToken);
        }

        private static void ApplyCalculatedTotals(Invoice invoice, List<Item> items)
        {
            var snapshot = invoice.CloneHeader();
            snapshot.Items = items ?? new List<Item>();
            snapshot.CalculateTotals();

            invoice.TotalCartons = snapshot.TotalCartons;
            invoice.TotalQuantity = snapshot.TotalQuantity;
            invoice.TotalGrossWeight = snapshot.TotalGrossWeight;
            invoice.TotalNetWeight = snapshot.TotalNetWeight;
            invoice.TotalVolume = snapshot.TotalVolume;
            invoice.TotalAmount = snapshot.TotalAmount;
            invoice.TotalPurchaseAmount = snapshot.TotalPurchaseAmount;
            invoice.TotalTaxRefundAmount = snapshot.TotalTaxRefundAmount;
            invoice.TotalProfit = snapshot.TotalProfit;
        }
    }
}
