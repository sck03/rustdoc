using System;
using System.Collections.Generic;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public class PayeeService : IPayeeService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IPayeeReadRepository _payeeReadRepository;

        public PayeeService(
            IDbContextFactory<AppDbContext> contextFactory,
            IPayeeReadRepository payeeReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _payeeReadRepository = payeeReadRepository ?? throw new ArgumentNullException(nameof(payeeReadRepository));
        }

        public async Task<int> SavePayeeAsync(Payee payee)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(payee);
                NormalizePayee(payee);

                using var context = await _contextFactory.CreateDbContextAsync();
                if (payee.Id == 0)
                {
                    await context.Payees.AddAsync(payee);
                }
                else
                {
                    context.Payees.Update(payee);
                }
                await context.SaveChangesAsync();
                return payee.Id;
            }
            catch (Exception ex)
            {
                throw new Exception($"保存支付对象信息失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Payee>> GetAllPayeesAsync()
        {
            try
            {
                var rows = await _payeeReadRepository.QueryAsync(new PayeeReadQuery());
                return rows.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取支付对象列表失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeletePayeeAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await context.Payees.FirstOrDefaultAsync(x => x.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Payees.Remove(entity);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"删除支付对象失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Payee>> SearchPayeesAsync(string keyword)
        {
            try
            {
                var rows = await _payeeReadRepository.QueryAsync(new PayeeReadQuery
                {
                    Keyword = keyword ?? string.Empty
                });
                return rows.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"搜索支付对象失败: {ex.Message}", ex);
            }
        }

        private static void NormalizePayee(Payee payee)
        {
            payee.Category = TextSearchHelper.NormalizeValue(payee.Category);
            payee.Name = TextSearchHelper.NormalizeValue(payee.Name);
            payee.BankName = TextSearchHelper.NormalizeValue(payee.BankName);
            payee.RMBAccount = TextSearchHelper.NormalizeValue(payee.RMBAccount);
            payee.USDAccount = TextSearchHelper.NormalizeValue(payee.USDAccount);
            payee.ContactPerson = TextSearchHelper.NormalizeValue(payee.ContactPerson);
            payee.Phone = TextSearchHelper.NormalizeValue(payee.Phone);
            payee.Notes = TextSearchHelper.NormalizeValue(payee.Notes);
        }
    }
}
