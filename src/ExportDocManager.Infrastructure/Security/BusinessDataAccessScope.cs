using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security
{
    public sealed class BusinessDataAccessScope
    {
        private readonly DatabaseConnectionSettings _settings;
        private readonly ICurrentUserContext _currentUserContext;

        public BusinessDataAccessScope(DatabaseConnectionSettings settings)
            : this(settings, null)
        {
        }

        public BusinessDataAccessScope(
            DatabaseConnectionSettings settings,
            ICurrentUserContext currentUserContext)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _currentUserContext = currentUserContext;
        }

        public User CurrentUser => _currentUserContext?.CurrentUser;

        public bool ShouldFilterBusinessData(User user = null)
        {
            user ??= _currentUserContext?.CurrentUser;
            return DatabaseModeHelper.UsesPostgreSql(_settings) &&
                   !CanViewAllBusinessData(user);
        }

        public static bool CanViewAllBusinessData(User user)
        {
            return string.Equals(user?.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);
        }

        public IQueryable<Invoice> ApplyInvoiceScope(IQueryable<Invoice> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(invoice => invoice.OwnerUserId == userId)
                : query.Where(_ => false);
        }

        public IQueryable<Payment> ApplyPaymentScope(IQueryable<Payment> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(payment => payment.OwnerUserId == userId)
                : query.Where(_ => false);
        }

        public IQueryable<CrmCustomer> ApplyCrmCustomerScope(
            IQueryable<CrmCustomer> query,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(item => item.OwnerUserId == userId)
                : query.Where(_ => false);
        }

        public IQueryable<CrmFollowUp> ApplyCrmFollowUpScope(IQueryable<CrmFollowUp> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<SupplierCompany> ApplySupplierScope(IQueryable<SupplierCompany> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<EmailTemplate> ApplyEmailTemplateScope(IQueryable<EmailTemplate> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId || item.IsShared) : query.Where(_ => false);
        }

        public IQueryable<EmailTemplate> ApplyOwnedEmailTemplateScope(IQueryable<EmailTemplate> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<UserReportTemplate> ApplyUserReportTemplateScope(
            IQueryable<UserReportTemplate> query,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (CanViewAllBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            string departmentId = NormalizeScope(user?.DepartmentId);
            string companyScope = NormalizeScope(user?.CompanyScope);
            return userId > 0
                ? query.Where(item => item.OwnerUserId == userId ||
                    (item.IsActive && item.IsShared &&
                     (item.ShareScope == "All" ||
                      item.ShareScope == "Company" && companyScope != "" && item.CompanyScope == companyScope ||
                      item.ShareScope == "Department" && departmentId != "" && item.DepartmentId == departmentId &&
                        (item.CompanyScope == "" || companyScope == "" || item.CompanyScope == companyScope))))
                : query.Where(_ => false);
        }

        public IQueryable<UserReportTemplate> ApplyOwnedUserReportTemplateScope(
            IQueryable<UserReportTemplate> query,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (CanViewAllBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(item => item.OwnerUserId == userId)
                : query.Where(_ => false);
        }

        public IQueryable<SalesOpportunity> ApplySalesOpportunityScope(IQueryable<SalesOpportunity> query, User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<ContainerProject> ApplyContainerProjectScope(
            IQueryable<ContainerProject> query,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            user ??= _currentUserContext?.CurrentUser;
            if (!ShouldFilterBusinessData(user)) return query;
            int userId = user?.Id ?? 0;
            return userId > 0 ? query.Where(item => item.OwnerUserId == userId) : query.Where(_ => false);
        }

        public IQueryable<SwSubmissionBatch> ApplySubmissionBatchScope(
            IQueryable<SwSubmissionBatch> query,
            AppDbContext context,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(context);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(batch => context.Invoices.Any(invoice =>
                    invoice.Id == batch.SourceInvoiceId &&
                    invoice.OwnerUserId == userId))
                : query.Where(_ => false);
        }

        public IQueryable<SwOperationTicket> ApplyOperationTicketScope(
            IQueryable<SwOperationTicket> query,
            AppDbContext context,
            User user = null)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(context);
            user ??= _currentUserContext?.CurrentUser;

            if (!ShouldFilterBusinessData(user))
            {
                return query;
            }

            int userId = user?.Id ?? 0;
            return userId > 0
                ? query.Where(ticket => context.Invoices.Any(invoice =>
                    invoice.Id == ticket.SourceInvoiceId &&
                    invoice.OwnerUserId == userId))
                : query.Where(_ => false);
        }

        public async Task<bool> CanAccessPaymentAsync(
            AppDbContext context,
            int paymentId,
            CancellationToken cancellationToken = default,
            User user = null)
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
            User user = null)
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

        public void ApplyOwner(Invoice invoice, User user = null)
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

        public void ApplyOwner(Payment payment, User user = null)
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

        public void ApplyOwner(CrmCustomer customer, User user = null)
        {
            ArgumentNullException.ThrowIfNull(customer);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || customer.OwnerUserId.HasValue) return;
            customer.OwnerUserId = user.Id;
            customer.DepartmentId = NormalizeScope(user.DepartmentId);
            customer.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(CrmFollowUp followUp, User user = null)
        {
            ArgumentNullException.ThrowIfNull(followUp);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || followUp.OwnerUserId.HasValue) return;
            followUp.OwnerUserId = user.Id;
            followUp.DepartmentId = NormalizeScope(user.DepartmentId);
            followUp.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(SupplierCompany supplier, User user = null)
        {
            ArgumentNullException.ThrowIfNull(supplier);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || supplier.OwnerUserId.HasValue) return;
            supplier.OwnerUserId = user.Id;
            supplier.DepartmentId = NormalizeScope(user.DepartmentId);
            supplier.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(EmailTemplate template, User user = null)
        {
            ArgumentNullException.ThrowIfNull(template);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || template.OwnerUserId.HasValue) return;
            template.OwnerUserId = user.Id;
            template.DepartmentId = NormalizeScope(user.DepartmentId);
            template.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(UserReportTemplate template, User user = null)
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

        public void ApplyOwner(SalesOpportunity opportunity, User user = null)
        {
            ArgumentNullException.ThrowIfNull(opportunity);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || opportunity.OwnerUserId.HasValue) return;
            opportunity.OwnerUserId = user.Id;
            opportunity.DepartmentId = NormalizeScope(user.DepartmentId);
            opportunity.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        public void ApplyOwner(ContainerProject project, User user = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            user ??= _currentUserContext?.CurrentUser;
            if (user == null || user.Id <= 0 || project.OwnerUserId.HasValue) return;
            project.OwnerUserId = user.Id;
            project.DepartmentId = NormalizeScope(user.DepartmentId);
            project.CompanyScope = NormalizeScope(user.CompanyScope);
        }

        private static string NormalizeScope(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
