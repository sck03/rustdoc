#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public class CustomerService : ICustomerService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ICustomerReadRepository _customerReadRepository;
        private readonly BusinessDataAccessScope _accessScope;

        public CustomerService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICustomerReadRepository customerReadRepository,
            BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _customerReadRepository = customerReadRepository ?? throw new ArgumentNullException(nameof(customerReadRepository));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<int> SaveCustomerAsync(
            Customer customer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(customer);
                MasterDataNormalization.NormalizeCustomer(customer);

                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                if (customer.Id == 0)
                {
                    _accessScope.ApplyOwner(customer);
                    await context.Customers.AddAsync(customer, cancellationToken);
                }
                else
                {
                    var existing = await _accessScope.ApplyCustomerScope(context.Customers)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == customer.Id, cancellationToken);
                    if (existing == null) throw new ResourceNotFoundException("客户不存在或不属于当前账号。");
                    _accessScope.DemandRecordAccess(existing, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Operate);
                    customer.OwnerUserId = existing.OwnerUserId;
                    customer.DepartmentId = existing.DepartmentId;
                    customer.CompanyScope = existing.CompanyScope;
                    context.Customers.Update(customer);
                }
                await context.SaveChangesAsync(cancellationToken);
                return customer.Id;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该客户数据已被其他用户修改，请刷新后重试。", ex);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceValidationException(ex.Message, ex);
            }
        }

        public async Task<List<Customer>> GetAllCustomersAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _customerReadRepository.QueryAsync(new CustomerReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<Customer?> GetCustomerByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplyCustomerScope(context.Customers)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<bool> DeleteCustomerAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await _accessScope.ApplyCustomerScope(context.Customers)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (entity == null)
                {
                    return false;
                }

                _accessScope.DemandRecordAccess(entity, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Manage);
                context.Customers.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该客户数据已被其他用户修改，请刷新后重试。", ex);
            }
        }

        public async Task<Customer?> GetCustomerByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            name = TextSearchHelper.NormalizeValue(name);
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplyCustomerScope(context.Customers)
                .FirstOrDefaultAsync(x => x.CustomerNameEN == name, cancellationToken);
        }

        public async Task<List<Customer>> SearchCustomersAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _customerReadRepository.QueryAsync(
                new CustomerReadQuery
                {
                    Keyword = keyword ?? string.Empty
                },
                cancellationToken);
            return rows.ToList();
        }

    }
}
