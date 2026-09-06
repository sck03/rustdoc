using System.Threading.Tasks;
using System.Data;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Security
{
    public class UserService : IUserService
    {
        private static readonly string DummyPasswordHash = PasswordHasher.HashPassword(
            "ExportDocManager-Dummy-Password-Verification-Only");
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly ICurrentUserContext? _currentUserContext;

        public UserService(IDbContextFactory<AppDbContext> contextFactory)
            : this(contextFactory, new DatabaseConnectionSettings())
        {
        }

        public UserService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings)
            : this(contextFactory, databaseSettings, null)
        {
        }

        public UserService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            ICurrentUserContext? currentUserContext)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _currentUserContext = currentUserContext;
        }

        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            if (DatabaseModeHelper.UsesPostgreSql(_databaseSettings) &&
                string.IsNullOrEmpty(password))
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var normalizedUsername = CanonicalKey(username);
            if (normalizedUsername.Length == 0)
            {
                _ = PasswordHasher.VerifyPassword(DummyPasswordHash, password);
                return null;
            }
            var user = await context.Users
                    .Include(item => item.PermissionTemplate!.Grants)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u =>
                        u.IsActive && u.UsernameNormalized == normalizedUsername);

            if (user == null)
            {
                _ = PasswordHasher.VerifyPassword(DummyPasswordHash, password);
                return null;
            }

            if (PasswordHasher.VerifyPassword(user.PasswordHash, password))
            {
                UserPermissionAccessResolver.PopulateEffectivePermissions(user);
                return user;
            }

            return null;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var normalizedUsername = CanonicalKey(username);
            if (normalizedUsername.Length == 0)
            {
                return null;
            }
            var user = await context.Users
                    .Include(item => item.PermissionTemplate!.Grants)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.IsActive && item.UsernameNormalized == normalizedUsername);
            if (user != null)
            {
                UserPermissionAccessResolver.PopulateEffectivePermissions(user);
            }

            return user;
        }

        public async Task<User?> GetActiveUserByIdAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (userId <= 0)
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users
                .Include(item => item.PermissionTemplate!.Grants)
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
            if (user != null)
            {
                UserPermissionAccessResolver.PopulateEffectivePermissions(user);
            }

            return user;
        }

        public async Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            EnsureCurrentUserCanManageUsers();

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Users
                .Include(user => user.PermissionTemplate)
                .AsNoTracking()
                .OrderByDescending(user => user.IsActive)
                .ThenBy(user => user.Username)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SaveUserAsync(
            User user,
            string resetPassword = "",
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentUserCanManageUsers();
            ArgumentNullException.ThrowIfNull(user);

            var normalized = NormalizeUserForSave(user);
            var normalizedPassword = resetPassword ?? string.Empty;
            bool shouldSetPassword = normalized.Id == 0 || normalizedPassword.Length > 0;
            if (shouldSetPassword)
            {
                UserPasswordPolicy.EnsureValid(
                    normalizedPassword,
                    normalized.Id == 0 ? "初始密码" : "重置密码");
            }

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    await EnsureUsernameUniqueAsync(context, normalized, token);
                    await ValidateOrganizationAssignmentAsync(context, normalized, token);
                    int? currentTemplateId = normalized.Id > 0
                        ? await context.Users.Where(item => item.Id == normalized.Id)
                            .Select(item => item.PermissionTemplateId)
                            .SingleOrDefaultAsync(token)
                        : null;
                    normalized.PermissionTemplateId = await ResolvePermissionTemplateIdAsync(
                        context,
                        normalized.Role,
                        normalized.PermissionTemplateId,
                        currentTemplateId,
                        token);

                    User savedUser;
                    if (normalized.Id == 0)
                    {
                        normalized.PasswordHash = PasswordHasher.HashPassword(normalizedPassword);
                        normalized.VersionNumber = 1;
                        await context.Users.AddAsync(normalized, token);
                        savedUser = normalized;
                    }
                    else
                    {
                        if (normalized.VersionNumber <= 0)
                        {
                            throw new BusinessConcurrencyException("保存现有用户时必须提供版本号，请刷新后重试。");
                        }

                        var existing = await context.Users
                            .FirstOrDefaultAsync(item => item.Id == normalized.Id, token)
                            ?? throw new ResourceNotFoundException("未找到要保存的用户。");
                        if (existing.VersionNumber != normalized.VersionNumber)
                        {
                            throw new BusinessConcurrencyException("该用户已被其他管理员修改，请刷新后重试。");
                        }

                        PreventSelfLockout(normalized);
                        if (IsActiveAdmin(existing) &&
                            (!normalized.IsActive || !IsAdminRole(normalized.Role)))
                        {
                            await EnsureAnotherActiveAdminAsync(context, existing.Id, token);
                        }

                        context.Entry(existing).Property(item => item.VersionNumber).OriginalValue = normalized.VersionNumber;
                        existing.VersionNumber++;
                        existing.Username = normalized.Username;
                        existing.FullName = normalized.FullName;
                        existing.Role = normalized.Role;
                        existing.PermissionTemplateId = normalized.PermissionTemplateId;
                        existing.DepartmentId = normalized.DepartmentId;
                        existing.CompanyScope = normalized.CompanyScope;
                        existing.IsActive = normalized.IsActive;

                        if (shouldSetPassword)
                        {
                            existing.PasswordHash = PasswordHasher.HashPassword(normalizedPassword);
                        }

                        savedUser = existing;
                    }

                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new BusinessConcurrencyException("该用户已被其他管理员修改，请刷新后重试。", exception);
                    }
                    catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
                    {
                        throw new ResourceConflictException("用户名已存在。", exception);
                    }

                    return savedUser.Id;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public async Task<bool> DeleteUserAsync(
            int userId,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0)
        {
            EnsureCurrentUserCanManageUsers();
            if (userId <= 0)
            {
                throw new ServiceValidationException("请选择要删除的用户。");
            }

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var user = await context.Users
                        .FirstOrDefaultAsync(item => item.Id == userId, token);
                    if (user == null)
                    {
                        return false;
                    }

                    if (expectedVersion <= 0 || user.VersionNumber != expectedVersion)
                    {
                        throw new BusinessConcurrencyException("该用户已被其他管理员修改，请刷新后重试。");
                    }

                    await EnsureUserCanBeDeletedAsync(context, user, token);
                    context.Entry(user).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.Users.Remove(user);
                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new BusinessConcurrencyException("该用户已被其他管理员修改，请刷新后重试。", exception);
                    }
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private static User NormalizeUserForSave(User user)
        {
            var username = (user.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ServiceValidationException("用户名不能为空。");
            }

            return new User
            {
                Id = user.Id,
                Username = username,
                PasswordHash = user.PasswordHash ?? string.Empty,
                FullName = (user.FullName ?? string.Empty).Trim(),
                Role = UserRoleCatalog.Normalize(user.Role),
                PermissionTemplateId = user.PermissionTemplateId,
                DepartmentId = NormalizeOrganizationCode(user.DepartmentId),
                CompanyScope = NormalizeOrganizationCode(user.CompanyScope),
                IsActive = user.IsActive,
                VersionNumber = user.Id > 0 ? user.VersionNumber : 1
            };
        }

        private static async Task EnsureUsernameUniqueAsync(
            AppDbContext context,
            User user,
            CancellationToken cancellationToken)
        {
            string normalizedUsername = CanonicalKey(user.Username);
            bool duplicate = await context.Users
                .AsNoTracking()
                .AnyAsync(
                    item => item.Id != user.Id && item.UsernameNormalized == normalizedUsername,
                    cancellationToken);
            if (duplicate)
            {
                throw new ResourceConflictException("用户名已存在。");
            }
        }

        private static async Task ValidateOrganizationAssignmentAsync(
            AppDbContext context,
            User user,
            CancellationToken cancellationToken)
        {
            string companyCode = NormalizeOrganizationCode(user.CompanyScope);
            string departmentCode = NormalizeOrganizationCode(user.DepartmentId);
            if (companyCode.Length == 0 && departmentCode.Length == 0)
            {
                user.CompanyScope = null;
                user.DepartmentId = null;
                return;
            }
            if (companyCode.Length == 0)
            {
                throw new ServiceValidationException("选择部门前必须先选择所属公司。");
            }

            bool companyAvailable = await context.OrganizationCompanies.AsNoTracking()
                .AnyAsync(item => item.Code == companyCode && item.IsActive, cancellationToken);
            if (!companyAvailable)
            {
                throw new ServiceValidationException("选择的公司不存在或已停用。");
            }

            if (departmentCode.Length > 0)
            {
                bool departmentAvailable = await context.OrganizationDepartments.AsNoTracking()
                    .AnyAsync(item => item.Code == departmentCode &&
                        item.CompanyCode == companyCode && item.IsActive,
                        cancellationToken);
                if (!departmentAvailable)
                {
                    throw new ServiceValidationException("选择的部门不存在、已停用或不属于所选公司。");
                }
            }

            user.CompanyScope = companyCode;
            user.DepartmentId = departmentCode.Length == 0 ? null : departmentCode;
        }

        private void PreventSelfLockout(User normalized)
        {
            var currentUser = _currentUserContext?.CurrentUser;
            if (currentUser == null || currentUser.Id != normalized.Id)
            {
                return;
            }

            if (!normalized.IsActive || !CanManageUsers(normalized))
            {
                throw new ResourceConflictException("不能停用当前管理员账号或取消自己的管理员角色。");
            }
        }

        private async Task EnsureUserCanBeDeletedAsync(
            AppDbContext context,
            User user,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserContext?.CurrentUser;
            if (currentUser != null && currentUser.Id == user.Id)
            {
                throw new ResourceConflictException("不能删除当前登录账号。");
            }

            if (IsActiveAdmin(user))
            {
                await EnsureAnotherActiveAdminAsync(context, user.Id, cancellationToken);
            }

            bool hasBusinessData = await context.Invoices
                .AsNoTracking()
                .AnyAsync(invoice => invoice.OwnerUserId == user.Id, cancellationToken);
            if (!hasBusinessData)
            {
                hasBusinessData = await context.Payments
                    .AsNoTracking()
                    .AnyAsync(payment => payment.OwnerUserId == user.Id, cancellationToken);
            }

            if (!hasBusinessData)
            {
                hasBusinessData = await context.CrmCustomers
                    .AsNoTracking()
                    .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.CrmFollowUps
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.SupplierCompanies
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.SalesOpportunities
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.EmailTemplates
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.UserReportTemplates
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken) ||
                    await context.ContainerProjects
                        .AsNoTracking()
                        .AnyAsync(item => item.OwnerUserId == user.Id, cancellationToken);
            }

            if (hasBusinessData)
            {
                throw new ResourceConflictException("该用户已有业务数据归属，请停用账号而不是删除。");
            }
        }

        private static bool IsActiveAdmin(User user)
        {
            return user.IsActive &&
                   IsAdminRole(user.Role);
        }

        private static bool IsAdminRole(string? role) =>
            string.Equals(role, UserRoleCatalog.Admin, StringComparison.OrdinalIgnoreCase);

        private static async Task EnsureAnotherActiveAdminAsync(
            AppDbContext context,
            int excludedUserId,
            CancellationToken cancellationToken)
        {
            bool hasAnother = await context.Users
                .AsNoTracking()
                .AnyAsync(item => item.Id != excludedUserId && item.IsActive &&
                    item.Role == UserRoleCatalog.Admin, cancellationToken);
            if (!hasAnother)
            {
                throw new ResourceConflictException("不能停用或删除最后一个启用的管理员账号。");
            }
        }

        private static string CanonicalKey(string? value) =>
            (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC).ToUpperInvariant();

        private static string NormalizeOrganizationCode(string? value) => CanonicalKey(value);

        private void EnsureCurrentUserCanManageUsers()
        {
            if (!CanManageUsers(_currentUserContext?.CurrentUser))
            {
                throw new PermissionDeniedException("只有管理员可以管理用户账号。");
            }
        }

        private static bool CanManageUsers(User? user)
        {
            return BusinessDataAccessScope.CanViewAllBusinessData(user);
        }

        private static async Task<int> ResolvePermissionTemplateIdAsync(
            AppDbContext context,
            string role,
            int? requestedTemplateId,
            int? currentTemplateId,
            CancellationToken cancellationToken)
        {
            string? requiredCode = string.Equals(role, UserRoleCatalog.Admin, StringComparison.OrdinalIgnoreCase)
                ? BuiltInPermissionTemplateCatalog.Admin
                : null;
            if (requiredCode != null)
            {
                return await context.PermissionTemplates
                    .Where(template => template.Code == requiredCode)
                    .Select(template => template.Id)
                    .SingleAsync(cancellationToken);
            }

            if (requestedTemplateId is > 0)
            {
                bool available = await context.PermissionTemplates.AnyAsync(
                    template => template.Id == requestedTemplateId &&
                        (template.IsActive || template.Id == currentTemplateId),
                    cancellationToken);
                if (available) return requestedTemplateId.Value;
                throw new ServiceValidationException("选择的权限模板不存在或已停用。");
            }

            string defaultCode = BuiltInPermissionTemplateCatalog.FindForRole(role).Code;
            return await context.PermissionTemplates
                .Where(template => template.Code == defaultCode)
                .Select(template => template.Id)
                .SingleAsync(cancellationToken);
        }
    }
}
