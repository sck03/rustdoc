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
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!DatabaseModeHelper.UsesPostgreSql(_settings))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View) switch
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

        public IQueryable<Payment> ApplyPaymentScope(IQueryable<Payment> query, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!DatabaseModeHelper.UsesPostgreSql(_settings))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentPayments, PermissionAction.View) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<CrmCustomer> ApplyCrmCustomerScope(
            IQueryable<CrmCustomer> query,
            User? user = null,
            string action = PermissionAction.View)
            => ApplyCrmCustomerScopeForPermission(
                query,
                PermissionResourceCatalog.CrmCustomers,
                action,
                user);

        public IQueryable<CrmCustomer> ApplyCrmCustomerScopeForPermission(
            IQueryable<CrmCustomer> query,
            string resourceKey,
            string action,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!DatabaseModeHelper.UsesPostgreSql(_settings))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, resourceKey, action) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<Customer> ApplyCustomerScope(IQueryable<Customer> query, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<Exporter> ApplyExporterScope(IQueryable<Exporter> query, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<Payee> ApplyPayeeScope(IQueryable<Payee> query, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentPayments, PermissionAction.View) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<CrmFollowUp> ApplyCrmFollowUpScope(
            IQueryable<CrmFollowUp> query,
            User? user = null,
            string action = PermissionAction.View)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionResourceCatalog.CrmFollowUps, action) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<SupplierCompany> ApplySupplierScope(
            IQueryable<SupplierCompany> query,
            User? user = null,
            string action = PermissionAction.View)
            => ApplySupplierScopeForPermission(
                query,
                PermissionResourceCatalog.Suppliers,
                action,
                user);

        public IQueryable<SupplierCompany> ApplySupplierScopeForPermission(
            IQueryable<SupplierCompany> query,
            string resourceKey,
            string action,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, resourceKey, action) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<EmailTemplate> ApplyEmailTemplateScope(
            IQueryable<EmailTemplate> query,
            User? user = null,
            string action = PermissionAction.View)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            if (CanViewAllBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionResourceCatalog.EmailTemplates, action) switch
            {
                PermissionDataScope.All when userId > 0 => query.Where(item =>
                    item.OwnerUserId == userId ||
                    item.Status == TemplateLifecycleStatusCatalog.Published &&
                    item.ShareScope != TemplateShareScopeCatalog.Private),
                PermissionDataScope.Company when userId > 0 => query.Where(item =>
                    item.OwnerUserId == userId ||
                    item.Status == TemplateLifecycleStatusCatalog.Published &&
                    (item.ShareScope == TemplateShareScopeCatalog.All ||
                     companyScope != string.Empty &&
                     item.ShareScope == TemplateShareScopeCatalog.Company &&
                     item.CompanyScope == companyScope)),
                PermissionDataScope.Department when userId > 0 => query.Where(item =>
                    item.OwnerUserId == userId ||
                    item.Status == TemplateLifecycleStatusCatalog.Published &&
                    (item.ShareScope == TemplateShareScopeCatalog.All ||
                     companyScope != string.Empty &&
                     item.ShareScope == TemplateShareScopeCatalog.Company &&
                     item.CompanyScope == companyScope ||
                     companyScope != string.Empty && departmentId != string.Empty &&
                     item.ShareScope == TemplateShareScopeCatalog.Department &&
                     item.DepartmentId == departmentId && item.CompanyScope == companyScope)),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<EmailTemplate> ApplyOwnedEmailTemplateScope(IQueryable<EmailTemplate> query, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings) || CanViewAllBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

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
            IQueryable<UserReportTemplate> query,
            User? user = null,
            string action = PermissionAction.View)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings))
            {
                return query;
            }

            if (CanViewAllBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionResourceCatalog.ReportTemplates, action) switch
            {
                PermissionDataScope.All when userId > 0 => query.Where(item =>
                    item.OwnerUserId == userId ||
                    item.Status == TemplateLifecycleStatusCatalog.Published &&
                    item.ShareScope != TemplateShareScopeCatalog.Private),
                PermissionDataScope.Company when userId > 0 => query.Where(item =>
                    item.OwnerUserId == userId ||
                    item.Status == TemplateLifecycleStatusCatalog.Published &&
                    (item.ShareScope == TemplateShareScopeCatalog.All ||
                     companyScope != string.Empty &&
                     item.ShareScope == TemplateShareScopeCatalog.Company && item.CompanyScope == companyScope)),
                PermissionDataScope.Department when userId > 0 =>
                    query.Where(item => item.OwnerUserId == userId ||
                        item.Status == TemplateLifecycleStatusCatalog.Published &&
                        (item.ShareScope == TemplateShareScopeCatalog.All ||
                         companyScope != string.Empty &&
                         item.ShareScope == TemplateShareScopeCatalog.Company && item.CompanyScope == companyScope ||
                         companyScope != string.Empty && departmentId != string.Empty &&
                         item.ShareScope == TemplateShareScopeCatalog.Department && item.DepartmentId == departmentId &&
                         item.CompanyScope == companyScope)),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<UserReportTemplate> ApplyOwnedUserReportTemplateScope(
            IQueryable<UserReportTemplate> query,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings) || CanViewAllBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(item => item.OwnerUserId == userId)
                : query.Where(_ => false);
        }

        public IQueryable<SalesOpportunity> ApplySalesOpportunityScope(
            IQueryable<SalesOpportunity> query,
            User? user = null,
            bool includeDeleted = false,
            string action = PermissionAction.View)
            => ApplySalesOpportunityScopeForPermission(
                query,
                PermissionResourceCatalog.SalesOpportunities,
                action,
                user,
                includeDeleted);

        public IQueryable<SalesOpportunity> ApplySalesOpportunityScopeForPermission(
            IQueryable<SalesOpportunity> query,
            string resourceKey,
            string action,
            User? user = null,
            bool includeDeleted = false)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            // Archived opportunities remain available to their audit/history
            // endpoints, but ordinary lists and aggregates must never mix them
            // into active pipeline figures. Callers that explicitly render
            // history can opt in with includeDeleted=true.
            if (!includeDeleted)
            {
                query = query.Where(item => !item.IsDeleted);
            }
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, resourceKey, action) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
        }

        public IQueryable<ContainerProject> ApplyContainerProjectScope(
            IQueryable<ContainerProject> query,
            User? user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!DatabaseModeHelper.UsesPostgreSql(_settings)) return query;
            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return ResolveDataScope(user, PermissionModuleCatalog.DocumentContainerPacking, PermissionAction.View) switch
            {
                PermissionDataScope.All => query,
                PermissionDataScope.Company when companyScope.Length > 0 => query.Where(item => item.CompanyScope == companyScope),
                PermissionDataScope.Department when departmentId.Length > 0 && companyScope.Length > 0 =>
                    query.Where(item => item.DepartmentId == departmentId && item.CompanyScope == companyScope),
                PermissionDataScope.Own when userId > 0 => query.Where(item => item.OwnerUserId == userId),
                _ => query.Where(_ => false)
            };
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

        public void ApplyOwner(Invoice invoice, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || invoice.OwnerUserId.HasValue)
            {
                return;
            }

            invoice.OwnerUserId = user.Id;
            invoice.DepartmentId = NormalizeScope(user.DepartmentId);
            invoice.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(Payment payment, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(payment);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || payment.OwnerUserId.HasValue)
            {
                return;
            }

            payment.OwnerUserId = user.Id;
            payment.DepartmentId = NormalizeScope(user.DepartmentId);
            payment.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(CrmCustomer customer, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(customer);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || customer.OwnerUserId.HasValue) return;
            customer.OwnerUserId = user.Id;
            customer.DepartmentId = NormalizeScope(user.DepartmentId);
            customer.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(Customer customer, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(customer);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || customer.OwnerUserId.HasValue) return;
            customer.OwnerUserId = user.Id;
            customer.DepartmentId = NormalizeScope(user.DepartmentId);
            customer.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(Exporter exporter, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(exporter);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || exporter.OwnerUserId.HasValue) return;
            exporter.OwnerUserId = user.Id;
            exporter.DepartmentId = NormalizeScope(user.DepartmentId);
            exporter.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(Payee payee, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(payee);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || payee.OwnerUserId.HasValue) return;
            payee.OwnerUserId = user.Id;
            payee.DepartmentId = NormalizeScope(user.DepartmentId);
            payee.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(CrmFollowUp followUp, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(followUp);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || followUp.OwnerUserId.HasValue) return;
            followUp.OwnerUserId = user.Id;
            followUp.DepartmentId = NormalizeScope(user.DepartmentId);
            followUp.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(SupplierCompany supplier, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(supplier);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || supplier.OwnerUserId.HasValue) return;
            supplier.OwnerUserId = user.Id;
            supplier.DepartmentId = NormalizeScope(user.DepartmentId);
            supplier.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(EmailTemplate template, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(template);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || template.OwnerUserId.HasValue) return;
            template.OwnerUserId = user.Id;
            template.DepartmentId = NormalizeScope(user.DepartmentId);
            template.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(UserReportTemplate template, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(template);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || template.OwnerUserId.HasValue)
            {
                return;
            }

            template.OwnerUserId = user.Id;
            template.DepartmentId = NormalizeScope(user.DepartmentId);
            template.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(SalesOpportunity opportunity, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(opportunity);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || opportunity.OwnerUserId.HasValue) return;
            opportunity.OwnerUserId = user.Id;
            opportunity.DepartmentId = NormalizeScope(user.DepartmentId);
            opportunity.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(ContainerProject project, User? user = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || project.OwnerUserId.HasValue) return;
            project.OwnerUserId = user.Id;
            project.DepartmentId = NormalizeScope(user.DepartmentId);
            project.CompanyScope = NormalizeScope(user.CompanyScope);
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
