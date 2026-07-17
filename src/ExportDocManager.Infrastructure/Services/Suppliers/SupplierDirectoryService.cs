using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers
{
    public sealed class SupplierDirectoryService : ISupplierDirectoryService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public SupplierDirectoryService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .OrderBy(item => item.Name).Select(ToRecordExpression()).ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<SupplierRecord>> QueryAsync(
            string keyword, string status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 10, 100);
            keyword = Clean(keyword);
            status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
            if (keyword.Length > 0)
                query = query.Where(item => item.Name.Contains(keyword) || item.CountryRegion.Contains(keyword) ||
                    item.Category.Contains(keyword) || item.MainProducts.Contains(keyword) || item.Notes.Contains(keyword));
            if (status.Length > 0) query = query.Where(item => item.Status == status);
            int total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).Skip((pageNumber - 1) * pageSize).Take(pageSize)
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
                entity = await _accessScope.ApplySupplierScope(context.SupplierCompanies)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商不存在或无权访问。");
            }
            else
            {
                entity = new SupplierCompany();
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
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return ToRecord(entity);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplySupplierScope(context.SupplierCompanies)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null) return false;
            context.SupplierCompanies.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
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
            foreach (var row in rows) { row.Status = status; row.UpdatedAt = DateTimeOffset.UtcNow; }
            await context.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }

        public async Task<IReadOnlyList<SupplierContactRecord>> ListContactsAsync(int supplierCompanyId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var suppliers = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
            return await context.SupplierContacts.AsNoTracking()
                .Where(item => item.SupplierCompanyId == supplierCompanyId && suppliers.Any(supplier => supplier.Id == item.SupplierCompanyId))
                .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.Name)
                .Select(item => new SupplierContactRecord(item.Id, item.SupplierCompanyId, item.Name, item.Title,
                    item.Email, item.Phone, item.InstantMessaging, item.IsPrimary))
                .ToListAsync(cancellationToken);
        }

        public async Task<SupplierContactRecord> SaveContactAsync(SupplierContactSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                    .AnyAsync(item => item.Id == request.SupplierCompanyId, cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");
            var entity = request.Id > 0
                ? await context.SupplierContacts.FirstOrDefaultAsync(item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商联系人不存在。")
                : new SupplierContact { SupplierCompanyId = request.SupplierCompanyId };
            if (entity.Id == 0) await context.SupplierContacts.AddAsync(entity, cancellationToken);
            entity.Name = Required(request.Name, "联系人姓名");
            entity.Title = Clean(request.Title);
            entity.Email = Clean(request.Email);
            entity.Phone = Clean(request.Phone);
            entity.InstantMessaging = Clean(request.InstantMessaging);
            entity.IsPrimary = request.IsPrimary;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            if (entity.IsPrimary)
            {
                var previous = await context.SupplierContacts.Where(item => item.SupplierCompanyId == request.SupplierCompanyId && item.Id != entity.Id && item.IsPrimary).ToListAsync(cancellationToken);
                foreach (var item in previous) item.IsPrimary = false;
            }
            await context.SaveChangesAsync(cancellationToken);
            return new(entity.Id, entity.SupplierCompanyId, entity.Name, entity.Title, entity.Email,
                entity.Phone, entity.InstantMessaging, entity.IsPrimary);
        }

        public async Task<bool> DeleteContactAsync(int supplierCompanyId, int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                    .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken)) return false;
            var entity = await context.SupplierContacts.FirstOrDefaultAsync(item => item.Id == id && item.SupplierCompanyId == supplierCompanyId, cancellationToken);
            if (entity == null) return false;
            context.SupplierContacts.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<IReadOnlyList<SupplierProductOptionRecord>> SearchProductsAsync(
            string keyword, CancellationToken cancellationToken = default)
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
                select new SupplierProductLinkRecord(link.Id, link.SupplierCompanyId, link.ProductId,
                    product.ProductCode ?? string.Empty, product.NameCN ?? string.Empty, product.NameEN ?? string.Empty,
                    link.SupplierProductCode, link.ReferencePrice, link.Currency, link.LeadTimeDays, link.Status))
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
            if (duplicate) throw new ArgumentException("该供应商已经关联此产品。");

            var entity = request.Id > 0
                ? await context.SupplierProductLinks.FirstOrDefaultAsync(item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商产品关联不存在。")
                : new SupplierProductLink { SupplierCompanyId = request.SupplierCompanyId };
            if (entity.Id == 0) await context.SupplierProductLinks.AddAsync(entity, cancellationToken);
            entity.ProductId = request.ProductId;
            entity.SupplierProductCode = Clean(request.SupplierProductCode);
            entity.ReferencePrice = request.ReferencePrice;
            entity.Currency = currency;
            entity.LeadTimeDays = request.LeadTimeDays;
            entity.Status = status;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
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
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private Task<bool> CanAccessSupplierAsync(AppDbContext context, int supplierCompanyId, CancellationToken cancellationToken) =>
            _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken);

        private static SupplierProductLinkRecord ToProductLinkRecord(SupplierProductLink link, Product product) =>
            new(link.Id, link.SupplierCompanyId, link.ProductId, product.ProductCode ?? string.Empty,
                product.NameCN ?? string.Empty, product.NameEN ?? string.Empty, link.SupplierProductCode,
                link.ReferencePrice, link.Currency, link.LeadTimeDays, link.Status);

        private static System.Linq.Expressions.Expression<Func<SupplierCompany, SupplierRecord>> ToRecordExpression() =>
            item => new SupplierRecord(item.Id, item.Name, item.CountryRegion, item.Category, item.Website,
                item.Status, item.MainProducts, item.Notes);
        private static SupplierRecord ToRecord(SupplierCompany item) =>
            new(item.Id, item.Name, item.CountryRegion, item.Category, item.Website, item.Status, item.MainProducts, item.Notes);
        private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field}不能为空。") : value.Trim();
        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
