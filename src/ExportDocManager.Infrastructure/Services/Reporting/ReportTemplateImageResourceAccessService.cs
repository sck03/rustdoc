using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting;

/// <summary>
/// Authoritative object-level boundary for content-addressed report images.
/// Physical hashes deduplicate bytes; database claims and template references
/// decide who may read or recycle them.
/// </summary>
public sealed partial class ReportTemplateImageResourceAccessService
    : IReportTemplateImageResourceAccessService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly BusinessDataAccessScope _accessScope;
    private readonly IBusinessClock _clock;

    public ReportTemplateImageResourceAccessService(
        IDbContextFactory<AppDbContext> contextFactory,
        BusinessDataAccessScope accessScope,
        IBusinessClock? clock = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        _clock = clock ?? BusinessClock.CreateSystem();
    }

    public async Task RegisterUploadAsync(
        ReportTemplateImageResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _accessScope.DemandPermission(PermissionResourceCatalog.ReportResources, PermissionAction.Upload);
        int userId = RequireCurrentUserId();
        string resourceId = NormalizeResourceId(resource.Id);
        string sha256 = (resource.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
        string mediaType = (resource.MediaType ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.Equals(resourceId[4..68], sha256, StringComparison.Ordinal) ||
            resource.ByteLength is <= 0 or > ReportTemplateV3ContractCatalog.MaxResourceBytes ||
            !ReportTemplateV3ContractCatalog.ImageMediaTypes.Contains(mediaType, StringComparer.Ordinal))
        {
            throw new ServiceValidationException("受控图片资源登记信息无效。");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await context.ReportTemplateImageResources
            .FirstOrDefaultAsync(item => item.Id == resourceId, cancellationToken);
        if (entry == null)
        {
            entry = new ReportTemplateImageResourceEntry
            {
                Id = resourceId,
                Sha256 = sha256,
                MediaType = mediaType,
                ByteLength = resource.ByteLength,
                CreatedByUserId = userId,
                CreatedAt = _clock.UtcNow,
                VersionNumber = 1
            };
            await context.ReportTemplateImageResources.AddAsync(entry, cancellationToken);
        }
        else
        {
            if (!string.Equals(entry.Sha256, sha256, StringComparison.Ordinal) ||
                !string.Equals(entry.MediaType, mediaType, StringComparison.Ordinal) ||
                entry.ByteLength != resource.ByteLength)
            {
                throw new UserVisibleInfrastructureException("受控图片资源登记与物理内容不一致，系统已停止复用该文件。");
            }

            if (entry.RecycledAt.HasValue)
            {
                entry.RecycledAt = null;
                entry.VersionNumber = checked(entry.VersionNumber + 1);
            }
        }

        bool hasClaim = await context.ReportTemplateImageResourceUploadClaims
            .AnyAsync(item => item.ResourceId == resourceId && item.UserId == userId, cancellationToken);
        if (!hasClaim)
        {
            await context.ReportTemplateImageResourceUploadClaims.AddAsync(
                new ReportTemplateImageResourceUploadClaim
                {
                    ResourceId = resourceId,
                    Resource = entry,
                    UserId = userId,
                    CreatedAt = _clock.UtcNow
                },
                cancellationToken);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            throw new ResourceConflictException("同一受控图片资源正在被并发登记，请重试。", exception);
        }
    }

    public async Task<bool> CanReadAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        string normalizedId = (resourceId ?? string.Empty).Trim();
        if (!ResourceIdRegex().IsMatch(normalizedId) ||
            !_accessScope.HasPermission(PermissionResourceCatalog.ReportResources, PermissionAction.View))
        {
            return false;
        }

        int userId = _accessScope.CurrentUser?.Id ?? 0;
        if (userId <= 0)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        bool exists = await context.ReportTemplateImageResources.AsNoTracking()
            .AnyAsync(item => item.Id == normalizedId && item.RecycledAt == null, cancellationToken);
        if (!exists)
        {
            return false;
        }

        bool ownsUpload = await context.ReportTemplateImageResourceUploadClaims.AsNoTracking()
            .AnyAsync(item => item.ResourceId == normalizedId && item.UserId == userId, cancellationToken);
        if (ownsUpload)
        {
            return true;
        }

        IQueryable<int> visibleTemplates = _accessScope
            .ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
            .Select(item => item.Id);
        return await context.UserReportTemplateResourceReferences.AsNoTracking()
            .AnyAsync(reference =>
                reference.ResourceId == normalizedId &&
                visibleTemplates.Contains(reference.UserReportTemplateId),
                cancellationToken);
    }

    public async Task<bool> RecycleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        _accessScope.DemandPermission(PermissionResourceCatalog.ReportResources, PermissionAction.Recycle);
        int userId = RequireCurrentUserId();
        string normalizedId = NormalizeResourceId(resourceId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await context.ReportTemplateImageResources
            .FirstOrDefaultAsync(item => item.Id == normalizedId, cancellationToken);
        if (entry == null || entry.RecycledAt.HasValue)
        {
            throw new ResourceNotFoundException("受控图片资源不存在或已回收。");
        }

        var claim = await context.ReportTemplateImageResourceUploadClaims
            .FirstOrDefaultAsync(
                item => item.ResourceId == normalizedId && item.UserId == userId,
                cancellationToken);
        if (claim == null)
        {
            throw new ResourceNotFoundException("受控图片资源不存在或不属于当前用户。");
        }

        bool isReferenced = await context.UserReportTemplateResourceReferences.AsNoTracking()
            .AnyAsync(item => item.ResourceId == normalizedId, cancellationToken) ||
            await context.UserReportTemplateVersionResourceReferences.AsNoTracking()
                .AnyAsync(item => item.ResourceId == normalizedId, cancellationToken);
        if (isReferenced)
        {
            throw new ResourceConflictException("该图片仍被报表模板或历史版本引用，不能回收。");
        }

        context.ReportTemplateImageResourceUploadClaims.Remove(claim);
        bool hasOtherClaim = await context.ReportTemplateImageResourceUploadClaims.AsNoTracking()
            .AnyAsync(
                item => item.ResourceId == normalizedId && item.UserId != userId,
                cancellationToken);
        if (!hasOtherClaim)
        {
            entry.RecycledAt = _clock.UtcNow;
            entry.VersionNumber = checked(entry.VersionNumber + 1);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BusinessConcurrencyException("图片资源正在被其他用户修改，请刷新后重试。", exception);
        }

        return !hasOtherClaim;
    }

    public async Task RollbackRecycleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        _accessScope.DemandPermission(PermissionResourceCatalog.ReportResources, PermissionAction.Recycle);
        int userId = RequireCurrentUserId();
        string normalizedId = NormalizeResourceId(resourceId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await context.ReportTemplateImageResources
            .FirstOrDefaultAsync(item => item.Id == normalizedId, cancellationToken)
            ?? throw new UserVisibleInfrastructureException("图片资源回收状态丢失，无法恢复重试。");
        bool hasClaim = await context.ReportTemplateImageResourceUploadClaims
            .AnyAsync(item => item.ResourceId == normalizedId && item.UserId == userId, cancellationToken);
        if (!hasClaim)
        {
            await context.ReportTemplateImageResourceUploadClaims.AddAsync(
                new ReportTemplateImageResourceUploadClaim
                {
                    ResourceId = normalizedId,
                    Resource = entry,
                    UserId = userId,
                    CreatedAt = _clock.UtcNow
                },
                cancellationToken);
        }

        if (entry.RecycledAt.HasValue)
        {
            entry.RecycledAt = null;
            entry.VersionNumber = checked(entry.VersionNumber + 1);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BusinessConcurrencyException("图片资源回收回滚与其他操作冲突，请刷新后重试。", exception);
        }
        catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            // Another request restored the same claim first. The resulting
            // active claim is exactly the idempotent rollback outcome.
        }
    }

    private int RequireCurrentUserId()
    {
        int userId = _accessScope.CurrentUser?.Id ?? 0;
        if (userId <= 0)
        {
            throw new PermissionDeniedException("当前会话没有可用于图片资源归属的用户身份。");
        }
        return userId;
    }

    private static string NormalizeResourceId(string? resourceId)
    {
        string normalized = (resourceId ?? string.Empty).Trim();
        if (!ResourceIdRegex().IsMatch(normalized))
        {
            throw new ServiceValidationException("受控图片资源 ID 无效。");
        }
        return normalized;
    }

    [GeneratedRegex(
        "^img-[0-9a-f]{64}\\.(?:png|jpg|gif|webp)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResourceIdRegex();
}
