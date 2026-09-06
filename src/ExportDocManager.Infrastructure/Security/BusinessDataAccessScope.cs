using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security
{
    public sealed class BusinessDataAccessScope
    {
        private readonly DatabaseConnectionSettings _settings;
        private readonly ICurrentUserContext? _currentUserContext;

        public BusinessDataAccessScope(DatabaseConnectionSettings settings)
            : this(settings, null)
        {
        }

        public BusinessDataAccessScope(
            DatabaseConnectionSettings settings,
            ICurrentUserContext? currentUserContext)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _currentUserContext = currentUserContext;
        }

        public User? CurrentUser => _currentUserContext?.CurrentUser;

        public bool UsesPostgreSql => DatabaseModeHelper.UsesPostgreSql(_settings);

        public bool ShouldFilterBusinessData(User? user = null)
        {
            user ??= _currentUserContext?.CurrentUser;
            return DatabaseModeHelper.UsesPostgreSql(_settings) &&
                   !CanViewAllBusinessData(user);
        }

        public static bool CanViewAllBusinessData(User? user)
        {
            return string.Equals(user?.Role?.Trim(), UserRoleCatalog.Admin, StringComparison.OrdinalIgnoreCase);
        }

        public bool HasPermission(string resourceKey, string action, User? user = null)
        {
            user ??= _currentUserContext?.CurrentUser;
            return PermissionDataScope.IsKnown(ResolveDataScope(user, resourceKey, action));
        }

        public void DemandPermission(string resourceKey, string action, User? user = null)
        {
            if (!HasPermission(resourceKey, action, user))
            {
                throw new PermissionDeniedException($"当前账号没有权限执行 {resourceKey}/{action}。");
            }
        }

        public bool IsOwnedByCurrentUser(int? ownerUserId, User? user = null)
        {
            user ??= _currentUserContext?.CurrentUser;
            return !DatabaseModeHelper.UsesPostgreSql(_settings) ||
                   user?.Id > 0 && ownerUserId == user.Id;
        }

        public void DemandRecordAccess(IBusinessOwnedEntity entity, string resourceKey, string action)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (UsesPostgreSql && !CanAccessOwnedBusinessRecord(
                entity.OwnerUserId, entity.DepartmentId, entity.CompanyScope, resourceKey, action))
            {
                throw new PermissionDeniedException("当前账号没有权限对这条记录执行该操作，请联系管理员调整权限方案。");
            }
        }

        public bool CanAccessOwnedBusinessRecord(
            int? ownerUserId,
            string? departmentId,
            string? companyScope,
            string resourceKey,
            string action,
            User? user = null)
        {
            user ??= _currentUserContext?.CurrentUser;
            string dataScope = ResolveDataScope(user, resourceKey, action);
            if (!PermissionDataScope.IsKnown(dataScope))
            {
                return false;
            }

            if (!DatabaseModeHelper.UsesPostgreSql(_settings))
            {
                return true;
            }

            return dataScope switch
            {
                PermissionDataScope.All => true,
                PermissionDataScope.Company =>
                    !string.IsNullOrWhiteSpace(user?.CompanyScope) &&
                    string.Equals(companyScope?.Trim(), user.CompanyScope.Trim(), StringComparison.Ordinal),
                PermissionDataScope.Department =>
                    !string.IsNullOrWhiteSpace(user?.CompanyScope) &&
                    !string.IsNullOrWhiteSpace(user.DepartmentId) &&
                    string.Equals(companyScope?.Trim(), user.CompanyScope.Trim(), StringComparison.Ordinal) &&
                    string.Equals(departmentId?.Trim(), user.DepartmentId.Trim(), StringComparison.Ordinal),
                PermissionDataScope.Own => user?.Id > 0 && ownerUserId == user.Id,
                _ => false
            };
        }

        public IQueryable<Invoice> ApplyInvoiceScope(IQueryable<Invoice> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View, user);

        public IQueryable<Invoice> ApplyInvoiceScopeForPermission(
            IQueryable<Invoice> query, string resourceKey, string action, User? user = null)
            => ApplyBusinessScope(query, resourceKey, action, user);

        public IQueryable<Payment> ApplyPaymentScope(IQueryable<Payment> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentPayments, PermissionAction.View, user);

        public IQueryable<Customer> ApplyCustomerScope(IQueryable<Customer> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View, user);

        public IQueryable<Exporter> ApplyExporterScope(IQueryable<Exporter> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View, user);

        public IQueryable<Payee> ApplyPayeeScope(IQueryable<Payee> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentPayments, PermissionAction.View, user);

        public IQueryable<CrmCustomer> ApplyCrmCustomerScope(
            IQueryable<CrmCustomer> query, User? user = null, string action = PermissionAction.View)
            => ApplyCrmCustomerScopeForPermission(query, PermissionResourceCatalog.CrmCustomers, action, user);

        public IQueryable<CrmCustomer> ApplyCrmCustomerScopeForPermission(
            IQueryable<CrmCustomer> query, string resourceKey, string action, User? user = null)
            => ApplyBusinessScope(query, resourceKey, action, user);

        public IQueryable<CrmFollowUp> ApplyCrmFollowUpScope(
            IQueryable<CrmFollowUp> query, User? user = null, string action = PermissionAction.View)
            => ApplyBusinessScope(query, PermissionResourceCatalog.CrmFollowUps, action, user);

        public IQueryable<SupplierCompany> ApplySupplierScope(
            IQueryable<SupplierCompany> query, User? user = null, string action = PermissionAction.View)
            => ApplySupplierScopeForPermission(query, PermissionResourceCatalog.Suppliers, action, user);

        public IQueryable<SupplierCompany> ApplySupplierScopeForPermission(
            IQueryable<SupplierCompany> query, string resourceKey, string action, User? user = null)
            => ApplyBusinessScope(query, resourceKey, action, user);

        public IQueryable<EmailTemplate> ApplyEmailTemplateScope(
            IQueryable<EmailTemplate> query, User? user = null, string action = PermissionAction.View)
            => ApplySharedTemplateScope(query, PermissionResourceCatalog.EmailTemplates, action, user);

        public IQueryable<EmailTemplate> ApplyOwnedEmailTemplateScope(IQueryable<EmailTemplate> query, User? user = null)
            => ApplyOwnedScope(query, user);

        public IQueryable<EmailDeliveryRecord> ApplyEmailDeliveryScope(
            IQueryable<EmailDeliveryRecord> query,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings) || CanViewAllBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(
                user,
                PermissionResourceCatalog.EmailDelivery,
                PermissionAction.ViewDelivery) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 =>
                    query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<UserReportTemplate> ApplyUserReportTemplateScope(
            IQueryable<UserReportTemplate> query, User? user = null, string action = PermissionAction.View)
            => ApplySharedTemplateScope(query, PermissionResourceCatalog.ReportTemplates, action, user);

        public IQueryable<UserReportTemplate> ApplyOwnedUserReportTemplateScope(
            IQueryable<UserReportTemplate> query, User? user = null)
            => ApplyOwnedScope(query, user);

        public IQueryable<SalesOpportunity> ApplySalesOpportunityScope(
            IQueryable<SalesOpportunity> query,
            User? user = null,
            bool includeDeleted = false,
            string action = PermissionAction.View)
            => ApplySalesOpportunityScopeForPermission(
                query, PermissionResourceCatalog.SalesOpportunities, action, user, includeDeleted);

        public IQueryable<SalesOpportunity> ApplySalesOpportunityScopeForPermission(
            IQueryable<SalesOpportunity> query,
            string resourceKey,
            string action,
            User? user = null,
            bool includeDeleted = false)
        {
            ArgumentNullException.ThrowIfNull(query);
            // Only explicit history queries may include archived opportunities.
            if (!includeDeleted)
            {
                query = query.Where(item => !item.IsDeleted);
            }
            return ApplyBusinessScope(query, resourceKey, action, user);
        }

        public IQueryable<ContainerProject> ApplyContainerProjectScope(
            IQueryable<ContainerProject> query, User? user = null)
            => ApplyBusinessScope(query, PermissionModuleCatalog.DocumentContainerPacking, PermissionAction.View, user);

        private IQueryable<TEntity> ApplyBusinessScope<TEntity>(
            IQueryable<TEntity> query, string resourceKey, string action, User? user)
            where TEntity : class, IBusinessOwnedEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!UsesPostgreSql) return query;
            user ??= CurrentUser;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, resourceKey, action) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 =>
                    query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        private IQueryable<TEntity> ApplySharedTemplateScope<TEntity>(
            IQueryable<TEntity> query, string resourceKey, string action, User? user)
            where TEntity : class, ISharedBusinessTemplate
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= CurrentUser;
            if (!UsesPostgreSql || CanViewAllBusinessData(user)) return query;
            string dataScope = ResolveDataScope(user, resourceKey, action);
            int userId = user?.Id ?? 0;
            if (userId <= 0 || !PermissionDataScope.IsKnown(dataScope)) return query.Where(_ => false);
            if (dataScope == PermissionDataScope.Own) return query.Where(item => item.OwnerUserId == userId);

            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            // A broader capability never widens a template author's sharing audience.
            // Private/draft templates remain owner-only; shared reads require publication.
            return query.Where(item =>
                item.OwnerUserId == userId ||
                item.Status == TemplateLifecycleStatusCatalog.Published &&
                (item.ShareScope == TemplateShareScopeCatalog.All ||
                 companyScope != string.Empty && item.CompanyScope == companyScope &&
                 (item.ShareScope == TemplateShareScopeCatalog.Company ||
                  departmentId != string.Empty && item.DepartmentId == departmentId &&
                  item.ShareScope == TemplateShareScopeCatalog.Department)));
        }

        private IQueryable<TEntity> ApplyOwnedScope<TEntity>(IQueryable<TEntity> query, User? user)
            where TEntity : class, IBusinessOwnedEntity
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= CurrentUser;
            if (!UsesPostgreSql || CanViewAllBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<SwSubmissionBatch> ApplySubmissionBatchScope(
            IQueryable<SwSubmissionBatch> query,
            AppDbContext context,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(context);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            var accessibleInvoices = ApplyInvoiceScope(context.Invoices.AsNoTracking(), user);
            return query.Where(batch => accessibleInvoices.Any(invoice =>
                invoice.Id == batch.SourceInvoiceId));
        }

        public async Task<bool> CanAccessPaymentAsync(
            AppDbContext context,
            int paymentId,
            CancellationToken cancellationToken = default,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (paymentId <= 0)
            {
                return false;
            }

            return await ApplyPaymentScope(context.Payments.AsNoTracking(), user)
                .AnyAsync(payment => payment.Id == paymentId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> CanAccessInvoiceAsync(
            AppDbContext context,
            int invoiceId,
            CancellationToken cancellationToken = default,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (invoiceId <= 0)
            {
                return false;
            }

            return await ApplyInvoiceScope(context.Invoices.AsNoTracking(), user)
                .AnyAsync(invoice => invoice.Id == invoiceId, cancellationToken)
                .ConfigureAwait(false);
        }

        public void ApplyOwner<TEntity>(TEntity entity, User? user = null)
            where TEntity : class, IBusinessOwnedEntity
        {
            ArgumentNullException.ThrowIfNull(entity);
            user ??= CurrentUser;
            if (user == null || user.Id <= 0 || entity.OwnerUserId.HasValue) return;
            entity.OwnerUserId = user.Id;
            entity.DepartmentId = NormalizeScope(user.DepartmentId);
            entity.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        private static string NormalizeScope(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        public static string ResolveDataScope(User? user, string resourceKey, string action)
        {
            if (!PermissionResourceCatalog.IsKnownAction(resourceKey, action)) return string.Empty;
            if (CanViewAllBusinessData(user)) return PermissionDataScope.All;

            string key = PermissionResourceCatalog.CreateGrantKey(resourceKey, action);
            var grants = UserPermissionAccessResolver.ResolveEffectiveGrants(user);
            return grants.TryGetValue(key, out string? scope) && PermissionDataScope.IsKnown(scope)
                ? PermissionDataScope.Normalize(scope)
                : string.Empty;
        }
    }
}
