using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Crm
{
    public sealed class CrmService : ICrmService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public CrmService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<IReadOnlyList<CrmCustomerRecord>> ListCustomersAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                .OrderBy(item => item.Name)
                .Select(item => new CrmCustomerRecord(item.Id, item.Name, item.CountryRegion, item.Website,
                    item.Status, item.Source, item.Notes, item.LinkedDocumentCustomerId, item.VersionNumber))
                .ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<CrmCustomerRecord>> QueryCustomersAsync(
            string keyword, string status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 10, 100);
            keyword = Clean(keyword);
            status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            if (keyword.Length > 0)
            {
                query = query.Where(item => item.Name.Contains(keyword) || item.CountryRegion.Contains(keyword) ||
                    item.Website.Contains(keyword) || item.Source.Contains(keyword) || item.Notes.Contains(keyword));
            }
            if (status.Length > 0)
            {
                query = query.Where(item => item.Status == status);
            }

            int totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new CrmCustomerRecord(item.Id, item.Name, item.CountryRegion, item.Website,
                    item.Status, item.Source, item.Notes, item.LinkedDocumentCustomerId, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<CrmCustomerRecord>(items, totalCount, pageNumber, pageSize);
        }

        public async Task<CrmCustomerRecord> SaveCustomerAsync(CrmCustomerSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string name = Required(request.Name, "客户名称");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            CrmCustomer entity;
            if (request.Id > 0)
            {
                if (request.ExpectedVersion <= 0)
                    throw new BusinessConcurrencyException("保存现有 CRM 客户时必须提供版本号，请刷新后重试。");
                entity = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("CRM 客户不存在或无权访问。");
                if (entity.VersionNumber != request.ExpectedVersion)
                    throw new BusinessConcurrencyException("该 CRM 客户已被其他用户修改，请刷新后重试。");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            else
            {
                entity = new CrmCustomer { VersionNumber = 1 };
                _accessScope.ApplyOwner(entity);
                await context.CrmCustomers.AddAsync(entity, cancellationToken);
            }

            entity.Name = name;
            entity.CountryRegion = Clean(request.CountryRegion);
            entity.Website = Clean(request.Website);
            entity.Status = string.IsNullOrWhiteSpace(request.Status) ? "潜在客户" : request.Status.Trim();
            entity.Source = Clean(request.Source);
            entity.Notes = Clean(request.Notes);
            entity.LinkedDocumentCustomerId = request.LinkedDocumentCustomerId;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该 CRM 客户已被其他用户修改，请刷新后重试。", exception);
            }
            return new(entity.Id, entity.Name, entity.CountryRegion, entity.Website, entity.Status,
                entity.Source, entity.Notes, entity.LinkedDocumentCustomerId, entity.VersionNumber);
        }

        public async Task<bool> DeleteCustomerAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null) return false;
            if (await _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps.AsNoTracking())
                    .AnyAsync(item => item.CrmCustomerId == id, cancellationToken))
            {
                throw new InvalidOperationException("该客户已有跟进历史，不能直接删除；请改为停用状态以保留业务记录。");
            }
            if (await _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities.AsNoTracking())
                    .AnyAsync(item => item.CrmCustomerId == id, cancellationToken))
                throw new InvalidOperationException("该客户已有商机记录，不能直接删除；请改为暂停或已流失状态。");

            context.CrmCustomers.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<int> UpdateCustomerStatusAsync(
            IReadOnlyList<int> ids, string status, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            int[] normalizedIds = ids.Where(id => id > 0).Distinct().Take(500).ToArray();
            status = Clean(status);
            if (normalizedIds.Length == 0) throw new ArgumentException("请选择 CRM 客户。");
            if (status is not ("潜在客户" or "跟进中" or "已成交" or "暂停" or "已流失"))
                throw new ArgumentException("CRM 客户状态无效。");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers)
                .Where(item => normalizedIds.Contains(item.Id)).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.Status = status;
                row.VersionNumber++;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("部分 CRM 客户已被其他用户修改，请刷新列表后重试。", exception);
            }
            return rows.Count;
        }

        public async Task<CrmEmailVariableDraft> GetEmailVariableDraftAsync(
            int crmCustomerId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customer = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == crmCustomerId, cancellationToken)
                ?? throw new KeyNotFoundException("CRM 客户不存在或无权访问。");
            var contact = await context.CrmContacts.AsNoTracking().Where(item => item.CrmCustomerId == crmCustomerId)
                .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            var user = _accessScope.CurrentUser;
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CustomerName"] = customer.Name,
                ["ContactName"] = contact?.Name ?? string.Empty,
                ["CompanyName"] = user?.CompanyScope ?? string.Empty,
                ["ProductName"] = string.Empty,
                ["QuotationNo"] = string.Empty,
                ["SenderName"] = user?.Username ?? string.Empty,
                ["Today"] = DateTimeOffset.Now.ToString("yyyy-MM-dd")
            };
            return new CrmEmailVariableDraft(customer.Id, contact?.Id, contact?.Email ?? string.Empty, variables);
        }

        public async Task<IReadOnlyList<CrmContactRecord>> ListContactsAsync(int crmCustomerId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            return await context.CrmContacts.AsNoTracking()
                .Where(item => item.CrmCustomerId == crmCustomerId && customers.Any(customer => customer.Id == item.CrmCustomerId))
                .OrderByDescending(item => item.IsPrimary)
                .ThenBy(item => item.Name)
                .Select(item => new CrmContactRecord(item.Id, item.CrmCustomerId, item.Name, item.Title,
                    item.Email, item.Phone, item.InstantMessaging, item.IsPrimary, item.VersionNumber))
                .ToListAsync(cancellationToken);
        }

        public async Task<CrmContactRecord> SaveContactAsync(CrmContactSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                    .AnyAsync(item => item.Id == request.CrmCustomerId, cancellationToken))
            {
                throw new KeyNotFoundException("CRM 客户不存在或无权访问。");
            }

            bool isNew = request.Id <= 0;
            CrmContact entity = request.Id > 0
                ? await context.CrmContacts.FirstOrDefaultAsync(item => item.Id == request.Id && item.CrmCustomerId == request.CrmCustomerId, cancellationToken)
                    ?? throw new KeyNotFoundException("联系人不存在。")
                : new CrmContact { CrmCustomerId = request.CrmCustomerId, VersionNumber = 1 };
            if (!isNew)
            {
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "联系人");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            if (entity.Id == 0) await context.CrmContacts.AddAsync(entity, cancellationToken);
            entity.Name = Required(request.Name, "联系人姓名");
            entity.Title = Clean(request.Title);
            entity.Email = Clean(request.Email);
            entity.Phone = Clean(request.Phone);
            entity.InstantMessaging = Clean(request.InstantMessaging);
            entity.IsPrimary = request.IsPrimary;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            if (entity.IsPrimary)
            {
                var previousPrimaryContacts = await context.CrmContacts
                    .Where(item => item.CrmCustomerId == request.CrmCustomerId && item.Id != entity.Id && item.IsPrimary)
                    .ToListAsync(cancellationToken);
                foreach (var previousPrimary in previousPrimaryContacts)
                {
                    previousPrimary.IsPrimary = false;
                    previousPrimary.VersionNumber++;
                    previousPrimary.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            await SaveWithConcurrencyAsync(context, "联系人", cancellationToken);
            return new(entity.Id, entity.CrmCustomerId, entity.Name, entity.Title, entity.Email,
                entity.Phone, entity.InstantMessaging, entity.IsPrimary, entity.VersionNumber);
        }

        public async Task<bool> DeleteContactAsync(int crmCustomerId, int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                    .AnyAsync(item => item.Id == crmCustomerId, cancellationToken)) return false;
            var entity = await context.CrmContacts.FirstOrDefaultAsync(
                item => item.Id == id && item.CrmCustomerId == crmCustomerId,
                cancellationToken);
            if (entity == null) return false;
            context.CrmContacts.Remove(entity);
            await SaveWithConcurrencyAsync(context, "联系人", cancellationToken);
            return true;
        }

        public async Task<IReadOnlyList<CrmFollowUpRecord>> ListFollowUpsAsync(
            int? crmCustomerId, bool includeCompleted, int limit, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps.AsNoTracking());
            if (crmCustomerId is > 0) query = query.Where(item => item.CrmCustomerId == crmCustomerId.Value);
            if (!includeCompleted) query = query.Where(item => !item.IsCompleted);
            var rows = await query
                .OrderByDescending(item => item.Id)
                .Take(1000)
                .Select(item => new CrmFollowUpRecord(
                    item.Id, item.CrmCustomerId,
                    context.CrmCustomers.Where(customer => customer.Id == item.CrmCustomerId).Select(customer => customer.Name).FirstOrDefault() ?? string.Empty,
                    item.CrmContactId,
                    context.CrmContacts.Where(contact => contact.Id == item.CrmContactId).Select(contact => contact.Name).FirstOrDefault() ?? string.Empty,
                    item.Type, item.Summary, item.NextAction, item.FollowedUpAt, item.NextFollowUpAt,
                    item.IsCompleted, item.CreatedAt, item.UpdatedAt, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return rows
                .OrderBy(item => item.IsCompleted)
                .ThenBy(item => item.NextFollowUpAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(item => item.FollowedUpAt)
                .Take(Math.Clamp(limit, 1, 200))
                .ToArray();
        }

        public async Task<CrmFollowUpRecord> SaveFollowUpAsync(CrmFollowUpSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customer = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers)
                .FirstOrDefaultAsync(item => item.Id == request.CrmCustomerId, cancellationToken)
                ?? throw new KeyNotFoundException("CRM 客户不存在或无权访问。");
            CrmContact contact = null;
            if (request.CrmContactId is > 0)
            {
                contact = await context.CrmContacts.FirstOrDefaultAsync(
                    item => item.Id == request.CrmContactId && item.CrmCustomerId == request.CrmCustomerId,
                    cancellationToken) ?? throw new KeyNotFoundException("联系人不存在。");
            }

            bool isNew = request.Id <= 0;
            CrmFollowUp entity = request.Id > 0
                ? await _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("跟进记录不存在或无权访问。")
                : new CrmFollowUp { VersionNumber = 1 };
            if (!isNew)
            {
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "跟进记录");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            if (entity.Id == 0)
            {
                _accessScope.ApplyOwner(entity);
                await context.CrmFollowUps.AddAsync(entity, cancellationToken);
            }
            entity.CrmCustomerId = request.CrmCustomerId;
            entity.CrmContactId = request.CrmContactId;
            entity.Type = string.IsNullOrWhiteSpace(request.Type) ? "其他" : request.Type.Trim();
            entity.Summary = Required(request.Summary, "跟进摘要");
            entity.NextAction = Clean(request.NextAction);
            entity.FollowedUpAt = request.FollowedUpAt ?? (entity.Id == 0 ? DateTimeOffset.UtcNow : entity.FollowedUpAt);
            entity.NextFollowUpAt = request.NextFollowUpAt;
            entity.IsCompleted = request.IsCompleted;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithConcurrencyAsync(context, "跟进记录", cancellationToken);
            return new(entity.Id, entity.CrmCustomerId, customer.Name, entity.CrmContactId,
                contact?.Name ?? string.Empty, entity.Type, entity.Summary, entity.NextAction,
                entity.FollowedUpAt, entity.NextFollowUpAt, entity.IsCompleted, entity.CreatedAt,
                entity.UpdatedAt, entity.VersionNumber);
        }

        public async Task<bool> DeleteFollowUpAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null) return false;
            context.CrmFollowUps.Remove(entity);
            await SaveWithConcurrencyAsync(context, "跟进记录", cancellationToken);
            return true;
        }

        public async Task<CrmDashboardRecord> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            var followUps = _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps.AsNoTracking());
            var now = DateTimeOffset.UtcNow;
            var sevenDaysLater = now.AddDays(7);

            int customerCount = await customers.CountAsync(cancellationToken);
            int contactCount = await context.CrmContacts.AsNoTracking()
                .CountAsync(contact => customers.Any(customer => customer.Id == contact.CrmCustomerId), cancellationToken);
            var pendingRows = await followUps
                .Where(item => !item.IsCompleted)
                .Select(item => item.NextFollowUpAt)
                .ToListAsync(cancellationToken);
            int pendingCount = pendingRows.Count;
            int overdueCount = pendingRows.Count(value => value.HasValue && value.Value < now);
            int dueNextSevenDays = pendingRows.Count(value =>
                value.HasValue && value.Value >= now && value.Value <= sevenDaysLater);
            var upcoming = await ListFollowUpsAsync(null, false, 8, cancellationToken);

            return new CrmDashboardRecord(
                customerCount,
                contactCount,
                pendingCount,
                overdueCount,
                dueNextSevenDays,
                upcoming);
        }

        private static string Required(string value, string fieldName)
        {
            string normalized = Clean(value);
            return normalized.Length == 0 ? throw new ArgumentException($"{fieldName}不能为空。") : normalized;
        }

        private static string Clean(string value) => (value ?? string.Empty).Trim();

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
        }
    }
}
