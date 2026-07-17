using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public class CustomerService : ICustomerService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ICustomerReadRepository _customerReadRepository;

        public CustomerService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICustomerReadRepository customerReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _customerReadRepository = customerReadRepository ?? throw new ArgumentNullException(nameof(customerReadRepository));
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
                    await context.Customers.AddAsync(customer);
                }
                else
                {
                    context.Customers.Update(customer);
                }
                await context.SaveChangesAsync();
                return customer.Id;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("该客户数据已被其他用户修改，请刷新后重试。");
            }
            catch (Exception ex)
            {
                throw new Exception($"保存客户信息失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            try
            {
                var rows = await _customerReadRepository.QueryAsync(new CustomerReadQuery());
                return rows.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取客户列表失败: {ex.Message}", ex);
            }
        }

        public async Task<Customer> GetCustomerByIdAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.Customers.FirstOrDefaultAsync(x => x.Id == id);
            }
            catch (Exception ex)
            {
                throw new Exception($"根据ID获取客户失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteCustomerAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await context.Customers.FirstOrDefaultAsync(x => x.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Customers.Remove(entity);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"删除客户失败: {ex.Message}", ex);
            }
        }

        public async Task<Customer> GetCustomerByNameAsync(string name)
        {
            try
            {
                name = TextSearchHelper.NormalizeValue(name);
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.Customers.FirstOrDefaultAsync(x => x.CustomerNameEN == name);
            }
            catch (Exception ex)
            {
                throw new Exception($"根据名称获取客户失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Customer>> SearchCustomersAsync(string keyword)
        {
            var rows = await _customerReadRepository.QueryAsync(new CustomerReadQuery
            {
                Keyword = keyword ?? string.Empty
            });
            return rows.ToList();
        }

    }
}
