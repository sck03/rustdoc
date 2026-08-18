using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers
{
    public sealed class SupplierDirectoryService : ISupplierDirectoryService
    {
        private const int MaximumContactsPerSupplier = 500;
        private const int MaximumProductLinksPerSupplier = 1_000;
        private const int LegacyListLimit = 200;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly IBusinessClock _clock;

        public SupplierDirectoryService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .OrderBy(item => item.Name)
                .Take(LegacyListLimit)
                .Select(ToRecordExpression())
                .ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<SupplierRecord>> QueryAsync(
            string? keyword, string? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 10, 100);
            keyword = Clean(keyword);
            status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
            query = query.ApplyKeywordSearch(
                context,
                keyword,
                item => item.Name,
                item => item.CountryRegion,
                item => item.Category,
                item => item.MainProducts,
                item => item.Notes);
            if (status.Length > 0) query = query.Where(item => item.Status == status);
            int total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).Skip(PagingHelper.CalculateOffset(pageNumber, pageSize)).Take(pageSize)
                .Select(ToRecordExpression()).ToListAsync(cancellationToken);
            return new PagedResult<SupplierRecord>(items, total, pageNumber, pageSize);
        }

        public async Task<SupplierRecord> SaveAsync(SupplierSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            SupplierCompany entity;
            if (request.Id > 0)
            {
                if (request.ExpectedVersion <= 0)
                    throw new BusinessConcurrencyException("保存现有供应商时必须提供版本号，请刷新后重试。");
                entity = await _accessScope.ApplySupplierScope(context.SupplierCompanies)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商不存在或无权访问。");
                if (entity.VersionNumber != request.ExpectedVersion)
                    throw new BusinessConcurrencyException("该供应商已被其他用户修改，请刷新后重试。");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            else
            {
                entity = new SupplierCompany { VersionNumber = 1 };
                _accessScope.ApplyOwner(entity);
                await context.SupplierCompanies.AddAsync(entity, cancellationToken);
            }
            entity.Name = Required(request.Name, "供应商名称");
            entity.CountryRegion = Clean(request.CountryRegion);
            entity.Category = Clean(request.Category);
            entity.Website = Clean(request.Website);
            entity.Status = string.IsNullOrWhiteSpace(request.Status) ? "合作中" : request.Status.Trim();
            entity.MainProducts = Clean(request.MainProducts);
            entity.Notes = Clean(request.Notes);
            entity.UpdatedAt = _clock.UtcNow;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该供应商已被其他用户修改，请刷新后重试。", exception);
            }
            return ToRecord(entity);
        }

        public async Task<SupplierDeleteResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplySupplierScope(context.SupplierCompanies)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null)
            {
                return new SupplierDeleteResult(false, false, false, 0, 0, 0);
            }

            var contacts = await context.SupplierContacts
                .Where(item => item.SupplierCompanyId == id)
                .ToListAsync(cancellationToken);
            int contactCount = contacts.Count;
            int productLinkCount = await context.SupplierProductLinks
                .CountAsync(item => item.SupplierCompanyId == id, cancellationToken);
            int assessmentCount = await context.SupplierAssessments
                .CountAsync(item => item.SupplierCompanyId == id, cancellationToken);

            if (productLinkCount > 0 || assessmentCount > 0)
            {
                entity.Status = "停用";
                entity.VersionNumber++;
                entity.UpdatedAt = _clock.UtcNow;
                await SaveWithConcurrencyAsync(context, "供应商", cancellationToken);
                return new SupplierDeleteResult(
                    true,
                    Deleted: false,
                    Deactivated: true,
                    contactCount,
                    productLinkCount,
                    assessmentCount);
            }

            context.SupplierContacts.RemoveRange(contacts);
            context.SupplierCompanies.Remove(entity);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该供应商已被其他用户修改，请刷新后重试。", exception);
            }
            catch (DbUpdateException exception)
            {
                throw new BusinessConcurrencyException(
                    "该供应商在删除期间新增了联系人、供货关系或评价，系统未删除供应商；请刷新后重试。",
                    exception);
            }
            return new SupplierDeleteResult(
                true,
                Deleted: true,
                Deactivated: false,
                contactCount,
                productLinkCount,
                assessmentCount);
        }

        public async Task<int> UpdateStatusAsync(IReadOnlyList<int> ids, string status, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            int[] normalizedIds = ids.Where(id => id > 0).Distinct().Take(500).ToArray();
            status = Clean(status);
            if (normalizedIds.Length == 0) throw new ArgumentException("请选择供应商。");
            if (status is not ("合作中" or "考察中" or "暂停" or "停用")) throw new ArgumentException("供应商状态无效。");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await _accessScope.ApplySupplierScope(context.SupplierCompanies)
                .Where(item => normalizedIds.Contains(item.Id)).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.Status = status;
                row.VersionNumber++;
                row.UpdatedAt = _clock.UtcNow;
            }
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("部分供应商已被其他用户修改，请刷新列表后重试。", exception);
            }
            return rows.Count;
        }

        public async Task<IReadOnlyList<SupplierContactRecord>> ListContactsAsync(int supplierCompanyId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var suppliers = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
            return await context.SupplierContacts.AsNoTracking()
                .Where(item => item.SupplierCompanyId == supplierCompanyId && suppliers.Any(supplier => supplier.Id == item.SupplierCompanyId))
                .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.Name)
                .Take(MaximumContactsPerSupplier)
                .Select(item => new SupplierContactRecord(item.Id, item.SupplierCompanyId, item.Name, item.Title,
                    item.Email, item.Phone, item.InstantMessaging, item.IsPrimary, item.VersionNumber))
                .ToListAsync(cancellationToken);
        }

        public async Task<SupplierContactRecord> SaveContactAsync(SupplierContactSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                    .AnyAsync(item => item.Id == request.SupplierCompanyId, cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");
            bool isNew = request.Id <= 0;
            var entity = request.Id > 0
                ? await context.SupplierContacts.FirstOrDefaultAsync(item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商联系人不存在。")
                : new SupplierContact { SupplierCompanyId = request.SupplierCompanyId, VersionNumber = 1 };
            if (!isNew)
            {
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "供应商联系人");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            if (entity.Id == 0) await context.SupplierContacts.AddAsync(entity, cancellationToken);
            entity.Name = Required(request.Name, "联系人姓名");
            entity.Title = Clean(request.Title);
            entity.Email = Clean(request.Email);
            entity.Phone = Clean(request.Phone);
            entity.InstantMessaging = Clean(request.InstantMessaging);
            bool makePrimary = request.IsPrimary;
            entity.IsPrimary = false;
            entity.UpdatedAt = _clock.UtcNow;
            if (makePrimary)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var previous = await context.SupplierContacts.Where(item => item.SupplierCompanyId == request.SupplierCompanyId && item.Id != entity.Id && item.IsPrimary).ToListAsync(cancellationToken);
                foreach (var item in previous)
                {
                    item.IsPrimary = false;
                    item.VersionNumber++;
                    item.UpdatedAt = _clock.UtcNow;
                }

                await SaveWithConcurrencyAsync(context, "供应商联系人", cancellationToken);
                entity.IsPrimary = true;
                await SaveWithConcurrencyAsync(context, "供应商联系人", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await SaveWithConcurrencyAsync(context, "供应商联系人", cancellationToken);
            }
            return new(entity.Id, entity.SupplierCompanyId, entity.Name, entity.Title, entity.Email,
                entity.Phone, entity.InstantMessaging, entity.IsPrimary, entity.VersionNumber);
        }

        public async Task<bool> DeleteContactAsync(int supplierCompanyId, int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                    .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken)) return false;
            var entity = await context.SupplierContacts.FirstOrDefaultAsync(item => item.Id == id && item.SupplierCompanyId == supplierCompanyId, cancellationToken);
            if (entity == null) return false;
            context.SupplierContacts.Remove(entity);
            await SaveWithConcurrencyAsync(context, "供应商联系人", cancellationToken);
            return true;
        }

        public async Task<IReadOnlyList<SupplierProductOptionRecord>> SearchProductsAsync(
            string? keyword, CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = context.Products.AsNoTracking();
            if (keyword.Length > 0)
                query = query.Where(item => (item.ProductCode ?? string.Empty).Contains(keyword) ||
                    (item.NameCN ?? string.Empty).Contains(keyword) || (item.NameEN ?? string.Empty).Contains(keyword));
            return await query.OrderBy(item => item.ProductCode).ThenBy(item => item.NameCN).Take(50)
                .Select(item => new SupplierProductOptionRecord(item.Id, item.ProductCode ?? string.Empty,
                    item.NameCN ?? string.Empty, item.NameEN ?? string.Empty))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<SupplierProductLinkRecord>> ListProductLinksAsync(
            int supplierCompanyId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(context, supplierCompanyId, cancellationToken)) return [];
            return await (from link in context.SupplierProductLinks.AsNoTracking()
                          join product in context.Products.AsNoTracking() on link.ProductId equals product.Id
                          where link.SupplierCompanyId == supplierCompanyId
                          orderby product.ProductCode, product.NameCN
                          select new { Link = link, Product = product })
                .Take(MaximumProductLinksPerSupplier)
                .Select(row => new SupplierProductLinkRecord(row.Link.Id, row.Link.SupplierCompanyId, row.Link.ProductId,
                    row.Product.ProductCode ?? string.Empty, row.Product.NameCN ?? string.Empty, row.Product.NameEN ?? string.Empty,
                    row.Link.SupplierProductCode, row.Link.ReferencePrice, row.Link.Currency, row.Link.LeadTimeDays, row.Link.Status,
                    row.Link.VersionNumber))
                .ToListAsync(cancellationToken);
        }

        public async Task<SupplierProductLinkRecord> SaveProductLinkAsync(
            SupplierProductLinkSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ProductId <= 0) throw new ArgumentException("请选择产品。");
            if (request.ReferencePrice < 0) throw new ArgumentException("参考价不能小于零。");
            if (request.LeadTimeDays is < 0 or > 3650) throw new ArgumentException("交期天数必须在 0 至 3650 之间。");
            string currency = Clean(request.Currency).ToUpperInvariant();
            if (currency.Length != 3) throw new ArgumentException("币种必须使用三位代码，例如 CNY、USD。");
            string status = Clean(request.Status);
            if (status is not ("供货中" or "备选" or "暂停" or "停用")) throw new ArgumentException("供货状态无效。");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(context, request.SupplierCompanyId, cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");
            var product = await context.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.ProductId, cancellationToken)
                ?? throw new KeyNotFoundException("产品不存在。");
            bool duplicate = await context.SupplierProductLinks.AnyAsync(item => item.SupplierCompanyId == request.SupplierCompanyId &&
                item.ProductId == request.ProductId && item.Id != request.Id, cancellationToken);
            if (duplicate) throw new ResourceConflictException("该供应商已经关联此产品。");

            bool isNew = request.Id <= 0;
            var entity = request.Id > 0
                ? await context.SupplierProductLinks.FirstOrDefaultAsync(item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商产品关联不存在。")
                : new SupplierProductLink { SupplierCompanyId = request.SupplierCompanyId, VersionNumber = 1 };
            if (!isNew)
            {
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "供应产品关联");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            if (entity.Id == 0) await context.SupplierProductLinks.AddAsync(entity, cancellationToken);
            entity.ProductId = request.ProductId;
            entity.SupplierProductCode = Clean(request.SupplierProductCode);
            entity.ReferencePrice = request.ReferencePrice;
            entity.Currency = currency;
            entity.LeadTimeDays = request.LeadTimeDays;
            entity.Status = status;
            entity.UpdatedAt = _clock.UtcNow;
            await SaveWithConcurrencyAsync(context, "供应产品关联", cancellationToken);
            return ToProductLinkRecord(entity, product);
        }

        public async Task<bool> DeleteProductLinkAsync(
            int supplierCompanyId, int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(context, supplierCompanyId, cancellationToken)) return false;
            var entity = await context.SupplierProductLinks.FirstOrDefaultAsync(item => item.Id == id && item.SupplierCompanyId == supplierCompanyId, cancellationToken);
            if (entity == null) return false;
            context.SupplierProductLinks.Remove(entity);
            await SaveWithConcurrencyAsync(context, "供应产品关联", cancellationToken);
            return true;
        }

        private Task<bool> CanAccessSupplierAsync(AppDbContext context, int supplierCompanyId, CancellationToken cancellationToken) =>
            _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken);

        private static SupplierProductLinkRecord ToProductLinkRecord(SupplierProductLink link, Product product) =>
            new(link.Id, link.SupplierCompanyId, link.ProductId, product.ProductCode ?? string.Empty,
                product.NameCN ?? string.Empty, product.NameEN ?? string.Empty, link.SupplierProductCode,
                link.ReferencePrice, link.Currency, link.LeadTimeDays, link.Status, link.VersionNumber);

        private static System.Linq.Expressions.Expression<Func<SupplierCompany, SupplierRecord>> ToRecordExpression() =>
            item => new SupplierRecord(item.Id, item.Name, item.CountryRegion, item.Category, item.Website,
                item.Status, item.MainProducts, item.Notes, item.VersionNumber);
        private static SupplierRecord ToRecord(SupplierCompany item) =>
            new(item.Id, item.Name, item.CountryRegion, item.Category, item.Website, item.Status, item.MainProducts,
                item.Notes, item.VersionNumber);
        private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field}不能为空。") : value.Trim();
        private static string Clean(string? value) => (value ?? string.Empty).Trim();

        private static void EnsureExpectedVersion(int expectedVersion, int currentVersion, string entityName)
        {
            if (expectedVersion <= 0)
                throw new BusinessConcurrencyException($"保存现有{entityName}时必须提供版本号，请刷新后重试。");
            if (expectedVersion != currentVersion)
                throw new BusinessConcurrencyException($"该{entityName}已被其他用户修改，请刷新后重试。");
        }

        private static async Task SaveWithConcurrencyAsync(
            AppDbContext context,
            string entityName,
            CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException($"该{entityName}已被其他用户修改，请刷新后重试。", exception);
            }
            catch (DbUpdateException exception) when (entityName.Contains("联系人", StringComparison.Ordinal))
            {
                throw new BusinessConcurrencyException(
                    $"该{entityName}的主要联系人已被其他用户调整，请刷新后重试。",
                    exception);
            }
        }
    }
}
