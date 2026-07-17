using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Security
{
    public class UserService : IUserService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly ICurrentUserContext _currentUserContext;

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
            ICurrentUserContext currentUserContext)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _currentUserContext = currentUserContext;
        }

        public async Task<User> AuthenticateAsync(string username, string password)
        {
            if (DatabaseModeHelper.UsesPostgreSql(_databaseSettings) &&
                string.IsNullOrEmpty(password))
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var normalizedUsername = (username ?? string.Empty).Trim();
            var user = (await context.Users
                    .Include(item => item.PermissionTemplate)
                    .ThenInclude(template => template.Modules)
                    .Where(u => u.IsActive)
                    .ToListAsync())
                .FirstOrDefault(u => string.Equals(u.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (user == null)
            {
                return null;
            }

            if (PasswordHasher.VerifyPassword(user.PasswordHash, password))
            {
                PopulateEffectiveModuleAccess(user);
                return user;
            }

            return null;
        }

        public async Task<User> GetUserByUsernameAsync(string username)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Users.FirstOrDefaultAsync(u => u.Username == username);
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

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await EnsureUsernameUniqueAsync(context, normalized, cancellationToken);
            normalized.PermissionTemplateId = await ResolvePermissionTemplateIdAsync(
                context,
                normalized.Role,
                normalized.PermissionTemplateId,
                cancellationToken);

            User savedUser;
            if (normalized.Id == 0)
            {
                normalized.PasswordHash = PasswordHasher.HashPassword(normalizedPassword);
                await context.Users.AddAsync(normalized, cancellationToken);
                savedUser = normalized;
            }
            else
            {
                var existing = await context.Users
                    .FirstOrDefaultAsync(item => item.Id == normalized.Id, cancellationToken)
                    ?? throw new InvalidOperationException("未找到要保存的用户。");

                PreventSelfLockout(normalized);
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

            await context.SaveChangesAsync(cancellationToken);
            return savedUser.Id;
        }

        public async Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            EnsureCurrentUserCanManageUsers();
            if (userId <= 0)
            {
                throw new InvalidOperationException("请选择要删除的用户。");
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

                    await EnsureUserCanBeDeletedAsync(context, user, token);
                    context.Users.Remove(user);
                    await context.SaveChangesAsync(token);
                    return true;
                },
                cancellationToken);
        }

        private static User NormalizeUserForSave(User user)
        {
            var username = (user.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("用户名不能为空。");
            }

            return new User
            {
                Id = user.Id,
                Username = username,
                PasswordHash = user.PasswordHash ?? string.Empty,
                FullName = (user.FullName ?? string.Empty).Trim(),
                Role = UserRoleCatalog.Normalize(user.Role),
                PermissionTemplateId = user.PermissionTemplateId,
                DepartmentId = (user.DepartmentId ?? string.Empty).Trim(),
                CompanyScope = (user.CompanyScope ?? string.Empty).Trim(),
                IsActive = user.IsActive
            };
        }

        private static async Task EnsureUsernameUniqueAsync(
            AppDbContext context,
            User user,
            CancellationToken cancellationToken)
        {
            var existingUsers = await context.Users
                .AsNoTracking()
                .Where(item => item.Id != user.Id)
                .Select(item => new { item.Username })
                .ToListAsync(cancellationToken);

            bool duplicate = existingUsers.Any(item =>
                string.Equals(item.Username?.Trim(), user.Username, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                throw new InvalidOperationException("用户名已存在。");
            }
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
                throw new InvalidOperationException("不能停用当前管理员账号或取消自己的管理员角色。");
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
                throw new InvalidOperationException("不能删除当前登录账号。");
            }

            if (IsActiveAdmin(user))
            {
                bool hasAnotherActiveAdmin = await context.Users
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.Id != user.Id &&
                        item.IsActive &&
                        item.Role == UserRoleCatalog.Admin,
                        cancellationToken);
                if (!hasAnotherActiveAdmin)
                {
                    throw new InvalidOperationException("不能删除最后一个启用的管理员账号。");
                }
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

            if (hasBusinessData)
            {
                throw new InvalidOperationException("该用户已有业务数据归属，请停用账号而不是删除。");
            }
        }

        private static bool IsActiveAdmin(User user)
        {
            return user.IsActive &&
                   string.Equals(user.Role, UserRoleCatalog.Admin, StringComparison.Ordinal);
        }

        private void EnsureCurrentUserCanManageUsers()
        {
            if (!CanManageUsers(_currentUserContext?.CurrentUser))
            {
                throw new UnauthorizedAccessException("只有管理员可以管理用户账号。");
            }
        }

        private static bool CanManageUsers(User user)
        {
            return BusinessDataAccessScope.CanViewAllBusinessData(user);
        }

        private static void PopulateEffectiveModuleAccess(User user)
        {
            if (user.PermissionTemplate != null)
            {
                user.EffectiveModuleAccess = user.PermissionTemplate.IsActive
                    ? user.PermissionTemplate.Modules
                        .Where(module => PermissionModuleCatalog.IsKnown(module.ModuleKey))
                        .GroupBy(module => module.ModuleKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => PermissionAccessLevel.Normalize(group.Last().AccessLevel),
                            StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            if (user.PermissionTemplateId == null)
            {
                user.EffectiveModuleAccess = BuiltInPermissionTemplateCatalog.FindForRole(user.Role)
                    .GetModuleAccess();
                return;
            }

            user.EffectiveModuleAccess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<int> ResolvePermissionTemplateIdAsync(
            AppDbContext context,
            string role,
            int? requestedTemplateId,
            CancellationToken cancellationToken)
        {
            string requiredCode = string.Equals(role, UserRoleCatalog.Admin, StringComparison.OrdinalIgnoreCase)
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
                    template => template.Id == requestedTemplateId && template.IsActive,
                    cancellationToken);
                if (available) return requestedTemplateId.Value;
                throw new InvalidOperationException("选择的权限模板不存在或已停用。");
            }

            string defaultCode = BuiltInPermissionTemplateCatalog.FindForRole(role).Code;
            return await context.PermissionTemplates
                .Where(template => template.Code == defaultCode)
                .Select(template => template.Id)
                .SingleAsync(cancellationToken);
        }
    }
}
