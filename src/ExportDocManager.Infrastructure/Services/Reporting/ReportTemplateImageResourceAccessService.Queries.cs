using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting;

public sealed partial class ReportTemplateImageResourceAccessService
{
    public async Task<IReadOnlySet<string>> GetReadableIdsAsync(IReadOnlyCollection<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        if (resourceIds.Count > ReportTemplateV3ContractCatalog.MaxResources)
            throw new ServiceValidationException("图片资源数量超过上限。");
        var requested = resourceIds.Select(id => (id ?? string.Empty).Trim())
            .Where(id => ResourceIdRegex().IsMatch(id)).ToHashSet(StringComparer.Ordinal);
        int userId = _accessScope.CurrentUser?.Id ?? 0;
        if (requested.Count == 0 || userId <= 0 || !_accessScope.HasPermission(PermissionResourceCatalog.ReportResources, PermissionAction.View))
            return new HashSet<string>(StringComparer.Ordinal);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var readable = (await VisibleResources(context, userId).Where(item => requested.Contains(item.Id))
            .Select(item => item.Id).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        requested.ExceptWith(readable);
        if (requested.Count > 0 && _accessScope.HasPermission(PermissionResourceCatalog.ReportTemplates, PermissionAction.View))
        {
            readable.UnionWith(await _fileReferences.FindAsync(requested, type =>
                _accessScope.HasPermission(ReportDocumentAccessCatalog.GetSourceResource(type), PermissionAction.View), cancellationToken));
        }
        return readable;
    }

    public async Task ReadManyAsync(IReadOnlyCollection<string> resourceIds, Action<ReportTemplateImageResourceContent> consume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consume);
        await using var fileLock = await _storageLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var ids = resourceIds.Select(NormalizeResourceId).Distinct(StringComparer.Ordinal).ToArray();
        var readable = await GetReadableIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        if (ids.Any(id => !readable.Contains(id))) throw new ResourceNotFoundException("受控图片资源不存在或无权访问。");
        foreach (string id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            consume(await _resourceService.ReadAsync(id, cancellationToken).ConfigureAwait(false));
        }
    }

    public async Task<PagedResult<ReportTemplateImageResourceListItem>> QueryAsync(int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        _accessScope.DemandPermission(PermissionResourceCatalog.ReportResources, PermissionAction.View);
        int userId = RequireCurrentUserId();
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var fileLock = await _storageLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var activeIds = await context.ReportTemplateImageResources.AsNoTracking().Where(item => item.RecycledAt == null)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var readableIds = (await GetReadableIdsAsync(activeIds, cancellationToken).ConfigureAwait(false)).ToArray();
        var query = context.ReportTemplateImageResources.AsNoTracking().Where(item => readableIds.Contains(item.Id) && item.RecycledAt == null);
        int total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize)).Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.MediaType,
                item.ByteLength,
                item.Sha256,
                OwnsUpload = context.ReportTemplateImageResourceUploadClaims.Any(claim => claim.ResourceId == item.Id && claim.UserId == userId),
                IsReferenced = context.UserReportTemplateResourceReferences.Any(reference => reference.ResourceId == item.Id) ||
                    context.UserReportTemplateVersionResourceReferences.Any(reference => reference.ResourceId == item.Id)
            }).ToListAsync(cancellationToken);
        var fileReferences = await _fileReferences.FindAsync(rows.Select(item => item.Id).ToHashSet(StringComparer.Ordinal), null, cancellationToken);
        bool canRecycle = _accessScope.HasPermission(PermissionResourceCatalog.ReportResources, PermissionAction.Recycle);
        return new(rows.Select(item => new ReportTemplateImageResourceListItem(item.Id, item.MediaType, item.ByteLength,
            item.Sha256, item.OwnsUpload, item.IsReferenced || fileReferences.Contains(item.Id),
            canRecycle && item.OwnsUpload && !item.IsReferenced && !fileReferences.Contains(item.Id))).ToList(), total, pageNumber, pageSize);
    }

    private IQueryable<ReportTemplateImageResourceEntry> VisibleResources(AppDbContext context, int userId)
    {
        bool viewTemplates = _accessScope.HasPermission(PermissionResourceCatalog.ReportTemplates, PermissionAction.View);
        bool viewExports = viewTemplates && _accessScope.HasPermission(ReportDocumentAccessCatalog.GetSourceResource(ReportDocumentType.ExportDocument), PermissionAction.View);
        bool viewPayments = viewTemplates && _accessScope.HasPermission(ReportDocumentAccessCatalog.GetSourceResource(ReportDocumentType.PaymentVoucher), PermissionAction.View);
        var templates = _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
            .Where(item => viewExports && item.ReportType == "ExportDocument" || viewPayments && item.ReportType == "PaymentVoucher")
            .Select(item => item.Id);
        return context.ReportTemplateImageResources.AsNoTracking().Where(item => item.RecycledAt == null &&
            (context.ReportTemplateImageResourceUploadClaims.Any(claim => claim.ResourceId == item.Id && claim.UserId == userId) ||
             context.UserReportTemplateResourceReferences.Any(reference => reference.ResourceId == item.Id && templates.Contains(reference.UserReportTemplateId))));
    }
}
