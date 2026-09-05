using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security
{
    public sealed partial class OrganizationDirectoryService : IOrganizationDirectoryService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ICurrentUserContext _currentUserContext;

        public OrganizationDirectoryService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICurrentUserContext currentUserContext)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        }

        public async Task<OrganizationDirectoryRecord> ListAsync(
            CancellationToken cancellationToken = default)
        {
            DemandAdministrator();
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var companies = await context.OrganizationCompanies.AsNoTracking()
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.Name)
                .ThenBy(item => item.Code)
                .Select(item => new OrganizationCompanyRecord(
                    item.Code, item.Name, item.IsActive, item.VersionNumber))
                .ToListAsync(cancellationToken);
            var departments = await context.OrganizationDepartments.AsNoTracking()
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.CompanyCode)
                .ThenBy(item => item.Name)
                .ThenBy(item => item.Code)
                .Select(item => new OrganizationDepartmentRecord(
                    item.Code, item.CompanyCode, item.Name, item.IsActive, item.VersionNumber))
                .ToListAsync(cancellationToken);
            return new OrganizationDirectoryRecord(companies, departments);
        }

        public Task<OrganizationCompanyRecord> SaveCompanyAsync(
            OrganizationCompanySaveRequest request,
            CancellationToken cancellationToken = default)
        {
            DemandAdministrator();
            ArgumentNullException.ThrowIfNull(request);
            string existingCode = NormalizeOptionalCode(request.ExistingCode);
            string code = NormalizeRequiredCode(request.Code, "公司代码");
            string name = NormalizeName(request.Name, "公司名称");
            if (existingCode.Length > 0 && existingCode != code)
            {
                throw new ServiceValidationException("公司代码是稳定授权标识，创建后不能修改。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    OrganizationCompany entity;
                    if (existingCode.Length == 0)
                    {
                        entity = new OrganizationCompany { Code = code, VersionNumber = 1 };
                        await context.OrganizationCompanies.AddAsync(entity, token);
                    }
                    else
                    {
                        entity = await context.OrganizationCompanies
                            .SingleOrDefaultAsync(item => item.Code == existingCode, token)
                            ?? throw new ResourceNotFoundException("公司目录项不存在。");
                        PrepareExpectedVersion(context, entity, request.ExpectedVersion, "公司");
                        if (!request.IsActive)
                        {
                            bool hasActiveDepartments = await context.OrganizationDepartments.AsNoTracking()
                                .AnyAsync(item => item.CompanyCode == code && item.IsActive, token);
                            bool hasActiveUsers = await context.Users.AsNoTracking()
                                .AnyAsync(item => item.CompanyScope == code && item.IsActive, token);
                            if (hasActiveDepartments || hasActiveUsers)
                            {
                                throw new ResourceConflictException("公司仍有启用部门或启用账号，请先停用或重新分配后再停用公司。");
                            }
                        }
                    }

                    entity.Name = name;
                    entity.IsActive = request.IsActive;
                    await SaveChangesAsync(context, "公司", token);
                    return new OrganizationCompanyRecord(
                        entity.Code, entity.Name, entity.IsActive, entity.VersionNumber);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public Task<OrganizationDepartmentRecord> SaveDepartmentAsync(
            OrganizationDepartmentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            DemandAdministrator();
            ArgumentNullException.ThrowIfNull(request);
            string existingCode = NormalizeOptionalCode(request.ExistingCode);
            string code = NormalizeRequiredCode(request.Code, "部门代码");
            string companyCode = NormalizeRequiredCode(request.CompanyCode, "所属公司");
            string name = NormalizeName(request.Name, "部门名称");
            if (existingCode.Length > 0 && existingCode != code)
            {
                throw new ServiceValidationException("部门代码是稳定授权标识，创建后不能修改。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    bool companyAvailable = await context.OrganizationCompanies.AsNoTracking()
                        .AnyAsync(item => item.Code == companyCode && item.IsActive, token);
                    if (!companyAvailable)
                    {
                        throw new ServiceValidationException("所属公司不存在或已停用。");
                    }

                    OrganizationDepartment entity;
                    if (existingCode.Length == 0)
                    {
                        entity = new OrganizationDepartment { Code = code, VersionNumber = 1 };
                        await context.OrganizationDepartments.AddAsync(entity, token);
                    }
                    else
                    {
                        entity = await context.OrganizationDepartments
                            .SingleOrDefaultAsync(item => item.Code == existingCode, token)
                            ?? throw new ResourceNotFoundException("部门目录项不存在。");
                        PrepareExpectedVersion(context, entity, request.ExpectedVersion, "部门");
                        if (entity.CompanyCode != companyCode)
                        {
                            bool hasUsers = await context.Users.AsNoTracking()
                                .AnyAsync(item => item.DepartmentId == code, token);
                            if (hasUsers)
                            {
                                throw new ResourceConflictException("部门已有账号引用，不能更换所属公司。");
                            }
                        }
                        if (!request.IsActive)
                        {
                            bool hasActiveUsers = await context.Users.AsNoTracking()
                                .AnyAsync(item => item.DepartmentId == code && item.IsActive, token);
                            if (hasActiveUsers)
                            {
                                throw new ResourceConflictException("部门仍有启用账号，请先停用或重新分配后再停用部门。");
                            }
                        }
                    }

                    entity.CompanyCode = companyCode;
                    entity.Name = name;
                    entity.IsActive = request.IsActive;
                    await SaveChangesAsync(context, "部门", token);
                    return new OrganizationDepartmentRecord(
                        entity.Code, entity.CompanyCode, entity.Name, entity.IsActive, entity.VersionNumber);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private void DemandAdministrator()
        {
            if (!BusinessDataAccessScope.CanViewAllBusinessData(_currentUserContext.CurrentUser))
            {
                throw new PermissionDeniedException("只有管理员可以维护组织目录。");
            }
        }

        private static void PrepareExpectedVersion<TEntity>(
            AppDbContext context,
            TEntity entity,
            int expectedVersion,
            string entityName)
            where TEntity : class
        {
            var property = context.Entry(entity).Property<int>(nameof(OrganizationCompany.VersionNumber));
            if (expectedVersion <= 0 || property.CurrentValue != expectedVersion)
            {
                throw new BusinessConcurrencyException($"该{entityName}目录项已被其他管理员修改，请刷新后重试。");
            }
            property.OriginalValue = expectedVersion;
        }

        private static async Task SaveChangesAsync(
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
                throw new BusinessConcurrencyException(
                    $"该{entityName}目录项已被其他管理员修改，请刷新后重试。",
                    exception);
            }
            catch (DbUpdateException exception) when (
                RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                throw new ResourceConflictException($"{entityName}代码已存在。", exception);
            }
        }

        private static string NormalizeName(string? value, string field)
        {
            string normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            if (normalized.Length == 0 || normalized.Length > 120)
            {
                throw new ServiceValidationException($"{field}不能为空且不能超过 120 个字符。");
            }
            return normalized;
        }

        private static string NormalizeRequiredCode(string? value, string field)
        {
            string normalized = NormalizeOptionalCode(value);
            if (!OrganizationCodePattern().IsMatch(normalized))
            {
                throw new ServiceValidationException(
                    $"{field}须为 1—50 位字母、数字、点、下划线或连字符，并以字母或数字开头。");
            }
            return normalized;
        }

        private static string NormalizeOptionalCode(string? value) =>
            (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

        [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N}._-]{0,49}$", RegexOptions.CultureInvariant)]
        private static partial Regex OrganizationCodePattern();
    }
}
