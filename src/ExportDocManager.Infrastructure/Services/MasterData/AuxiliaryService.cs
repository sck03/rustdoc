using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public class AuxiliaryService : IAuxiliaryService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IPortReadRepository _portReadRepository;
        private readonly IUnitReadRepository _unitReadRepository;

        public AuxiliaryService(
            IDbContextFactory<AppDbContext> contextFactory,
            IPortReadRepository portReadRepository,
            IUnitReadRepository unitReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _portReadRepository = portReadRepository ?? throw new ArgumentNullException(nameof(portReadRepository));
            _unitReadRepository = unitReadRepository ?? throw new ArgumentNullException(nameof(unitReadRepository));
        }

        // --- Port Methods ---

        public async Task<List<Port>> GetAllPortsAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _portReadRepository.QueryAsync(new PortReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<List<Port>> SearchPortsAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _portReadRepository.QueryAsync(new PortReadQuery
            {
                Keyword = keyword ?? string.Empty
            }, cancellationToken);
            return rows.ToList();
        }

        public async Task SavePortAsync(Port port, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(port);
            AuxiliaryDataTextHelper.NormalizePort(port);
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                if (port.Id == 0)
                {
                    context.Ports.Add(port);
                }
                else
                {
                    context.Ports.Update(port);
                }
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该港口已被其他用户修改，请加载最新数据后再保存。", ex);
            }
        }

        public async Task DeletePortAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var port = await context.Ports.FindAsync([id], cancellationToken);
            if (port != null)
            {
                context.Ports.Remove(port);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        // --- Unit Methods ---

        public async Task<List<Unit>> GetAllUnitsAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _unitReadRepository.QueryAsync(new UnitReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<List<Unit>> SearchUnitsAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _unitReadRepository.QueryAsync(new UnitReadQuery
            {
                Keyword = keyword ?? string.Empty
            }, cancellationToken);
            return rows.ToList();
        }

        public async Task SaveUnitAsync(Unit unit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(unit);
            AuxiliaryDataTextHelper.NormalizeUnit(unit);
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                if (unit.Id == 0)
                {
                    context.Units.Add(unit);
                }
                else
                {
                    context.Units.Update(unit);
                }
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该单位已被其他用户修改，请加载最新数据后再保存。", ex);
            }
        }

        public async Task DeleteUnitAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var unit = await context.Units.FindAsync([id], cancellationToken);
            if (unit != null)
            {
                context.Units.Remove(unit);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        public async Task<List<string>> GetUnitsByEnglishNameAsync(
            string nameEn,
            CancellationToken cancellationToken = default)
        {
            var normalizedName = TextSearchHelper.NormalizeUpperValue(nameEn);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return [];
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Units
                .AsNoTracking()
                .Where(unit => unit.NameEN != null && unit.NameEN.ToUpper() == normalizedName)
                .Select(u => u.NameCN ?? string.Empty)
                .Where(name => name != string.Empty)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
    }
}
