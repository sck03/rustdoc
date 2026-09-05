using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers
{
    public sealed class SupplierAssessmentService : ISupplierAssessmentService
    {
        private static readonly IReadOnlySet<string> AllowedKinds =
            SupplierAssessmentCatalog.Kinds.ToHashSet(StringComparer.Ordinal);
        private static readonly IReadOnlySet<string> AllowedConclusions =
            SupplierAssessmentCatalog.Conclusions.ToHashSet(StringComparer.Ordinal);
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly IBusinessClock _clock;

        public SupplierAssessmentService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope,
            IBusinessClock clock)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async Task<IReadOnlyList<SupplierAssessmentRecord>> ListAsync(
            int supplierCompanyId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(
                    context,
                    supplierCompanyId,
                    PermissionAction.View,
                    cancellationToken)) return [];

            int currentUserId = _accessScope.CurrentUser?.Id ?? 0;
            bool canApprove = await CanAccessSupplierAsync(
                context, supplierCompanyId, PermissionAction.Approve, cancellationToken);
            var rows = await context.SupplierAssessments.AsNoTracking()
                .Where(item => item.SupplierCompanyId == supplierCompanyId &&
                    (item.Status == SupplierAssessmentStatusCatalog.Confirmed ||
                     item.OwnerUserId == currentUserId || canApprove))
                .OrderByDescending(item => item.Id)
                .ToListAsync(cancellationToken);
            return rows.OrderByDescending(item => item.AssessmentDate).ThenByDescending(item => item.Id)
                .Select(ToRecord).ToArray();
        }

        public async Task<SupplierAssessmentOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var suppliers = _accessScope.ApplySupplierScopeForPermission(
                context.SupplierCompanies.AsNoTracking(),
                PermissionResourceCatalog.SupplierAssessments,
                PermissionAction.View);
            int totalSuppliers = await suppliers.CountAsync(cancellationToken);
            if (totalSuppliers == 0)
                return new SupplierAssessmentOverview(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

            var assessmentSummaries =
                from assessment in context.SupplierAssessments.AsNoTracking()
                    .Where(item => item.Status == SupplierAssessmentStatusCatalog.Confirmed)
                join supplier in suppliers on assessment.SupplierCompanyId equals supplier.Id
                group new { assessment, supplier } by new
                {
                    supplier.Id,
                    supplier.Name,
                    supplier.Status,
                    supplier.Category
                }
                into groupRows
                select new
                {
                    SupplierCompanyId = groupRows.Key.Id,
                    SupplierName = groupRows.Key.Name,
                    SupplierStatus = groupRows.Key.Status,
                    groupRows.Key.Category,
                    AssessmentCount = groupRows.Count(),
                    LatestAssessmentId = groupRows
                        .OrderByDescending(row => row.assessment.AssessmentDate)
                        .ThenByDescending(row => row.assessment.Id)
                        .Select(row => row.assessment.Id)
                        .First()
                };

            var items = await (from summary in assessmentSummaries
                               join latest in context.SupplierAssessments.AsNoTracking()
                                   on summary.LatestAssessmentId equals latest.Id
                               select new SupplierAssessmentOverviewItem(
                                   summary.SupplierCompanyId,
                                   summary.SupplierName,
                                   summary.SupplierStatus,
                                   summary.Category,
                                   summary.AssessmentCount,
                                   latest.AssessmentDate,
                                   latest.AssessmentKind,
                                   latest.QualityScore,
                                   latest.DeliveryScore,
                                   latest.ServiceScore,
                                   latest.PriceScore,
                                   (latest.QualityScore + latest.DeliveryScore + latest.ServiceScore + latest.PriceScore) / 4m,
                                   latest.Conclusion,
                                   latest.Notes))
                .ToListAsync(cancellationToken);

            items = items.OrderBy(item => ConclusionPriority(item.Conclusion))
                .ThenByDescending(item => item.LatestAssessmentDate)
                .ThenBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SupplierAssessmentOverview(
                totalSuppliers, items.Count, totalSuppliers - items.Count,
                items.Count(item => item.Conclusion == "优先合作"),
                items.Count(item => item.Conclusion == "合格"),
                items.Count(item => item.Conclusion == "观察"),
                items.Count(item => item.Conclusion == "暂停合作"),
                Mean(items.Select(item => item.QualityScore)),
                Mean(items.Select(item => item.DeliveryScore)),
                Mean(items.Select(item => item.ServiceScore)),
                Mean(items.Select(item => item.PriceScore)),
                items);
        }

        public async Task<SupplierAssessmentRecord> SaveAsync(
            SupplierAssessmentSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Validate(request);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            string writeAction = request.Id <= 0 ? PermissionAction.Create : PermissionAction.Edit;
            if (!await CanAccessSupplierAsync(
                    context,
                    request.SupplierCompanyId,
                    writeAction,
                    cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");

            bool isNew = request.Id <= 0;
            DateTimeOffset now = _clock.UtcNow;
            var entity = request.Id > 0
                ? await context.SupplierAssessments.FirstOrDefaultAsync(
                    item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId,
                    cancellationToken) ?? throw new KeyNotFoundException("供应商评价不存在。")
                : new SupplierAssessment
                {
                    SupplierCompanyId = request.SupplierCompanyId,
                    Status = SupplierAssessmentStatusCatalog.Draft,
                    OwnerUserId = _accessScope.CurrentUser?.Id,
                    CreatedAt = now,
                    UpdatedAt = now,
                    VersionNumber = 1
                };

            if (!isNew)
            {
                if (entity.Status == SupplierAssessmentStatusCatalog.Confirmed)
                    throw new ResourceConflictException("已确认的供应商评价不可修改；如需更正，请新建一条复评记录。");
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber);
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }

            if (entity.Id == 0) await context.SupplierAssessments.AddAsync(entity, cancellationToken);
            entity.AssessmentDate = request.AssessmentDate;
            entity.AssessmentKind = request.AssessmentKind.Trim();
            entity.QualityScore = request.QualityScore;
            entity.DeliveryScore = request.DeliveryScore;
            entity.ServiceScore = request.ServiceScore;
            entity.PriceScore = request.PriceScore;
            entity.Conclusion = request.Conclusion.Trim();
            entity.Notes = (request.Notes ?? string.Empty).Trim();
            entity.AssessedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty;
            entity.UpdatedAt = now;

            await SaveWithConcurrencyAsync(context, cancellationToken);
            return ToRecord(entity);
        }

        public Task<SupplierAssessmentRecord> ConfirmAsync(
            int supplierCompanyId,
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await CanAccessSupplierAsync(
                            context,
                            supplierCompanyId,
                            PermissionAction.Approve,
                            token))
                        throw new KeyNotFoundException("供应商不存在或无权确认评价。");

                    var entity = await context.SupplierAssessments.FirstOrDefaultAsync(
                            item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                            token)
                        ?? throw new KeyNotFoundException("供应商评价不存在。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber);
                    if (entity.Status == SupplierAssessmentStatusCatalog.Confirmed)
                        return ToRecord(entity);

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    entity.Status = SupplierAssessmentStatusCatalog.Confirmed;
                    entity.ConfirmedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty;
                    entity.ConfirmedAt = _clock.UtcNow;
                    entity.UpdatedAt = entity.ConfirmedAt.Value;
                    entity.VersionNumber++;
                    await SaveWithConcurrencyAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        public Task<bool> DeleteAsync(
            int supplierCompanyId,
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    if (!await CanAccessSupplierAsync(
                            context,
                            supplierCompanyId,
                            PermissionAction.Delete,
                            token)) return false;
                    var entity = await context.SupplierAssessments.FirstOrDefaultAsync(
                        item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                        token);
                    if (entity == null) return false;
                    if (entity.Status == SupplierAssessmentStatusCatalog.Confirmed)
                        throw new ResourceConflictException("已确认的供应商评价属于审计记录，不能删除。");
                    EnsureExpectedVersion(expectedVersion, entity.VersionNumber);
                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.SupplierAssessments.Remove(entity);
                    await SaveWithConcurrencyAsync(context, token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);

        private Task<bool> CanAccessSupplierAsync(
            AppDbContext context,
            int supplierCompanyId,
            string action,
            CancellationToken cancellationToken) =>
            _accessScope.ApplySupplierScopeForPermission(
                    context.SupplierCompanies.AsNoTracking(),
                    PermissionResourceCatalog.SupplierAssessments,
                    action)
                .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken);

        private void Validate(SupplierAssessmentSaveRequest request)
        {
            if (request.SupplierCompanyId <= 0) throw new ArgumentException("请选择供应商。");
            if (request.AssessmentDate == default) throw new ArgumentException("请选择评价日期。");
            if (request.AssessmentDate > _clock.Today) throw new ArgumentException("评价日期不能晚于今天。");
            if (!AllowedKinds.Contains((request.AssessmentKind ?? string.Empty).Trim())) throw new ArgumentException("评价类型无效。");
            if (!AllowedConclusions.Contains((request.Conclusion ?? string.Empty).Trim())) throw new ArgumentException("评价结论无效。");
            ValidateScore(request.QualityScore, "质量");
            ValidateScore(request.DeliveryScore, "交期");
            ValidateScore(request.ServiceScore, "服务");
            ValidateScore(request.PriceScore, "价格");
            if ((request.Notes ?? string.Empty).Trim().Length > 1000) throw new ArgumentException("评价备注不能超过 1000 个字符。");
        }

        private static void ValidateScore(int value, string name)
        {
            if (value is < 1 or > 5) throw new ArgumentException($"{name}评分必须在 1 至 5 分之间。");
        }

        private static SupplierAssessmentRecord ToRecord(SupplierAssessment item) => new(
            item.Id, item.SupplierCompanyId, item.AssessmentDate, item.AssessmentKind,
            item.QualityScore, item.DeliveryScore, item.ServiceScore, item.PriceScore,
            Math.Round((item.QualityScore + item.DeliveryScore + item.ServiceScore + item.PriceScore) / 4m, 2),
            item.Conclusion, item.Notes, item.AssessedBy, item.Status, item.ConfirmedBy, item.ConfirmedAt,
            item.CreatedAt, item.UpdatedAt,
            item.VersionNumber);

        private static void EnsureExpectedVersion(int expectedVersion, int currentVersion)
        {
            if (expectedVersion <= 0)
                throw new BusinessConcurrencyException("保存现有供应商评价时必须提供版本号，请刷新后重试。");
            if (expectedVersion != currentVersion)
                throw new BusinessConcurrencyException("该供应商评价已被其他用户修改，请刷新后重试。");
        }

        private static async Task SaveWithConcurrencyAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException(
                    "该供应商评价已被其他用户修改，请刷新后重试。",
                    exception);
            }
        }

        private static decimal Average(int quality, int delivery, int service, int price) =>
            Math.Round((quality + delivery + service + price) / 4m, 2);

        private static decimal Mean(IEnumerable<int> values)
        {
            int[] rows = values.ToArray();
            return rows.Length == 0 ? 0m : Math.Round((decimal)rows.Average(), 2);
        }

        private static int ConclusionPriority(string conclusion) => conclusion switch
        {
            SupplierAssessmentCatalog.Paused => 0,
            SupplierAssessmentCatalog.Watch => 1,
            SupplierAssessmentCatalog.Qualified => 2,
            SupplierAssessmentCatalog.Preferred => 3,
            _ => 4
        };
    }
}
