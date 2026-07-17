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
    public class ExporterService : IExporterService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IExporterReadRepository _exporterReadRepository;

        public ExporterService(
            IDbContextFactory<AppDbContext> contextFactory,
            IExporterReadRepository exporterReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _exporterReadRepository = exporterReadRepository ?? throw new ArgumentNullException(nameof(exporterReadRepository));
        }

        public async Task<int> SaveExporterAsync(Exporter exporter)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(exporter);
                MasterDataNormalization.NormalizeExporter(exporter);

                using var context = await _contextFactory.CreateDbContextAsync();
                if (exporter.Id == 0)
                {
                    await context.Exporters.AddAsync(exporter);
                }
                else
                {
                    context.Exporters.Update(exporter);
                }
                await context.SaveChangesAsync();
                return exporter.Id;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("该出口商数据已被其他用户修改，请刷新后重试。");
            }
            catch (Exception ex)
            {
                throw new Exception($"保存出口商信息失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Exporter>> GetAllExportersAsync()
        {
            try
            {
                var rows = await _exporterReadRepository.QueryAsync(new ExporterReadQuery());
                return rows.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取出口商列表失败: {ex.Message}", ex);
            }
        }

        public async Task<Exporter> GetExporterByIdAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.Exporters.FirstOrDefaultAsync(x => x.Id == id);
            }
            catch (Exception ex)
            {
                throw new Exception($"根据ID获取出口商失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteExporterAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await context.Exporters.FirstOrDefaultAsync(x => x.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Exporters.Remove(entity);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"删除出口商失败: {ex.Message}", ex);
            }
        }

        public async Task<Exporter> GetExporterByNameAsync(string name)
        {
            try
            {
                name = TextSearchHelper.NormalizeValue(name);
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.Exporters.FirstOrDefaultAsync(x => x.ExporterNameEN == name || x.ExporterNameCN == name);
            }
            catch (Exception ex)
            {
                throw new Exception($"根据名称获取出口商失败: {ex.Message}", ex);
            }
        }

        public async Task<List<Exporter>> SearchExportersAsync(string keyword)
        {
            var rows = await _exporterReadRepository.QueryAsync(new ExporterReadQuery
            {
                Keyword = keyword ?? string.Empty
            });
            return rows.ToList();
        }

    }
}
