using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Crm
{
    public sealed class CrmService : ICrmService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly IBusinessClock _clock;

        public CrmService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<PagedResult<CrmCustomerRecord>> QueryCustomersAsync(
            string? keyword, string? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 10, 100);
            keyword = Clean(keyword);
            status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            if (keyword.Length > 0)
            {
                query = query.ApplyKeywordSearch(
                    context,
                    keyword,
                    item => item.Name,
                    item => item.CountryRegion,
                    item => item.Website,
                    item => item.Source,
                    item => item.Notes);
            }
            if (status.Length > 0)
            {
                query = query.Where(item => item.Status == status);
            }

            int totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name)
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
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
                entity = await _accessScope.ApplyCrmCustomerScope(
                        context.CrmCustomers,
                        action: PermissionAction.Edit)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
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
            if (request.Id <= 0)
            {
                entity.Status = CrmCustomerStatusCatalog.Prospect;
            }
            entity.Source = Clean(request.Source);
            entity.Notes = Clean(request.Notes);
            entity.LinkedDocumentCustomerId = await ResolveLinkedDocumentCustomerAsync(
                context,
                request.LinkedDocumentCustomerId,
                cancellationToken);
            entity.UpdatedAt = _clock.UtcNow;
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

        public Task<CrmCustomerRecord> DeactivateCustomerAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeCustomerStatusAsync(
                id,
                expectedVersion,
                CrmCustomerStatusCatalog.Paused,
                "客户已处于暂停状态。",
                cancellationToken);

        public Task<CrmCustomerRecord> RestoreCustomerAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeCustomerStatusAsync(
                id,
                expectedVersion,
                CrmCustomerStatusCatalog.InProgress,
                "只有暂停或流失客户可以恢复。",
                cancellationToken,
                requireInactiveState: true);

        private Task<CrmCustomerRecord> ChangeCustomerStatusAsync(
            int id,
            int expectedVersion,
            string nextStatus,
            string invalidStateMessage,
            CancellationToken cancellationToken,
            bool requireInactiveState = false) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplyCrmCustomerScope(
                            context.CrmCustomers,
                            action: PermissionAction.Deactivate)
                        .FirstOrDefaultAsync(item => item.Id == id, token)
                        ?? throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "CRM 客户");
                    bool isInactive = entity.Status is CrmCustomerStatusCatalog.Paused or CrmCustomerStatusCatalog.Lost;
                    if (requireInactiveState != isInactive || entity.Status == nextStatus)
                    {
                        throw new ResourceConflictException(invalidStateMessage);
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    entity.Status = nextStatus;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "CRM 客户", token);
                    return new CrmCustomerRecord(
                        entity.Id, entity.Name, entity.CountryRegion, entity.Website, entity.Status,
                        entity.Source, entity.Notes, entity.LinkedDocumentCustomerId, entity.VersionNumber);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteCustomerAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplyCrmCustomerScope(
                            context.CrmCustomers,
                            action: PermissionAction.Delete)
                        .FirstOrDefaultAsync(item => item.Id == id, token);
                    if (entity == null) return false;

                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "CRM 客户");

                    // Deletion safety must not depend on the caller's filtered
                    // view. Hidden and archived history still prevents
                    // destructive removal so audit records keep a valid
                    // customer relationship.
                    if (await context.CrmFollowUps.AsNoTracking()
                            .AnyAsync(item => item.CrmCustomerId == id, token))
                    {
                        throw new ResourceConflictException(
                            "该客户已有跟进历史，不能直接删除；请改为停用状态以保留业务记录。");
                    }

                    if (await context.SalesOpportunities.AsNoTracking()
                            .AnyAsync(item => item.CrmCustomerId == id, token))
                    {
                        throw new ResourceConflictException(
                            "该客户已有商机历史，不能直接删除；请改为暂停或已流失状态以保留业务记录。");
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.CrmCustomers.Remove(entity);
                    await SaveWithConcurrencyAsync(context, "CRM 客户", token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public async Task<int> UpdateCustomerStatusAsync(
            IReadOnlyList<int> ids, string status, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            int[] normalizedIds = ids.Where(id => id > 0).Distinct().ToArray();
            status = Clean(status);
            if (normalizedIds.Length == 0) throw new ServiceValidationException("请选择 CRM 客户。");
            if (normalizedIds.Length > 500)
                throw new ServiceValidationException("单次最多修改 500 家 CRM 客户，请分批提交；超出部分不会被静默忽略。");
            status = NormalizeCustomerStatus(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await _accessScope.ApplyCrmCustomerScope(
                    context.CrmCustomers,
                    action: PermissionAction.Deactivate)
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
                ?? throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
            var contact = await context.CrmContacts.AsNoTracking().Where(item => item.CrmCustomerId == crmCustomerId)
                .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            var user = _accessScope.CurrentUser;
            string companyName = string.IsNullOrWhiteSpace(user?.CompanyScope)
                ? string.Empty
                : await context.OrganizationCompanies.AsNoTracking()
                    .Where(item => item.Code == user.CompanyScope && item.IsActive)
                    .Select(item => item.Name)
                    .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CustomerName"] = customer.Name,
                ["ContactName"] = contact?.Name ?? string.Empty,
                ["CompanyName"] = companyName,
                ["ProductName"] = string.Empty,
                ["QuotationNo"] = string.Empty,
                ["SenderName"] = user?.Username ?? string.Empty,
                ["Today"] = _clock.Today.ToString("yyyy-MM-dd")
            };
            return new CrmEmailVariableDraft(customer.Id, contact?.Id, contact?.Email ?? string.Empty, variables);
        }

        public async Task<PagedResult<CrmContactRecord>> QueryContactsAsync(
            int crmCustomerId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customers = _accessScope.ApplyCrmCustomerScopeForPermission(
                context.CrmCustomers.AsNoTracking(),
                PermissionResourceCatalog.CrmContacts,
                PermissionAction.View);
            var query = context.CrmContacts.AsNoTracking()
                .Where(item => item.CrmCustomerId == crmCustomerId && customers.Any(customer => customer.Id == item.CrmCustomerId))
                .OrderByDescending(item => item.IsPrimary)
                .ThenBy(item => item.Name);
            int totalCount = await query.CountAsync(cancellationToken);
            var rows = await query
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
                .Take(pageSize)
                .Select(item => new CrmContactRecord(item.Id, item.CrmCustomerId, item.Name, item.Title,
                    item.Email, item.Phone, item.InstantMessaging, item.IsPrimary, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<CrmContactRecord>(rows, totalCount, pageNumber, pageSize);
        }

        public async Task<CrmContactRecord> SaveContactAsync(CrmContactSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            bool isNew = request.Id <= 0;
            string writeAction = isNew ? PermissionAction.Create : PermissionAction.Edit;
            if (!await _accessScope.ApplyCrmCustomerScopeForPermission(
                    context.CrmCustomers.AsNoTracking(),
                    PermissionResourceCatalog.CrmContacts,
                    writeAction)
                    .AnyAsync(item => item.Id == request.CrmCustomerId, cancellationToken))
            {
                throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
            }

            CrmContact entity = request.Id > 0
                ? await context.CrmContacts.FirstOrDefaultAsync(item => item.Id == request.Id && item.CrmCustomerId == request.CrmCustomerId, cancellationToken)
                    ?? throw new ResourceNotFoundException("联系人不存在。")
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
            entity.UpdatedAt = _clock.UtcNow;
            await SaveWithConcurrencyAsync(context, "联系人", cancellationToken);
            return new(entity.Id, entity.CrmCustomerId, entity.Name, entity.Title, entity.Email,
                entity.Phone, entity.InstantMessaging, entity.IsPrimary, entity.VersionNumber);
        }

        public Task<CrmContactRecord> SetPrimaryContactAsync(
            int crmCustomerId,
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    bool customerAccessible = await _accessScope.ApplyCrmCustomerScopeForPermission(
                            context.CrmCustomers.AsNoTracking(),
                            PermissionResourceCatalog.CrmContacts,
                            PermissionAction.SetPrimary)
                        .AnyAsync(item => item.Id == crmCustomerId, token);
                    if (!customerAccessible)
                    {
                        throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
                    }

                    var entity = await context.CrmContacts
                        .FirstOrDefaultAsync(item => item.Id == id && item.CrmCustomerId == crmCustomerId, token)
                        ?? throw new ResourceNotFoundException("联系人不存在。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "联系人");
                    if (entity.IsPrimary)
                    {
                        return new CrmContactRecord(
                            entity.Id, entity.CrmCustomerId, entity.Name, entity.Title, entity.Email,
                            entity.Phone, entity.InstantMessaging, true, entity.VersionNumber);
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    var previousPrimaryContacts = await context.CrmContacts
                        .Where(item => item.CrmCustomerId == crmCustomerId && item.Id != id && item.IsPrimary)
                        .ToListAsync(token);
                    foreach (var previous in previousPrimaryContacts)
                    {
                        previous.IsPrimary = false;
                        previous.VersionNumber++;
                        previous.UpdatedAt = _clock.UtcNow;
                    }
                    entity.IsPrimary = true;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "联系人", token);
                    return new CrmContactRecord(
                        entity.Id, entity.CrmCustomerId, entity.Name, entity.Title, entity.Email,
                        entity.Phone, entity.InstantMessaging, true, entity.VersionNumber);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteContactAsync(
            int crmCustomerId,
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await _accessScope.ApplyCrmCustomerScopeForPermission(
                            context.CrmCustomers.AsNoTracking(),
                            PermissionResourceCatalog.CrmContacts,
                            PermissionAction.Delete)
                            .AnyAsync(item => item.Id == crmCustomerId, token))
                    {
                        return false;
                    }

                    var entity = await context.CrmContacts.FirstOrDefaultAsync(
                        item => item.Id == id && item.CrmCustomerId == crmCustomerId,
                        token);
                    if (entity == null) return false;

                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "联系人");
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.CrmContacts.Remove(entity);
                    await SaveWithConcurrencyAsync(context, "联系人", token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public async Task<PagedResult<CrmFollowUpRecord>> QueryFollowUpsAsync(
            int? crmCustomerId, bool includeCompleted, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps.AsNoTracking());
            if (crmCustomerId is > 0) query = query.Where(item => item.CrmCustomerId == crmCustomerId.Value);
            if (!includeCompleted) query = query.Where(item => !item.IsCompleted);
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var totalCount = await query.CountAsync(cancellationToken);
            var rows = await query
                .OrderBy(item => item.IsCompleted)
                .ThenBy(item => item.NextFollowUpAt == null)
                .ThenBy(item => item.NextFollowUpAt)
                .ThenByDescending(item => item.FollowedUpAt)
                .ThenByDescending(item => item.Id)
                .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
                .Take(pageSize)
                .Select(item => new CrmFollowUpRecord(
                    item.Id, item.CrmCustomerId,
                    context.CrmCustomers.Where(customer => customer.Id == item.CrmCustomerId).Select(customer => customer.Name).FirstOrDefault() ?? string.Empty,
                    item.CrmContactId,
                    context.CrmContacts.Where(contact => contact.Id == item.CrmContactId).Select(contact => contact.Name).FirstOrDefault() ?? string.Empty,
                    item.Type, item.Summary, item.NextAction, item.FollowedUpAt, item.NextFollowUpAt,
                    item.IsCompleted, item.CreatedAt, item.UpdatedAt, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<CrmFollowUpRecord>(rows, totalCount, pageNumber, pageSize);
        }

        public async Task<CrmFollowUpRecord> SaveFollowUpAsync(CrmFollowUpSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            bool isNew = request.Id <= 0;
            string writeAction = isNew ? PermissionAction.Create : PermissionAction.Edit;
            var customer = await _accessScope.ApplyCrmCustomerScopeForPermission(
                    context.CrmCustomers,
                    PermissionResourceCatalog.CrmFollowUps,
                    writeAction)
                .FirstOrDefaultAsync(item => item.Id == request.CrmCustomerId, cancellationToken)
                ?? throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");
            CrmContact? contact = null;
            if (request.CrmContactId is > 0)
            {
                contact = await context.CrmContacts.FirstOrDefaultAsync(
                    item => item.Id == request.CrmContactId && item.CrmCustomerId == request.CrmCustomerId,
                    cancellationToken) ?? throw new ResourceNotFoundException("联系人不存在。");
            }

            CrmFollowUp entity = request.Id > 0
                ? await _accessScope.ApplyCrmFollowUpScope(
                        context.CrmFollowUps,
                        action: PermissionAction.Edit)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new ResourceNotFoundException("跟进记录不存在或无权访问。")
                : new CrmFollowUp { VersionNumber = 1 };
            if (!isNew)
            {
                if (entity.CrmCustomerId != request.CrmCustomerId)
                {
                    throw new ServiceValidationException("已有跟进记录不能更换所属客户，请新建一条跟进记录。");
                }
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
            entity.Type = NormalizeFollowUpType(request.Type);
            entity.Summary = Required(request.Summary, "跟进摘要");
            entity.NextAction = Clean(request.NextAction);
            entity.FollowedUpAt = request.FollowedUpAt ?? (entity.Id == 0 ? _clock.UtcNow : entity.FollowedUpAt);
            entity.NextFollowUpAt = request.NextFollowUpAt;
            entity.UpdatedAt = _clock.UtcNow;
            await SaveWithConcurrencyAsync(context, "跟进记录", cancellationToken);
            return new(entity.Id, entity.CrmCustomerId, customer.Name, entity.CrmContactId,
                contact?.Name ?? string.Empty, entity.Type, entity.Summary, entity.NextAction,
                entity.FollowedUpAt, entity.NextFollowUpAt, entity.IsCompleted, entity.CreatedAt,
                entity.UpdatedAt, entity.VersionNumber);
        }

        public Task<CrmFollowUpRecord> CompleteFollowUpAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeFollowUpCompletionAsync(id, expectedVersion, true, PermissionAction.Complete, cancellationToken);

        public Task<CrmFollowUpRecord> RestoreFollowUpAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            ChangeFollowUpCompletionAsync(id, expectedVersion, false, PermissionAction.Restore, cancellationToken);

        public Task<CrmFollowUpRecord> TransferFollowUpAsync(
            int id,
            CrmFollowUpTransferRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplyCrmFollowUpScope(
                            context.CrmFollowUps,
                            action: PermissionAction.Assign)
                        .FirstOrDefaultAsync(item => item.Id == id, token)
                        ?? throw new ResourceNotFoundException("跟进记录不存在或无权转移。");
                    EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "跟进记录");
                    var customer = await _accessScope.ApplyCrmCustomerScopeForPermission(
                            context.CrmCustomers,
                            PermissionResourceCatalog.CrmFollowUps,
                            PermissionAction.Assign)
                        .FirstOrDefaultAsync(item => item.Id == request.CrmCustomerId, token)
                        ?? throw new ResourceNotFoundException("目标 CRM 客户不存在或无权访问。");
                    CrmContact? contact = null;
                    if (request.CrmContactId is > 0)
                    {
                        contact = await context.CrmContacts.AsNoTracking()
                            .FirstOrDefaultAsync(item => item.Id == request.CrmContactId &&
                                item.CrmCustomerId == customer.Id, token)
                            ?? throw new ResourceNotFoundException("目标联系人不存在或不属于目标客户。");
                    }
                    if (entity.CrmCustomerId == customer.Id && entity.CrmContactId == request.CrmContactId)
                    {
                        throw new ResourceConflictException("跟进记录已属于所选客户和联系人。");
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                    entity.CrmCustomerId = customer.Id;
                    entity.CrmContactId = request.CrmContactId;
                    entity.OwnerUserId = customer.OwnerUserId;
                    entity.DepartmentId = customer.DepartmentId;
                    entity.CompanyScope = customer.CompanyScope;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "跟进记录", token);
                    return ToFollowUpRecord(entity, customer.Name, contact?.Name ?? string.Empty);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private Task<CrmFollowUpRecord> ChangeFollowUpCompletionAsync(
            int id,
            int expectedVersion,
            bool completed,
            string action,
            CancellationToken cancellationToken) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps, action: action)
                        .FirstOrDefaultAsync(item => item.Id == id, token)
                        ?? throw new ResourceNotFoundException("跟进记录不存在或无权操作。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "跟进记录");
                    if (entity.IsCompleted == completed)
                    {
                        throw new ResourceConflictException(completed ? "跟进记录已经完成。" : "跟进记录尚未完成。");
                    }
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    entity.IsCompleted = completed;
                    entity.VersionNumber++;
                    entity.UpdatedAt = _clock.UtcNow;
                    await SaveWithConcurrencyAsync(context, "跟进记录", token);
                    string customerName = await context.CrmCustomers.AsNoTracking()
                        .Where(item => item.Id == entity.CrmCustomerId)
                        .Select(item => item.Name)
                        .SingleOrDefaultAsync(token) ?? string.Empty;
                    string contactName = entity.CrmContactId is > 0
                        ? await context.CrmContacts.AsNoTracking()
                            .Where(item => item.Id == entity.CrmContactId)
                            .Select(item => item.Name)
                            .SingleOrDefaultAsync(token) ?? string.Empty
                        : string.Empty;
                    return ToFollowUpRecord(entity, customerName, contactName);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        private static CrmFollowUpRecord ToFollowUpRecord(
            CrmFollowUp entity,
            string customerName,
            string contactName) =>
            new(entity.Id, entity.CrmCustomerId, customerName, entity.CrmContactId,
                contactName, entity.Type, entity.Summary, entity.NextAction,
                entity.FollowedUpAt, entity.NextFollowUpAt, entity.IsCompleted,
                entity.CreatedAt, entity.UpdatedAt, entity.VersionNumber);

        public Task<bool> DeleteFollowUpAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await _accessScope.ApplyCrmFollowUpScope(
                            context.CrmFollowUps,
                            action: PermissionAction.Delete)
                        .FirstOrDefaultAsync(item => item.Id == id, token);
                    if (entity == null) return false;

                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "跟进记录");
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.CrmFollowUps.Remove(entity);
                    await SaveWithConcurrencyAsync(context, "跟进记录", token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<CrmDashboardRecord> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                LoadDashboardSnapshotAsync,
                IsolationLevel.Serializable,
                cancellationToken);

        private async Task<CrmDashboardRecord> LoadDashboardSnapshotAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            var followUps = _accessScope.ApplyCrmFollowUpScope(context.CrmFollowUps.AsNoTracking());
            var now = _clock.UtcNow;
            var sevenDaysLater = now.AddDays(7);

            int customerCount = await customers.CountAsync(cancellationToken);
            int contactCount = await context.CrmContacts.AsNoTracking()
                .CountAsync(contact => customers.Any(customer => customer.Id == contact.CrmCustomerId), cancellationToken);
            var pendingFollowUps = followUps.Where(item => !item.IsCompleted);
            int pendingCount = await pendingFollowUps.CountAsync(cancellationToken);
            int overdueCount = await pendingFollowUps.CountAsync(
                item => item.NextFollowUpAt.HasValue && item.NextFollowUpAt.Value < now,
                cancellationToken);
            int dueNextSevenDays = await pendingFollowUps.CountAsync(
                item => item.NextFollowUpAt.HasValue &&
                        item.NextFollowUpAt.Value >= now &&
                        item.NextFollowUpAt.Value <= sevenDaysLater,
                cancellationToken);
            // Keep dashboard aggregates and the upcoming list on the same
            // snapshot/context.  Calling the public list method here opened a
            // second context and could show a different state mid-request.
            var upcoming = await pendingFollowUps
                .OrderBy(item => item.NextFollowUpAt == null)
                .ThenBy(item => item.NextFollowUpAt)
                .ThenByDescending(item => item.FollowedUpAt)
                .ThenByDescending(item => item.Id)
                .Take(8)
                .Select(item => new CrmFollowUpRecord(
                    item.Id,
                    item.CrmCustomerId,
                    context.CrmCustomers
                        .Where(customer => customer.Id == item.CrmCustomerId)
                        .Select(customer => customer.Name)
                        .FirstOrDefault() ?? string.Empty,
                    item.CrmContactId,
                    context.CrmContacts
                        .Where(contact => contact.Id == item.CrmContactId)
                        .Select(contact => contact.Name)
                        .FirstOrDefault() ?? string.Empty,
                    item.Type,
                    item.Summary,
                    item.NextAction,
                    item.FollowedUpAt,
                    item.NextFollowUpAt,
                    item.IsCompleted,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.VersionNumber))
                .ToListAsync(cancellationToken);

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
            return normalized.Length == 0 ? throw new ServiceValidationException($"{fieldName}不能为空。") : normalized;
        }

        private async Task<int?> ResolveLinkedDocumentCustomerAsync(
            AppDbContext context,
            int? linkedCustomerId,
            CancellationToken cancellationToken)
        {
            if (linkedCustomerId is not > 0)
            {
                return null;
            }

            bool accessible = await _accessScope
                .ApplyCustomerScope(context.Customers.AsNoTracking())
                .AnyAsync(item => item.Id == linkedCustomerId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (accessible)
            {
                return linkedCustomerId.Value;
            }

            bool exists = await context.Customers.AsNoTracking()
                .AnyAsync(item => item.Id == linkedCustomerId.Value, cancellationToken)
                .ConfigureAwait(false);
            throw exists
                ? new PermissionDeniedException("关联的单证客户不在当前账号的数据范围内。")
                : new ResourceNotFoundException("关联的单证客户不存在。");
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();

        private static string NormalizeCustomerStatus(string? value)
        {
            try
            {
                return CrmCustomerStatusCatalog.Normalize(value);
            }
            catch (ArgumentException exception)
            {
                throw new ServiceValidationException(exception.Message, exception);
            }
        }

        private static string NormalizeFollowUpType(string? value)
        {
            try
            {
                return CrmFollowUpTypeCatalog.Normalize(value);
            }
            catch (ArgumentException exception)
            {
                throw new ServiceValidationException(exception.Message, exception);
            }
        }

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
