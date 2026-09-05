using System.Data;
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
            string name = Required(request.Name, "供应商名称", 200);
            string countryRegion = Optional(request.CountryRegion, "国家/地区", 100);
            string category = Optional(request.Category, "供应商分类", 100);
            string website = Optional(request.Website, "网站", 300);
            string mainProducts = Optional(request.MainProducts, "主要产品", 500);
            string notes = Optional(request.Notes, "备注", 1000);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            SupplierCompany entity;
            if (request.Id > 0)
            {
                if (request.ExpectedVersion <= 0)
                    throw new BusinessConcurrencyException("保存现有供应商时必须提供版本号，请刷新后重试。");
                entity = await _accessScope.ApplySupplierScope(
                        context.SupplierCompanies,
                        action: PermissionAction.Edit)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("供应商不存在或无权访问。");
                if (entity.VersionNumber != request.ExpectedVersion)
                    throw new BusinessConcurrencyException("该供应商已被其他用户修改，请刷新后重试。");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            else
            {
                entity = new SupplierCompany
                {
                    Status = SupplierStatusCatalog.Evaluating,
                    VersionNumber = 1
                };
                _accessScope.ApplyOwner(entity);
                await context.SupplierCompanies.AddAsync(entity, cancellationToken);
            }
            entity.Name = name;
            entity.CountryRegion = countryRegion;
            entity.Category = category;
            entity.Website = website;
            entity.MainProducts = mainProducts;
            entity.Notes = notes;
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

        public Task<SupplierRecord> AdmitAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeSupplierStatusAsync(
                id,
                expectedVersion,
                SupplierStatusCatalog.Active,
                PermissionAction.Admit,
                [SupplierStatusCatalog.Evaluating],
                "只有考察中的供应商可以准入。",
                cancellationToken);

        public Task<SupplierRecord> DeactivateAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeSupplierStatusAsync(
                id,
                expectedVersion,
                SupplierStatusCatalog.Inactive,
                PermissionAction.Deactivate,
                [SupplierStatusCatalog.Active, SupplierStatusCatalog.Evaluating, SupplierStatusCatalog.Paused],
                "供应商已经停用。",
                cancellationToken);

        public Task<SupplierRecord> RestoreAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeSupplierStatusAsync(
                id,
                expectedVersion,
                SupplierStatusCatalog.Evaluating,
                PermissionAction.Deactivate,
                [SupplierStatusCatalog.Inactive, SupplierStatusCatalog.Paused],
                "只有暂停或停用的供应商可以恢复考察。",
                cancellationToken);

        private Task<SupplierRecord> ChangeSupplierStatusAsync(
            int id,
            int expectedVersion,
            string nextStatus,
            string action,
            IReadOnlyCollection<string> allowedCurrentStatuses,
            string invalidStateMessage,
            CancellationToken cancellationToken) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplySupplierScope(
                            context.SupplierCompanies,
                            action: action)
                        .FirstOrDefaultAsync(item => item.Id == id, token)
                        ?? throw new KeyNotFoundException("供应商不存在或无权访问。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应商");
                    if (!allowedCurrentStatuses.Contains(entity.Status, StringComparer.Ordinal))
                    {
                        throw new ResourceConflictException(invalidStateMessage);
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    entity.Status = nextStatus;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "供应商", token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplySupplierScope(
                            context.SupplierCompanies,
                            action: PermissionAction.Delete)
                        .FirstOrDefaultAsync(item => item.Id == id, token);
                    if (entity == null)
                    {
                        return false;
                    }

                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应商");
                    var contacts = await context.SupplierContacts
                        .Where(item => item.SupplierCompanyId == id)
                        .ToListAsync(token);
                    int productLinkCount = await context.SupplierProductLinks
                        .CountAsync(item => item.SupplierCompanyId == id, token);
                    int assessmentCount = await context.SupplierAssessments
                        .CountAsync(item => item.SupplierCompanyId == id, token);

                    if (productLinkCount > 0 || assessmentCount > 0)
                    {
                        throw new ResourceConflictException(
                            $"该供应商已有 {productLinkCount} 条供货关系和 {assessmentCount} 条评价，不能删除；请改为停用以保留业务历史。");
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.SupplierContacts.RemoveRange(contacts);
                    context.SupplierCompanies.Remove(entity);
                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new BusinessConcurrencyException("该供应商已被其他用户修改，请刷新后重试。", exception);
                    }
                    catch (DbUpdateException exception)
                    {
                        throw new InfrastructureServiceException(
                            "供应商删除失败，数据库中的关联记录未被修改；请稍后重试。",
                            exception);
                    }
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public async Task<PagedResult<SupplierContactRecord>> QueryContactsAsync(
            int supplierCompanyId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var suppliers = _accessScope.ApplySupplierScopeForPermission(
                context.SupplierCompanies.AsNoTracking(),
                PermissionResourceCatalog.SupplierContacts,
                PermissionAction.View);
            var query = context.SupplierContacts.AsNoTracking()
                .Where(item => item.SupplierCompanyId == supplierCompanyId && suppliers.Any(supplier => supplier.Id == item.SupplierCompanyId))
                .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.Name).ThenBy(item => item.Id);
            int totalCount = await query.CountAsync(cancellationToken);
            var rows = await query
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
                .Take(pageSize)
                .Select(item => new SupplierContactRecord(item.Id, item.SupplierCompanyId, item.Name, item.Title,
                    item.Email, item.Phone, item.InstantMessaging, item.IsPrimary, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<SupplierContactRecord>(rows, totalCount, pageNumber, pageSize);
        }

        public async Task<SupplierContactRecord> SaveContactAsync(
            SupplierContactSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            bool isNew = request.Id <= 0;
            string writeAction = isNew ? PermissionAction.Create : PermissionAction.Edit;
            if (!await _accessScope.ApplySupplierScopeForPermission(
                    context.SupplierCompanies.AsNoTracking(),
                    PermissionResourceCatalog.SupplierContacts,
                    writeAction)
                    .AnyAsync(item => item.Id == request.SupplierCompanyId, cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");
            var entity = request.Id > 0
                ? await context.SupplierContacts.FirstOrDefaultAsync(
                    item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId,
                    cancellationToken) ?? throw new KeyNotFoundException("供应商联系人不存在。")
                : new SupplierContact { SupplierCompanyId = request.SupplierCompanyId, VersionNumber = 1 };
            if (!isNew)
            {
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "供应商联系人");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            if (entity.Id == 0) await context.SupplierContacts.AddAsync(entity, cancellationToken);
            entity.Name = Required(request.Name, "联系人姓名", 100);
            entity.Title = Optional(request.Title, "联系人职务", 100);
            entity.Email = Optional(request.Email, "联系人邮箱", 200);
            entity.Phone = Optional(request.Phone, "联系人电话", 100);
            entity.InstantMessaging = Optional(request.InstantMessaging, "即时通讯", 100);
            entity.UpdatedAt = _clock.UtcNow;
            await SaveWithConcurrencyAsync(context, "供应商联系人", cancellationToken);
            return new(entity.Id, entity.SupplierCompanyId, entity.Name, entity.Title, entity.Email,
                entity.Phone, entity.InstantMessaging, entity.IsPrimary, entity.VersionNumber);
        }

        public Task<SupplierContactRecord> SetPrimaryContactAsync(
            int supplierCompanyId,
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await _accessScope.ApplySupplierScopeForPermission(
                            context.SupplierCompanies.AsNoTracking(),
                            PermissionResourceCatalog.SupplierContacts,
                            PermissionAction.SetPrimary)
                            .AnyAsync(item => item.Id == supplierCompanyId, token))
                    {
                        throw new KeyNotFoundException("供应商不存在或无权访问。");
                    }

                    var entity = await context.SupplierContacts.FirstOrDefaultAsync(
                        item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                        token) ?? throw new KeyNotFoundException("供应商联系人不存在。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应商联系人");
                    if (entity.IsPrimary)
                    {
                        return new SupplierContactRecord(
                            entity.Id, entity.SupplierCompanyId, entity.Name, entity.Title, entity.Email,
                            entity.Phone, entity.InstantMessaging, true, entity.VersionNumber);
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    var previous = await context.SupplierContacts
                        .Where(item => item.SupplierCompanyId == supplierCompanyId && item.Id != id && item.IsPrimary)
                        .ToListAsync(token);
                    foreach (var item in previous)
                    {
                        item.IsPrimary = false;
                        item.VersionNumber++;
                        item.UpdatedAt = _clock.UtcNow;
                    }
                    entity.IsPrimary = true;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "供应商联系人", token);
                    return new SupplierContactRecord(
                        entity.Id, entity.SupplierCompanyId, entity.Name, entity.Title, entity.Email,
                        entity.Phone, entity.InstantMessaging, true, entity.VersionNumber);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteContactAsync(
            int supplierCompanyId,
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await _accessScope.ApplySupplierScopeForPermission(
                            context.SupplierCompanies.AsNoTracking(),
                            PermissionResourceCatalog.SupplierContacts,
                            PermissionAction.Delete)
                            .AnyAsync(item => item.Id == supplierCompanyId, token))
                    {
                        return false;
                    }

                    var entity = await context.SupplierContacts.FirstOrDefaultAsync(
                        item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                        token);
                    if (entity == null) return false;
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应商联系人");
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.SupplierContacts.Remove(entity);
                    await SaveWithConcurrencyAsync(context, "供应商联系人", token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public async Task<PagedResult<SupplierProductOptionRecord>> SearchProductsAsync(
            string? keyword,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            keyword = Clean(keyword);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = context.Products.AsNoTracking();
            if (keyword.Length > 0)
                query = query.Where(item => (item.ProductCode ?? string.Empty).Contains(keyword) ||
                    (item.NameCN ?? string.Empty).Contains(keyword) || (item.NameEN ?? string.Empty).Contains(keyword));
            int totalCount = await query.CountAsync(cancellationToken);
            var rows = await query.OrderBy(item => item.ProductCode).ThenBy(item => item.NameCN).ThenBy(item => item.Id)
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
                .Take(pageSize)
                .Select(item => new SupplierProductOptionRecord(item.Id, item.ProductCode ?? string.Empty,
                    item.NameCN ?? string.Empty, item.NameEN ?? string.Empty))
                .ToListAsync(cancellationToken);
            return new PagedResult<SupplierProductOptionRecord>(rows, totalCount, pageNumber, pageSize);
        }

        public async Task<PagedResult<SupplierProductLinkRecord>> QueryProductLinksAsync(
            int supplierCompanyId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(
                    context,
                    supplierCompanyId,
                    PermissionResourceCatalog.SupplierProductLinks,
                    PermissionAction.View,
                    cancellationToken))
            {
                return new PagedResult<SupplierProductLinkRecord>([], 0, pageNumber, pageSize);
            }
            var query = from link in context.SupplierProductLinks.AsNoTracking()
                        join product in context.Products.AsNoTracking() on link.ProductId equals product.Id
                        where link.SupplierCompanyId == supplierCompanyId
                        orderby product.ProductCode, product.NameCN, link.Id
                        select new { Link = link, Product = product };
            int totalCount = await query.CountAsync(cancellationToken);
            var rows = await query
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
                .Take(pageSize)
                .Select(row => new SupplierProductLinkRecord(row.Link.Id, row.Link.SupplierCompanyId, row.Link.ProductId,
                    row.Product.ProductCode ?? string.Empty, row.Product.NameCN ?? string.Empty, row.Product.NameEN ?? string.Empty,
                    row.Link.SupplierProductCode, row.Link.ReferencePrice, row.Link.Currency, row.Link.LeadTimeDays, row.Link.Status,
                    row.Link.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<SupplierProductLinkRecord>(rows, totalCount, pageNumber, pageSize);
        }

        public async Task<SupplierProductLinkRecord> SaveProductLinkAsync(
            SupplierProductLinkSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ProductId <= 0) throw new ArgumentException("请选择产品。");
            if (request.ReferencePrice < 0) throw new ArgumentException("参考价不能小于零。");
            if (request.LeadTimeDays is < 0 or > 3650) throw new ArgumentException("交期天数必须在 0 至 3650 之间。");
            string currency = CurrencyCodeCatalog.Normalize(request.Currency);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(
                    context,
                    request.SupplierCompanyId,
                    PermissionResourceCatalog.SupplierProductLinks,
                    PermissionAction.Edit,
                    cancellationToken))
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
            if (entity.Id == 0)
            {
                entity.Status = SupplierProductLinkStatusCatalog.Active;
                await context.SupplierProductLinks.AddAsync(entity, cancellationToken);
            }
            entity.ProductId = request.ProductId;
            entity.SupplierProductCode = Clean(request.SupplierProductCode);
            entity.ReferencePrice = request.ReferencePrice;
            entity.Currency = currency;
            entity.LeadTimeDays = request.LeadTimeDays;
            entity.UpdatedAt = _clock.UtcNow;
            await SaveWithConcurrencyAsync(context, "供应产品关联", cancellationToken);
            return ToProductLinkRecord(entity, product);
        }

        public Task<SupplierProductLinkRecord> DeactivateProductLinkAsync(
            int supplierCompanyId,
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeProductLinkStatusAsync(
                supplierCompanyId,
                id,
                expectedVersion,
                SupplierProductLinkStatusCatalog.Inactive,
                [SupplierProductLinkStatusCatalog.Active, SupplierProductLinkStatusCatalog.Candidate, SupplierProductLinkStatusCatalog.Paused],
                "供货关系已经停用。",
                cancellationToken);

        public Task<SupplierProductLinkRecord> RestoreProductLinkAsync(
            int supplierCompanyId,
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeProductLinkStatusAsync(
                supplierCompanyId,
                id,
                expectedVersion,
                SupplierProductLinkStatusCatalog.Active,
                [SupplierProductLinkStatusCatalog.Inactive, SupplierProductLinkStatusCatalog.Paused],
                "只有暂停或停用的供货关系可以恢复。",
                cancellationToken);

        private Task<SupplierProductLinkRecord> ChangeProductLinkStatusAsync(
            int supplierCompanyId,
            int id,
            int expectedVersion,
            string nextStatus,
            IReadOnlyCollection<string> allowedCurrentStatuses,
            string invalidStateMessage,
            CancellationToken cancellationToken) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await CanAccessSupplierAsync(
                            context,
                            supplierCompanyId,
                            PermissionResourceCatalog.SupplierProductLinks,
                            PermissionAction.Deactivate,
                            token))
                    {
                        throw new KeyNotFoundException("供应商不存在或无权访问。");
                    }
                    var entity = await context.SupplierProductLinks.FirstOrDefaultAsync(
                        item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                        token) ?? throw new KeyNotFoundException("供应商产品关联不存在。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应产品关联");
                    if (!allowedCurrentStatuses.Contains(entity.Status, StringComparer.Ordinal))
                    {
                        throw new ResourceConflictException(invalidStateMessage);
                    }
                    var product = await context.Products.AsNoTracking()
                        .SingleAsync(item => item.Id == entity.ProductId, token);
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    entity.Status = nextStatus;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "供应产品关联", token);
                    return ToProductLinkRecord(entity, product);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteProductLinkAsync(
            int supplierCompanyId,
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await CanAccessSupplierAsync(
                            context,
                            supplierCompanyId,
                            PermissionResourceCatalog.SupplierProductLinks,
                            PermissionAction.Delete,
                            token)) return false;
                    var entity = await context.SupplierProductLinks.FirstOrDefaultAsync(
                        item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                        token);
                    if (entity == null) return false;
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "供应产品关联");
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.SupplierProductLinks.Remove(entity);
                    await SaveWithConcurrencyAsync(context, "供应产品关联", token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        private Task<bool> CanAccessSupplierAsync(
            AppDbContext context,
            int supplierCompanyId,
            string resourceKey,
            string action,
            CancellationToken cancellationToken) =>
            _accessScope.ApplySupplierScopeForPermission(
                    context.SupplierCompanies.AsNoTracking(),
                    resourceKey,
                    action)
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
        private static string Required(string value, string field, int maximumLength)
        {
            string normalized = Optional(value, field, maximumLength);
            return normalized.Length == 0 ? throw new ArgumentException($"{field}不能为空。") : normalized;
        }

        private static string Optional(string? value, string field, int maximumLength)
        {
            string normalized = Clean(value);
            return normalized.Length > maximumLength
                ? throw new ArgumentException($"{field}不能超过 {maximumLength} 个字符。")
                : normalized;
        }

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
            catch (DbUpdateException exception) when (
                entityName.Contains("联系人", StringComparison.Ordinal) &&
                RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                throw new BusinessConcurrencyException(
                    $"该{entityName}的主要联系人已被其他用户调整，请刷新后重试。",
                    exception);
            }
        }
    }
}
