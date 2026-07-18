using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Opportunities
{
    public sealed class SalesOpportunityService : ISalesOpportunityService
    {
        private static readonly string[] AllowedStages = ["线索", "需求确认", "已报价", "谈判中", "已成交", "已失单"];
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public SalesOpportunityService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<PagedResult<SalesOpportunityRecord>> QueryAsync(
            string keyword, string stage, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword); stage = Clean(stage);
            pageNumber = Math.Max(pageNumber, 1); pageSize = Math.Clamp(pageSize, 10, 100);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var opportunities = _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities.AsNoTracking());
            var query = from opportunity in opportunities
                join customer in context.CrmCustomers.AsNoTracking() on opportunity.CrmCustomerId equals customer.Id
                join product in context.Products.AsNoTracking() on opportunity.ProductId equals product.Id into products
                from product in products.DefaultIfEmpty()
                select new { Opportunity = opportunity, Customer = customer, Product = product };
            if (keyword.Length > 0)
                query = query.Where(item => item.Opportunity.Title.Contains(keyword) || item.Opportunity.QuotationNo.Contains(keyword) ||
                    item.Opportunity.NextAction.Contains(keyword) || item.Customer.Name.Contains(keyword) ||
                    (item.Product != null && ((item.Product.ProductCode ?? string.Empty).Contains(keyword) || (item.Product.NameCN ?? string.Empty).Contains(keyword))));
            if (stage.Length > 0) query = query.Where(item => item.Opportunity.Stage == stage);
            int total = await query.CountAsync(cancellationToken);
            var rows = await query.OrderBy(item => item.Opportunity.Stage == "已成交" || item.Opportunity.Stage == "已失单")
                .ThenByDescending(item => item.Opportunity.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .Select(item => new SalesOpportunityRecord(item.Opportunity.Id, item.Opportunity.CrmCustomerId,
                    item.Customer.Name, item.Opportunity.ProductId, item.Product != null ? item.Product.ProductCode ?? string.Empty : string.Empty,
                    item.Product != null ? (item.Product.NameCN ?? item.Product.NameEN ?? string.Empty) : string.Empty,
                    item.Opportunity.Title, item.Opportunity.Stage, item.Opportunity.QuotationNo,
                    item.Opportunity.EstimatedAmount, item.Opportunity.Currency, item.Opportunity.ProbabilityPercent,
                    item.Opportunity.ExpectedCloseAt, item.Opportunity.NextAction, item.Opportunity.Notes,
                    item.Opportunity.VersionNumber))
                .ToListAsync(cancellationToken);
            return new PagedResult<SalesOpportunityRecord>(rows, total, pageNumber, pageSize);
        }

        public async Task<SalesOpportunityRecord> SaveAsync(
            SalesOpportunitySaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string title = Required(request.Title, "商机名称");
            string stage = Clean(request.Stage);
            if (!AllowedStages.Contains(stage)) throw new ArgumentException("商机阶段无效。");
            if (request.EstimatedAmount < 0) throw new ArgumentException("预计金额不能小于零。");
            if (request.ProbabilityPercent is < 0 or > 100) throw new ArgumentException("成交概率必须在 0 至 100 之间。");
            string currency = Clean(request.Currency).ToUpperInvariant();
            if (currency.Length != 3) throw new ArgumentException("币种必须使用三位代码，例如 USD、CNY。");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customer = await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == request.CrmCustomerId, cancellationToken)
                ?? throw new KeyNotFoundException("CRM 客户不存在或无权访问。");
            Product product = null;
            if (request.ProductId is > 0)
                product = await context.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.ProductId, cancellationToken)
                    ?? throw new KeyNotFoundException("产品不存在。");
            string quotationNo = Clean(request.QuotationNo);
            string changeNote = Clean(request.ChangeNote);
            if (changeNote.Length > 1000) throw new ArgumentException("变更备注不能超过 1000 个字符。");
            if (quotationNo.Length > 0 && await _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities.AsNoTracking())
                    .AnyAsync(item => item.Id != request.Id && item.QuotationNo == quotationNo, cancellationToken))
                throw new ArgumentException("报价跟踪编号已存在。");

            SalesOpportunity entity;
            bool isNew = request.Id <= 0;
            if (request.Id > 0)
            {
                if (request.ExpectedVersion <= 0)
                    throw new BusinessConcurrencyException("保存现有商机时必须提供版本号，请刷新后重试。");
                entity = await _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("商机不存在或无权访问。");
                if (entity.VersionNumber != request.ExpectedVersion)
                    throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.VersionNumber++;
            }
            else
            {
                entity = new SalesOpportunity { VersionNumber = 1 }; _accessScope.ApplyOwner(entity);
                await context.SalesOpportunities.AddAsync(entity, cancellationToken);
            }
            string previousStage = entity.Stage;
            string previousQuotationNo = entity.QuotationNo;
            decimal previousAmount = entity.EstimatedAmount;
            string previousCurrency = entity.Currency;
            int previousProbability = entity.ProbabilityPercent;
            DateTimeOffset? previousExpectedCloseAt = entity.ExpectedCloseAt;
            entity.CrmCustomerId = request.CrmCustomerId; entity.ProductId = request.ProductId is > 0 ? request.ProductId : null;
            entity.Title = title; entity.Stage = stage; entity.QuotationNo = quotationNo;
            entity.EstimatedAmount = request.EstimatedAmount; entity.Currency = currency;
            entity.ProbabilityPercent = request.ProbabilityPercent; entity.ExpectedCloseAt = request.ExpectedCloseAt;
            entity.NextAction = Clean(request.NextAction); entity.Notes = Clean(request.Notes); entity.UpdatedAt = DateTimeOffset.UtcNow;

            bool stageChanged = !string.Equals(previousStage, stage, StringComparison.Ordinal);
            bool quotationChanged = !string.Equals(previousQuotationNo, quotationNo, StringComparison.Ordinal) ||
                previousAmount != request.EstimatedAmount || !string.Equals(previousCurrency, currency, StringComparison.Ordinal) ||
                previousProbability != request.ProbabilityPercent || previousExpectedCloseAt != request.ExpectedCloseAt;
            string changeType = isNew ? "创建" : stageChanged && quotationChanged ? "阶段与报价更新" :
                stageChanged ? "阶段变更" : quotationChanged ? "报价更新" : changeNote.Length > 0 ? "进展备注" : string.Empty;
            if (changeType.Length > 0)
            {
                int version = isNew ? 1 : (await context.SalesOpportunityHistories
                    .Where(item => item.SalesOpportunityId == entity.Id).MaxAsync(item => (int?)item.VersionNumber, cancellationToken) ?? 0) + 1;
                var history = new SalesOpportunityHistory
                {
                    SalesOpportunityId = entity.Id, Opportunity = isNew ? entity : null, VersionNumber = version,
                    ChangeType = changeType, Stage = stage, QuotationNo = quotationNo,
                    EstimatedAmount = request.EstimatedAmount, Currency = currency,
                    ProbabilityPercent = request.ProbabilityPercent, ExpectedCloseAt = request.ExpectedCloseAt,
                    ChangeNote = changeNote, ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty
                };
                await context.SalesOpportunityHistories.AddAsync(history, cancellationToken);
            }
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。", exception);
            }
            return new(entity.Id, entity.CrmCustomerId, customer.Name, entity.ProductId, product?.ProductCode ?? string.Empty,
                product?.NameCN ?? product?.NameEN ?? string.Empty, entity.Title, entity.Stage, entity.QuotationNo,
                entity.EstimatedAmount, entity.Currency, entity.ProbabilityPercent, entity.ExpectedCloseAt,
                entity.NextAction, entity.Notes, entity.VersionNumber);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null) return false;
            context.SalesOpportunities.Remove(entity);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。", exception);
            }
            return true;
        }

        public async Task<IReadOnlyList<SalesOpportunityHistoryRecord>> ListHistoryAsync(
            int opportunityId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities.AsNoTracking())
                    .AnyAsync(item => item.Id == opportunityId, cancellationToken)) return [];
            return await context.SalesOpportunityHistories.AsNoTracking()
                .Where(item => item.SalesOpportunityId == opportunityId)
                .OrderByDescending(item => item.VersionNumber)
                .Select(item => new SalesOpportunityHistoryRecord(item.Id, item.SalesOpportunityId,
                    item.VersionNumber, item.ChangeType, item.Stage, item.QuotationNo, item.EstimatedAmount,
                    item.Currency, item.ProbabilityPercent, item.ExpectedCloseAt, item.ChangeNote,
                    item.ChangedBy, item.CreatedAt)).ToListAsync(cancellationToken);
        }

        public async Task<SalesOpportunityDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var scoped = _accessScope.ApplySalesOpportunityScope(context.SalesOpportunities.AsNoTracking());
            var stageCounts = await scoped.GroupBy(item => item.Stage)
                .Select(group => new SalesOpportunityStageSummary(group.Key, group.Count()))
                .ToListAsync(cancellationToken);
            var stages = AllowedStages.Select(stage => new SalesOpportunityStageSummary(
                stage, stageCounts.FirstOrDefault(item => item.Stage == stage)?.Count ?? 0)).ToArray();

            var active = scoped.Where(item => item.Stage != "已成交" && item.Stage != "已失单")
                .OrderByDescending(item => item.Id).Take(5000);
            var rows = await (from opportunity in active
                join customer in context.CrmCustomers.AsNoTracking() on opportunity.CrmCustomerId equals customer.Id
                join product in context.Products.AsNoTracking() on opportunity.ProductId equals product.Id into products
                from product in products.DefaultIfEmpty()
                select new SalesOpportunityRecord(opportunity.Id, opportunity.CrmCustomerId, customer.Name,
                    opportunity.ProductId, product != null ? product.ProductCode ?? string.Empty : string.Empty,
                    product != null ? (product.NameCN ?? product.NameEN ?? string.Empty) : string.Empty,
                    opportunity.Title, opportunity.Stage, opportunity.QuotationNo, opportunity.EstimatedAmount,
                    opportunity.Currency, opportunity.ProbabilityPercent, opportunity.ExpectedCloseAt,
                    opportunity.NextAction, opportunity.Notes, opportunity.VersionNumber)).ToListAsync(cancellationToken);
            var currencies = rows.GroupBy(item => item.Currency, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SalesOpportunityCurrencySummary(group.Key.ToUpperInvariant(), group.Count(),
                    group.Sum(item => item.EstimatedAmount),
                    group.Sum(item => item.EstimatedAmount * item.ProbabilityPercent / 100m)))
                .OrderBy(item => item.Currency).ToArray();
            var now = DateTimeOffset.Now;
            var upcoming = rows.Where(item => item.ExpectedCloseAt.HasValue && item.ExpectedCloseAt.Value >= now && item.ExpectedCloseAt.Value <= now.AddDays(30))
                .OrderBy(item => item.ExpectedCloseAt).ThenByDescending(item => item.ProbabilityPercent).Take(8).ToArray();
            return new SalesOpportunityDashboard(stages, currencies, upcoming);
        }

        private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field}不能为空。") : value.Trim();
        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
