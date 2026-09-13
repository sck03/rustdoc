using System.Linq.Expressions;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting;

public sealed partial class UserReportTemplateService
{
    private static readonly Expression<Func<UserReportTemplate, UserReportTemplate>> Metadata = item => new UserReportTemplate
    {
        Id = item.Id,
        ReportType = item.ReportType,
        Name = item.Name,
        Status = item.Status,
        ShareScope = item.ShareScope,
        VersionNumber = item.VersionNumber,
        OwnerUserId = item.OwnerUserId,
        CompanyScope = item.CompanyScope,
        DepartmentId = item.DepartmentId
    };

    public async Task<PagedResult<UserReportTemplateSummaryRecord>> ListAsync(ReportDocumentType reportType,
        bool includeArchived = false, int pageNumber = 1, int pageSize = 50, string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        DemandReportTypeAccess(reportType);
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        string type = reportType.ToString();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking()).Where(item => item.ReportType == type);
        if (!includeArchived) query = query.Where(item => item.Status != TemplateLifecycleStatusCatalog.Archived);
        string search = (keyword ?? string.Empty).Trim();
        if (search.Length > 150) throw new ServiceValidationException("模板搜索名称不能超过 150 个字符。");
        if (search.Length > 0) query = query.Where(item => item.Name.Contains(search));
        int total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.Status == TemplateLifecycleStatusCatalog.Published)
            .ThenBy(item => item.Name).ThenBy(item => item.Id)
            .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize)).Take(pageSize).Select(Metadata).ToListAsync(cancellationToken);
        return new(rows.Select(ToSummary).ToList(), total, pageNumber, pageSize);
    }

    public async Task<UserReportTemplateRecord> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var template = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("报表模板不存在或无权访问。");
        DemandReportTypeAccess(Enum.Parse<ReportDocumentType>(template.ReportType, true));
        return ToRecord(template);
    }

    public async Task<PagedResult<UserReportTemplateVersionRecord>> ListVersionsAsync(int id, int pageNumber = 1,
        int pageSize = 20, CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var template = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
            .Select(Metadata).FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("报表模板不存在或无权访问。");
        DemandReportTypeAccess(Enum.Parse<ReportDocumentType>(template.ReportType, true));
        bool canRestore = CanAct(template, PermissionAction.Restore);
        var query = context.UserReportTemplateVersions.AsNoTracking().Where(item => item.UserReportTemplateId == id);
        int total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.VersionNumber).ThenBy(item => item.Id)
            .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize)).Take(pageSize)
            .Select(item => new UserReportTemplateVersionRecord(item.Id, item.UserReportTemplateId, item.VersionNumber,
                item.ChangeType, item.Name, item.Status, item.ShareScope, item.ChangedBy, item.CreatedAt, canRestore))
            .ToListAsync(cancellationToken);
        return new(rows, total, pageNumber, pageSize);
    }
}
