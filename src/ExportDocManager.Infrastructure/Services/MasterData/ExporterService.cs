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
        private readonly IExporterSealService? _exporterSealService;
        private readonly BusinessDataAccessScope _accessScope;

        public ExporterService(
            IDbContextFactory<AppDbContext> contextFactory,
            IExporterReadRepository exporterReadRepository,
            BusinessDataAccessScope accessScope,
            IExporterSealService? exporterSealService = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _exporterReadRepository = exporterReadRepository ?? throw new ArgumentNullException(nameof(exporterReadRepository));
            _exporterSealService = exporterSealService;
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<int> SaveExporterAsync(
            Exporter exporter,
            CancellationToken cancellationToken = default)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(exporter);
                MasterDataNormalization.NormalizeExporter(exporter);

                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                string? previousDocSealPath = null;
                string? previousCustomsSealPath = null;
                if (exporter.Id == 0)
                {
                    if (!string.IsNullOrWhiteSpace(exporter.DocSealPath) ||
                        !string.IsNullOrWhiteSpace(exporter.CustomsSealPath))
                    {
                        throw new ServiceValidationException("请先保存出口商基础资料，再通过受控上传保存印章图片。");
                    }
                    _accessScope.ApplyOwner(exporter);
                    await context.Exporters.AddAsync(exporter, cancellationToken);
                }
                else
                {
                    var existing = await _accessScope.ApplyExporterScope(context.Exporters)
                        .AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == exporter.Id, cancellationToken);
                    if (existing == null) throw new ResourceNotFoundException("出口商不存在或不属于当前账号。");
                    if (!IsUnchangedOrCleared(exporter.DocSealPath, existing.DocSealPath) ||
                        !IsUnchangedOrCleared(exporter.CustomsSealPath, existing.CustomsSealPath))
                    {
                        throw new ServiceValidationException("印章路径不能直接编辑，请使用印章上传按钮。");
                    }
                    previousDocSealPath = existing.DocSealPath;
                    previousCustomsSealPath = existing.CustomsSealPath;
                    _accessScope.DemandRecordAccess(existing, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Operate);
                    exporter.OwnerUserId = existing.OwnerUserId;
                    exporter.DepartmentId = existing.DepartmentId;
                    exporter.CompanyScope = existing.CompanyScope;
                    context.Exporters.Update(exporter);
                }

                await context.SaveChangesAsync(cancellationToken);
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
        }

        public async Task<List<Exporter>> GetAllExportersAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _exporterReadRepository.QueryAsync(new ExporterReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<Exporter?> GetExporterByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplyExporterScope(context.Exporters)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<bool> DeleteExporterAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await _accessScope.ApplyExporterScope(context.Exporters)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (entity == null)
                {
                    return false;
                }

                _accessScope.DemandRecordAccess(entity, PermissionModuleCatalog.DocumentMasterData, PermissionAction.Manage);
                context.Exporters.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
                _exporterSealService?.DeleteAllManagedSeals(id);
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该出口商数据已被其他用户修改，请刷新后重试。", ex);
            }
        }

        public async Task<Exporter?> GetExporterByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            name = TextSearchHelper.NormalizeValue(name);
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await _accessScope.ApplyExporterScope(context.Exporters)
                .FirstOrDefaultAsync(
                    x => x.ExporterNameEN == name || x.ExporterNameCN == name,
                    cancellationToken);
        }

        public async Task<List<Exporter>> SearchExportersAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _exporterReadRepository.QueryAsync(
                new ExporterReadQuery
                {
                    Keyword = keyword ?? string.Empty
                },
                cancellationToken);
            return rows.ToList();
        }

        private static bool IsUnchangedOrCleared(string? requestedPath, string? existingPath) =>
            string.IsNullOrWhiteSpace(requestedPath) ||
            string.Equals(requestedPath, existingPath, StringComparison.Ordinal);

    }
}
