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
using ExportDocManager.Services.Security;

namespace ExportDocManager.Services.MasterData
{
    public class PayeeService : IPayeeService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IPayeeReadRepository _payeeReadRepository;
        private readonly BusinessDataAccessScope _accessScope;

        public PayeeService(
            IDbContextFactory<AppDbContext> contextFactory,
            IPayeeReadRepository payeeReadRepository,
            BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _payeeReadRepository = payeeReadRepository ?? throw new ArgumentNullException(nameof(payeeReadRepository));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
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
                    _accessScope.ApplyOwner(payee);
                    await context.Payees.AddAsync(payee, cancellationToken);
                }
                else
                {
                    var existing = await _accessScope.ApplyPayeeScope(context.Payees)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == payee.Id, cancellationToken);
                    if (existing == null)
                    {
                        throw new ResourceNotFoundException("收款对象不存在或不属于当前账号。");
                    }

                    _accessScope.DemandRecordAccess(existing, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Operate);
                    payee.OwnerUserId = existing.OwnerUserId;
                    payee.DepartmentId = existing.DepartmentId;
                    payee.CompanyScope = existing.CompanyScope;
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
                var entity = await _accessScope.ApplyPayeeScope(context.Payees)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (entity == null)
                {
                    return false;
                }

                _accessScope.DemandRecordAccess(entity, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Manage);
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
