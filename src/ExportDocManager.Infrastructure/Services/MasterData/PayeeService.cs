using System;
using System.Collections.Generic;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
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

        public async Task<int> SavePayeeAsync(
            Payee payee,
            CancellationToken cancellationToken = default)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(payee);
                NormalizePayee(payee);

                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                if (payee.Id == 0)
                {
                    await context.Payees.AddAsync(payee, cancellationToken);
                }
                else
                {
                    context.Payees.Update(payee);
                }
                await context.SaveChangesAsync(cancellationToken);
                return payee.Id;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该收款对象已被其他用户修改，请加载最新数据后再保存。", ex);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceValidationException(ex.Message, ex);
            }
        }

        public async Task<List<Payee>> GetAllPayeesAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _payeeReadRepository.QueryAsync(new PayeeReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<bool> DeletePayeeAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await context.Payees.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (entity == null)
                {
                    return false;
                }

                context.Payees.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该收款对象已被其他用户修改，请刷新后再试。", ex);
            }
        }

        public async Task<List<Payee>> SearchPayeesAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _payeeReadRepository.QueryAsync(
                new PayeeReadQuery
                {
                    Keyword = keyword ?? string.Empty
                },
                cancellationToken);
            return rows.ToList();
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
