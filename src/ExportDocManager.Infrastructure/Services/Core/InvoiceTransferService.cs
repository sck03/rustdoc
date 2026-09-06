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
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public class InvoiceTransferService : IInvoiceTransferService
    {
        private const long MaximumPackageJsonBytes = 32L * 1024L * 1024L;
        private const long MaximumPackageMetadataBytes = 256L * 1024L;
        private const int MaximumPackageEntries = 16;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IInvoicePartyResolver _invoicePartyResolver;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly IAppPathProvider _pathProvider;
        private readonly IBusinessClock _clock;

        public InvoiceTransferService(
            IDbContextFactory<AppDbContext> contextFactory,
            IInvoicePartyResolver invoicePartyResolver,
            IAppPathProvider pathProvider,
            BusinessDataAccessScope businessDataAccessScope,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _invoicePartyResolver = invoicePartyResolver ?? throw new ArgumentNullException(nameof(invoicePartyResolver));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
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
            if (archive.Entries.Count > MaximumPackageEntries)
            {
                throw new InvalidDataException("单据包包含过多文件。");
            }
            var dataEntry = archive.GetEntry("data.json");
            var metaEntry = archive.GetEntry("meta.json");
            if (dataEntry == null || metaEntry == null)
            {
                throw new InvalidDataException("包格式不正确，缺少必要文件");
            }

            string dataJson;
            using (var stream = dataEntry.Open())
            {
                dataJson = await BoundedStreamHelper.ReadUtf8TextAsync(
                    stream,
                    MaximumPackageJsonBytes,
                    cancellationToken);
            }

            string metaJson;
            using (var stream = metaEntry.Open())
            {
                metaJson = await BoundedStreamHelper.ReadUtf8TextAsync(
                    stream,
                    MaximumPackageMetadataBytes,
                    cancellationToken);
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

        public async Task<InvoiceImportResult> ImportAsync(InvoiceTransferPackage pkg, InvoiceImportConflictAction action, string? newInvoiceNo = null, CancellationToken cancellationToken = default)
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

                    var importCustomer = CloneCustomer(pkg.Customer);
                    if (importCustomer != null) importCustomer.Id = 0;
                    var customerId = await _invoicePartyResolver.ResolveCustomerIdAsync(
                        context,
                        importCustomer,
                        pkg.Invoice?.CustomerNameEN,
                        token);
                    var importExporter = CloneExporter(pkg.Exporter);
                    if (importExporter != null) importExporter.Id = 0;
                    var exporterId = await _invoicePartyResolver.ResolveExporterIdAsync(
                        context,
                        importExporter,
                        pkg.Invoice?.ExporterNameEN,
                        pkg.Invoice?.ExporterNameCN,
                        token);
                    var importItems = CloneItems(pkg.Items);
                    var packageInvoice = pkg.Invoice
                        ?? throw new InvalidDataException("迁移包缺少发票主体。");
                    var importInvoice = CloneInvoice(packageInvoice);

                    importInvoice.Id = 0;
                    importInvoice.RowVersion = null;
                    importInvoice.CustomerId = customerId;
                    importInvoice.ExporterId = exporterId;
                    importInvoice.OwnerUserId = null;
                    _businessDataAccessScope.ApplyOwner(importInvoice);

                    // The visibility scope may hide another department's invoice, but the
                    // company-scoped unique key must still be honored. Keep the preview
                    // usable without exposing the hidden record ID.
                    bool companyScopedConflict = await context.Invoices.AsNoTracking().AnyAsync(
                        item => item.CompanyScope == importInvoice.CompanyScope &&
                            item.InvoiceNo == importInvoice.InvoiceNo &&
                            item.Type == importInvoice.Type,
                        token);
                    if (companyScopedConflict && !preview.InvoiceExists)
                    {
                        preview.InvoiceExists = true;
                        preview.InvoiceMatches = false;
                        preview.ExistingInvoiceId = 0;
                    }

                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.Skip)
                    {
                        return new InvoiceImportResult
                        {
                            Success = true,
                            Message = preview.InvoiceMatches
                                ? "目标库中已存在完全相同的单据，已跳过导入。"
                                : "目标公司范围已有相同发票号和类型，已跳过导入。",
                            ActionTaken = action,
                            InvoiceId = preview.ExistingInvoiceId > 0 ? preview.ExistingInvoiceId : null,
                            FinalInvoiceNo = preview.InvoiceNo
                        };
                    }

                    if (preview.InvoiceExists && preview.ExistingInvoiceId <= 0 &&
                        action is not InvoiceImportConflictAction.Skip and not InvoiceImportConflictAction.NewInvoiceNo)
                    {
                        throw new PermissionDeniedException("目标公司范围已有相同发票号和类型，但当前账号无权覆盖；请改用新发票号导入。");
                    }

                    Invoice? existingInvoiceForMutation = null;
                    if (preview.InvoiceExists && preview.ExistingInvoiceId > 0 &&
                        action is InvoiceImportConflictAction.Overwrite or InvoiceImportConflictAction.AppendItems)
                    {
                        existingInvoiceForMutation = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                            .FirstOrDefaultAsync(item => item.Id == preview.ExistingInvoiceId, token);
                        if (existingInvoiceForMutation == null)
                        {
                            throw new ResourceNotFoundException("目标发票已不存在或当前账号无权修改，请刷新后重试。");
                        }

                        _businessDataAccessScope.DemandRecordAccess(existingInvoiceForMutation, PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate);
                        if (!InvoiceStatusCatalog.IsEditable(existingInvoiceForMutation.Status))
                        {
                            throw new ResourceConflictException("目标发票已锁定，请先反审核回草稿后再导入覆盖或追加明细。");
                        }
                    }

                    var targetInvoiceNo = importInvoice.InvoiceNo;
                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.NewInvoiceNo)
                    {
                        targetInvoiceNo = await ResolveInvoiceNoAsync(
                            context,
                            importInvoice.CompanyScope,
                            importInvoice.InvoiceNo,
                            importInvoice.Type,
                            newInvoiceNo,
                            token);
                    }

                    importInvoice.InvoiceNo = targetInvoiceNo;
                    importInvoice.Status = InvoiceStatusCatalog.Draft;
                    importInvoice.Items = importItems;
                    await InvoiceBusinessValidator.ValidateNormalizeAndCalculateAsync(
                        context,
                        importInvoice,
                        importItems,
                        isNew: existingInvoiceForMutation == null,
                        existingStatus: existingInvoiceForMutation?.Status ?? InvoiceStatusCatalog.Draft,
                        token).ConfigureAwait(false);
                    importItems = importInvoice.Items?.ToList() ?? [];
                    importInvoice.Items = [];

                    int finalInvoiceId;
                    if (preview.InvoiceExists && action == InvoiceImportConflictAction.Overwrite)
                    {
                        var existingInvoice = existingInvoiceForMutation
                            ?? throw new ResourceNotFoundException("目标发票已不存在或当前账号无权修改，请刷新后重试。");
                        importInvoice.Id = existingInvoice.Id;
                        importInvoice.OwnerUserId = existingInvoice.OwnerUserId;
                        importInvoice.DepartmentId = existingInvoice.DepartmentId;
                        importInvoice.CompanyScope = existingInvoice.CompanyScope;
                        importInvoice.RowVersion = existingInvoice.RowVersion?.ToArray();
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
                throw new ResourceNotFoundException("未找到要导出的发票。");
            }

            _businessDataAccessScope.DemandRecordAccess(invoice, PermissionResourceCatalog.InvoiceOutput, PermissionAction.ExportZip);
            var items = await context.Items.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);

            Customer? customer = null;
            Exporter? exporter = null;
            if (invoice.CustomerId > 0)
            {
                customer = await _businessDataAccessScope.ApplyCustomerScope(context.Customers.AsNoTracking())
                    .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);
                if (customer == null && await context.Customers.AnyAsync(c => c.Id == invoice.CustomerId, cancellationToken))
                    throw new PermissionDeniedException("单据包关联的客户不在当前账号的数据范围内。");
            }

            if (invoice.ExporterId > 0)
            {
                exporter = await _businessDataAccessScope.ApplyExporterScope(context.Exporters.AsNoTracking())
                    .FirstOrDefaultAsync(e => e.Id == invoice.ExporterId, cancellationToken);
                if (exporter == null && await context.Exporters.AnyAsync(e => e.Id == invoice.ExporterId, cancellationToken))
                    throw new PermissionDeniedException("单据包关联的出口商不在当前账号的数据范围内。");
            }

            return new InvoiceTransferPackage
            {
                SchemaVersion = "1.0",
                AppVersion = typeof(InvoiceTransferService).Assembly.GetName().Version?.ToString() ?? "1.0",
                CreatedAt = _clock.UtcNow,
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
                Type = pkg.Invoice.Type ?? string.Empty,
                ItemCount = pkg.Items?.Count ?? 0
            };

            if (pkg.Customer != null)
            {
                preview.CustomerExists = await _businessDataAccessScope
                    .ApplyCustomerScope(context.Customers.AsNoTracking())
                    .AnyAsync(c =>
                    c.CustomerNameEN == pkg.Customer.CustomerNameEN ||
                    (!string.IsNullOrWhiteSpace(pkg.Customer.TaxId) && c.TaxId == pkg.Customer.TaxId), cancellationToken);
            }

            if (pkg.Exporter != null)
            {
                preview.ExporterExists = await _businessDataAccessScope
                    .ApplyExporterScope(context.Exporters.AsNoTracking())
                    .AnyAsync(e =>
                    e.ExporterNameEN == pkg.Exporter.ExporterNameEN ||
                    e.ExporterNameCN == pkg.Exporter.ExporterNameCN ||
                    (!string.IsNullOrWhiteSpace(pkg.Exporter.CreditCode) && e.CreditCode == pkg.Exporter.CreditCode), cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(preview.InvoiceNo))
            {
                string companyScope = ResolveImportCompanyScope(pkg.Invoice.CompanyScope);
                var existing = await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .FirstOrDefaultAsync(i =>
                        i.CompanyScope == companyScope &&
                        i.InvoiceNo == preview.InvoiceNo &&
                        i.Type == preview.Type,
                        cancellationToken);
                if (existing != null)
                {
                    preview.InvoiceExists = true;
                    preview.ExistingInvoiceId = existing.Id;
                    preview.InvoiceMatches = CompareInvoice(existing, pkg.Invoice) &&
                                             await CompareItemsAsync(context, existing.Id, pkg.Items, cancellationToken);
                }

                if (!preview.InvoiceExists)
                {
                    bool hiddenCompanyScopedConflict = await context.Invoices.AsNoTracking().AnyAsync(
                        i => i.CompanyScope == companyScope &&
                            i.InvoiceNo == preview.InvoiceNo &&
                            i.Type == preview.Type,
                        cancellationToken);
                    if (hiddenCompanyScopedConflict)
                    {
                        preview.InvoiceExists = true;
                        preview.InvoiceMatches = false;
                        preview.ExistingInvoiceId = 0;
                    }
                }
            }

            return preview;
        }

        private string ResolveImportCompanyScope(string? packageCompanyScope)
        {
            var currentUser = _businessDataAccessScope.CurrentUser;
            return currentUser != null && currentUser.Id > 0
                ? (currentUser.CompanyScope ?? string.Empty).Trim()
                : (packageCompanyScope ?? string.Empty).Trim();
        }

        private async Task<string> ResolveInvoiceNoAsync(
            AppDbContext context,
            string? companyScope,
            string? baseInvoiceNo,
            string? invoiceType,
            string? requestedInvoiceNo,
            CancellationToken cancellationToken)
        {
            var seed = string.IsNullOrWhiteSpace(requestedInvoiceNo)
                ? $"{baseInvoiceNo}_IMPORTED"
                : requestedInvoiceNo.Trim();
            var candidate = seed;
            var counter = 1;

            string normalizedCompanyScope = (companyScope ?? string.Empty).Trim();
            while (await context.Invoices.AnyAsync(
                i => i.CompanyScope == normalizedCompanyScope &&
                    i.InvoiceNo == candidate &&
                    i.Type == invoiceType,
                cancellationToken))
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

            string normalizedType = InvoiceTypeCatalog.Normalize(package.Invoice.Type);
            if (!InvoiceTypeCatalog.IsKnown(normalizedType))
            {
                throw new InvalidDataException("单据包发票类型只能是“实际数据”或“报关数据”。");
            }

            package.Invoice.Type = normalizedType;
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static Invoice CloneInvoice(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            return invoice.CloneHeader();
        }

        private static List<Item> CloneItems(IEnumerable<Item> items)
        {
            return items?
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList()
                ?? new List<Item>();
        }

        private static Customer? CloneCustomer(Customer? customer)
        {
            if (customer == null)
            {
                return null;
            }

            return new Customer
            {
                Id = customer.Id,
                CustomerNameEN = customer.CustomerNameEN,
                NotifyPartyMode = customer.NotifyPartyMode,
                NotifyPartyName = customer.NotifyPartyMode == NotifyPartyMode.Separate ? customer.NotifyPartyName : string.Empty,
                AddressEN = customer.AddressEN,
                NotifyPartyAddress = customer.NotifyPartyMode == NotifyPartyMode.Separate ? customer.NotifyPartyAddress : string.Empty,
                ContactPerson = customer.ContactPerson,
                Phone = customer.Phone,
                Email = customer.Email,
                TaxId = customer.TaxId,
                Notes = customer.Notes,
                RowVersion = customer.RowVersion?.ToArray()
            };
        }

        private static Exporter? CloneExporter(Exporter? exporter)
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

        private async Task<bool> CompareItemsAsync(AppDbContext context, int invoiceId, List<Item>? items, CancellationToken cancellationToken)
        {
            var existing = await context.Items.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(cancellationToken);
            var incoming = items ?? new List<Item>();
            if (existing.Count != incoming.Count)
            {
                return false;
            }

            var currentItems = existing
                .OrderBy(x => x.StyleNo)
                .ThenBy(x => x.Quantity)
                .ThenBy(x => x.UnitPrice)
                .ThenBy(x => x.TotalPrice)
                .ToList();
            var importedItems = incoming
                .OrderBy(x => x.StyleNo)
                .ThenBy(x => x.Quantity)
                .ThenBy(x => x.UnitPrice)
                .ThenBy(x => x.TotalPrice)
                .ToList();

            for (var i = 0; i < currentItems.Count; i++)
            {
                var currentItem = currentItems[i];
                var importedItem = importedItems[i];
                if (currentItem.StyleNo != importedItem.StyleNo ||
                    currentItem.Quantity != importedItem.Quantity ||
                    currentItem.UnitPrice != importedItem.UnitPrice ||
                    currentItem.TotalPrice != importedItem.TotalPrice ||
                    !string.Equals(
                        ItemPriceCalculationModeCatalog.Normalize(currentItem.PriceCalculationMode),
                        ItemPriceCalculationModeCatalog.Normalize(importedItem.PriceCalculationMode),
                        StringComparison.Ordinal))
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

        private async Task UpdateInvoiceTotalsAsync(AppDbContext context, int invoiceId, List<Item>? items = null, CancellationToken cancellationToken = default)
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
