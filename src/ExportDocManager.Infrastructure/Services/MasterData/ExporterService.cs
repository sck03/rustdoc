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
    public class ExporterService : IExporterService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IExporterReadRepository _exporterReadRepository;
        private readonly IExporterSealService _exporterSealService;
        private readonly BusinessDataAccessScope _accessScope;

        public ExporterService(
            IDbContextFactory<AppDbContext> contextFactory,
            IExporterReadRepository exporterReadRepository,
            IExporterSealService exporterSealService = null,
            BusinessDataAccessScope accessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _exporterReadRepository = exporterReadRepository ?? throw new ArgumentNullException(nameof(exporterReadRepository));
            _exporterSealService = exporterSealService;
            _accessScope = accessScope ?? new BusinessDataAccessScope(new DatabaseConnectionSettings());
        }

        public async Task<int> SaveExporterAsync(Exporter exporter)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(exporter);
                MasterDataNormalization.NormalizeExporter(exporter);

                using var context = await _contextFactory.CreateDbContextAsync();
                string previousDocSealPath = null;
                string previousCustomsSealPath = null;
                if (exporter.Id == 0)
                {
                    if (!string.IsNullOrWhiteSpace(exporter.DocSealPath) ||
                        !string.IsNullOrWhiteSpace(exporter.CustomsSealPath))
                    {
                        throw new ServiceValidationException("请先保存出口商基础资料，再通过受控上传保存印章图片。");
                    }
                    _accessScope.ApplyOwner(exporter);
                    await context.Exporters.AddAsync(exporter);
                }
                else
                {
                    var existing = await _accessScope.ApplyExporterScope(context.Exporters)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == exporter.Id);
                    if (existing == null) throw new ResourceNotFoundException("出口商不存在或不属于当前账号。");
                    if (!IsUnchangedOrCleared(exporter.DocSealPath, existing.DocSealPath) ||
                        !IsUnchangedOrCleared(exporter.CustomsSealPath, existing.CustomsSealPath))
                    {
                        throw new ServiceValidationException("印章路径不能直接编辑，请使用印章上传按钮。");
                    }
                    previousDocSealPath = existing.DocSealPath;
                    previousCustomsSealPath = existing.CustomsSealPath;
                    exporter.OwnerUserId = existing.OwnerUserId;
                    exporter.DepartmentId = existing.DepartmentId;
                    exporter.CompanyScope = existing.CompanyScope;
                    context.Exporters.Update(exporter);
                }

                await context.SaveChangesAsync();
                _exporterSealService?.DeleteReplacedManagedSeal(
                    exporter.Id,
                    previousDocSealPath,
                    exporter.DocSealPath);
                _exporterSealService?.DeleteReplacedManagedSeal(
                    exporter.Id,
                    previousCustomsSealPath,
                    exporter.CustomsSealPath);
                return exporter.Id;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该出口商数据已被其他用户修改，请刷新后重试。", ex);
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
                throw new InfrastructureServiceException("出口商数据保存服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<List<Exporter>> GetAllExportersAsync()
        {
            try
            {
                var rows = await _exporterReadRepository.QueryAsync(new ExporterReadQuery());
                return rows.ToList();
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("出口商列表服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Exporter> GetExporterByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                return await _accessScope.ApplyExporterScope(context.Exporters)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("出口商查询服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<bool> DeleteExporterAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await _accessScope.ApplyExporterScope(context.Exporters)
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Exporters.Remove(entity);
                await context.SaveChangesAsync();
                _exporterSealService?.DeleteAllManagedSeals(id);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该出口商数据已被其他用户修改，请刷新后重试。", ex);
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("出口商删除服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Exporter> GetExporterByNameAsync(string name)
        {
            try
            {
                name = TextSearchHelper.NormalizeValue(name);
                using var context = await _contextFactory.CreateDbContextAsync();
                return await _accessScope.ApplyExporterScope(context.Exporters)
                    .FirstOrDefaultAsync(x => x.ExporterNameEN == name || x.ExporterNameCN == name);
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
                throw new InfrastructureServiceException("出口商名称查询服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<List<Exporter>> SearchExportersAsync(string keyword)
        {
            try
            {
                var rows = await _exporterReadRepository.QueryAsync(new ExporterReadQuery
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
                throw new InfrastructureServiceException("出口商搜索服务暂时不可用，请稍后重试。", ex);
            }
        }

        private static bool IsUnchangedOrCleared(string requestedPath, string existingPath) =>
            string.IsNullOrWhiteSpace(requestedPath) ||
            string.Equals(requestedPath, existingPath, StringComparison.Ordinal);

    }
}
