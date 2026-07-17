using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers
{
    public sealed class SupplierAssessmentService : ISupplierAssessmentService
    {
        private static readonly string[] AllowedKinds = ["定期评价", "订单复盘", "样品评估", "其它"];
        private static readonly string[] AllowedConclusions = ["优先合作", "合格", "观察", "暂停合作"];
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public SupplierAssessmentService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<IReadOnlyList<SupplierAssessmentRecord>> ListAsync(
            int supplierCompanyId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(context, supplierCompanyId, cancellationToken)) return [];

            var rows = await context.SupplierAssessments.AsNoTracking()
                .Where(item => item.SupplierCompanyId == supplierCompanyId)
                .OrderByDescending(item => item.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            return rows.OrderByDescending(item => item.AssessedAt).ThenByDescending(item => item.Id)
                .Select(ToRecord).ToArray();
        }

        public async Task<SupplierAssessmentOverview> GetOverviewAsync(
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var suppliers = await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .Select(item => new { item.Id, item.Name, item.Status, item.Category })
                .ToListAsync(cancellationToken);
            if (suppliers.Count == 0)
                return new SupplierAssessmentOverview(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

            int[] supplierIds = suppliers.Select(item => item.Id).ToArray();
            var assessments = await context.SupplierAssessments.AsNoTracking()
                .Where(item => supplierIds.Contains(item.SupplierCompanyId))
                .Select(item => new
                {
                    item.Id, item.SupplierCompanyId, item.AssessedAt, item.AssessmentKind,
                    item.QualityScore, item.DeliveryScore, item.ServiceScore, item.PriceScore,
                    item.Conclusion, item.Notes
                })
                .ToListAsync(cancellationToken);

            var groups = assessments.GroupBy(item => item.SupplierCompanyId)
                .ToDictionary(group => group.Key, group => group
                    .OrderByDescending(item => item.AssessedAt)
                    .ThenByDescending(item => item.Id)
                    .ToArray());
            var items = suppliers.Where(item => groups.ContainsKey(item.Id)).Select(supplier =>
            {
                var rows = groups[supplier.Id];
                var latest = rows[0];
                return new SupplierAssessmentOverviewItem(
                    supplier.Id, supplier.Name, supplier.Status, supplier.Category, rows.Length,
                    latest.AssessedAt, latest.AssessmentKind,
                    latest.QualityScore, latest.DeliveryScore, latest.ServiceScore, latest.PriceScore,
                    Average(latest.QualityScore, latest.DeliveryScore, latest.ServiceScore, latest.PriceScore),
                    latest.Conclusion, latest.Notes);
            }).OrderBy(item => ConclusionPriority(item.Conclusion))
                .ThenByDescending(item => item.LatestAssessedAt)
                .ThenBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new SupplierAssessmentOverview(
                suppliers.Count, items.Length, suppliers.Count - items.Length,
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
            if (!await CanAccessSupplierAsync(context, request.SupplierCompanyId, cancellationToken))
                throw new KeyNotFoundException("供应商不存在或无权访问。");

            var entity = request.Id > 0
                ? await context.SupplierAssessments.FirstOrDefaultAsync(
                    item => item.Id == request.Id && item.SupplierCompanyId == request.SupplierCompanyId,
                    cancellationToken) ?? throw new KeyNotFoundException("供应商评价不存在。")
                : new SupplierAssessment { SupplierCompanyId = request.SupplierCompanyId };

            if (entity.Id == 0) await context.SupplierAssessments.AddAsync(entity, cancellationToken);
            entity.AssessedAt = request.AssessedAt;
            entity.AssessmentKind = request.AssessmentKind.Trim();
            entity.QualityScore = request.QualityScore;
            entity.DeliveryScore = request.DeliveryScore;
            entity.ServiceScore = request.ServiceScore;
            entity.PriceScore = request.PriceScore;
            entity.Conclusion = request.Conclusion.Trim();
            entity.Notes = (request.Notes ?? string.Empty).Trim();
            entity.AssessedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
            return ToRecord(entity);
        }

        public async Task<bool> DeleteAsync(
            int supplierCompanyId, int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await CanAccessSupplierAsync(context, supplierCompanyId, cancellationToken)) return false;
            var entity = await context.SupplierAssessments.FirstOrDefaultAsync(
                item => item.Id == id && item.SupplierCompanyId == supplierCompanyId,
                cancellationToken);
            if (entity == null) return false;
            context.SupplierAssessments.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private Task<bool> CanAccessSupplierAsync(
            AppDbContext context, int supplierCompanyId, CancellationToken cancellationToken) =>
            _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .AnyAsync(item => item.Id == supplierCompanyId, cancellationToken);

        private static void Validate(SupplierAssessmentSaveRequest request)
        {
            if (request.SupplierCompanyId <= 0) throw new ArgumentException("请选择供应商。");
            if (request.AssessedAt == default) throw new ArgumentException("请选择评价日期。");
            if (request.AssessedAt > DateTimeOffset.UtcNow.AddDays(1)) throw new ArgumentException("评价日期不能晚于今天。");
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
            item.Id, item.SupplierCompanyId, item.AssessedAt, item.AssessmentKind,
            item.QualityScore, item.DeliveryScore, item.ServiceScore, item.PriceScore,
            Math.Round((item.QualityScore + item.DeliveryScore + item.ServiceScore + item.PriceScore) / 4m, 2),
            item.Conclusion, item.Notes, item.AssessedBy, item.CreatedAt, item.UpdatedAt);

        private static decimal Average(int quality, int delivery, int service, int price) =>
            Math.Round((quality + delivery + service + price) / 4m, 2);

        private static decimal Mean(IEnumerable<int> values)
        {
            int[] rows = values.ToArray();
            return rows.Length == 0 ? 0m : Math.Round((decimal)rows.Average(), 2);
        }

        private static int ConclusionPriority(string conclusion) => conclusion switch
        {
            "暂停合作" => 0,
            "观察" => 1,
            "合格" => 2,
            "优先合作" => 3,
            _ => 4
        };
    }
}
