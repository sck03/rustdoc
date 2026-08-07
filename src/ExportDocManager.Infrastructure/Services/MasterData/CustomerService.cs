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
            BusinessDataAccessScope accessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _customerReadRepository = customerReadRepository ?? throw new ArgumentNullException(nameof(customerReadRepository));
            _accessScope = accessScope ?? new BusinessDataAccessScope(new DatabaseConnectionSettings());
        }

        public async Task<int> SaveCustomerAsync(Customer customer)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(customer);
                MasterDataNormalization.NormalizeCustomer(customer);

                using var context = await _contextFactory.CreateDbContextAsync();
                if (customer.Id == 0)
                {
                    _accessScope.ApplyOwner(customer);
                    await context.Customers.AddAsync(customer);
                }
                else
                {
                    var existing = await _accessScope.ApplyCustomerScope(context.Customers)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == customer.Id);
                    if (existing == null) throw new ResourceNotFoundException("客户不存在或不属于当前账号。");
                    customer.OwnerUserId = existing.OwnerUserId;
                    customer.DepartmentId = existing.DepartmentId;
                    customer.CompanyScope = existing.CompanyScope;
                    context.Customers.Update(customer);
                }
                await context.SaveChangesAsync();
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
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户数据保存服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            try
            {
                var rows = await _customerReadRepository.QueryAsync(new CustomerReadQuery());
                return rows.ToList();
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户列表服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Customer> GetCustomerByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                return await _accessScope.ApplyCustomerScope(context.Customers)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户查询服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<bool> DeleteCustomerAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await _accessScope.ApplyCustomerScope(context.Customers)
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Customers.Remove(entity);
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该客户数据已被其他用户修改，请刷新后重试。", ex);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户删除服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Customer> GetCustomerByNameAsync(string name)
        {
            try
            {
                name = TextSearchHelper.NormalizeValue(name);
                using var context = await _contextFactory.CreateDbContextAsync();
                return await _accessScope.ApplyCustomerScope(context.Customers)
                    .FirstOrDefaultAsync(x => x.CustomerNameEN == name);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                throw new ServiceValidationException(ex.Message, ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户名称查询服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<List<Customer>> SearchCustomersAsync(string keyword)
        {
            try
            {
                var rows = await _customerReadRepository.QueryAsync(new CustomerReadQuery
                {
                    Keyword = keyword ?? string.Empty
                });
                return rows.ToList();
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("客户搜索服务暂时不可用，请稍后重试。", ex);
            }
        }

    }
}
