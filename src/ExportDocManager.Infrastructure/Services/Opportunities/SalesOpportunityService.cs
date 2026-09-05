using System.Data;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Opportunities;

public sealed class SalesOpportunityService : ISalesOpportunityService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly BusinessDataAccessScope _accessScope;
    private readonly IBusinessClock _clock;

    public SalesOpportunityService(
        IDbContextFactory<AppDbContext> contextFactory,
        BusinessDataAccessScope accessScope,
        IBusinessClock? clock = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        _clock = clock ?? BusinessClock.CreateSystem();
    }

    public async Task<PagedResult<SalesOpportunityRecord>> QueryAsync(
        string? keyword,
        string? stage,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        keyword = Clean(keyword);
        stage = Clean(stage);
        if (stage.Length > 0) stage = NormalizeStage(stage);
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        _accessScope.DemandPermission(
            PermissionModuleCatalog.CommonProductReference,
            PermissionAction.View);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var opportunities = _accessScope.ApplySalesOpportunityScope(
            context.SalesOpportunities.AsNoTracking());
        var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
        var query = from opportunity in opportunities
                    join customer in customers on opportunity.CrmCustomerId equals customer.Id
                    join product in context.Products.AsNoTracking()
                        on opportunity.ProductId equals product.Id into products
                    from product in products.DefaultIfEmpty()
                    select new { Opportunity = opportunity, Customer = customer, Product = product };

        if (keyword.Length > 0)
        {
            query = query.Where(item =>
                item.Opportunity.Title.Contains(keyword) ||
                item.Opportunity.QuotationNo.Contains(keyword) ||
                item.Opportunity.NextAction.Contains(keyword) ||
                item.Customer.Name.Contains(keyword) ||
                item.Product != null &&
                ((item.Product.ProductCode ?? string.Empty).Contains(keyword) ||
                 (item.Product.NameCN ?? string.Empty).Contains(keyword) ||
                 (item.Product.NameEN ?? string.Empty).Contains(keyword)));
        }

        if (stage.Length > 0)
        {
            query = query.Where(item => item.Opportunity.Stage == stage);
        }

        int total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(item => item.Opportunity.Stage == SalesOpportunityStageCatalog.Won ||
                             item.Opportunity.Stage == SalesOpportunityStageCatalog.Lost)
            .ThenByDescending(item => item.Opportunity.UpdatedAt)
            .ThenByDescending(item => item.Opportunity.Id)
            .Skip(PagingHelper.CalculateOffset(pageNumber, pageSize))
            .Take(pageSize)
            .Select(item => new SalesOpportunityRecord(
                item.Opportunity.Id,
                item.Opportunity.CrmCustomerId,
                item.Customer.Name,
                item.Opportunity.ProductId,
                item.Product != null ? item.Product.ProductCode ?? string.Empty : string.Empty,
                item.Product != null ? item.Product.NameCN ?? item.Product.NameEN ?? string.Empty : string.Empty,
                item.Opportunity.Title,
                item.Opportunity.Stage,
                item.Opportunity.QuotationNo,
                item.Opportunity.EstimatedAmount,
                item.Opportunity.Currency,
                item.Opportunity.ProbabilityPercent,
                item.Opportunity.ExpectedCloseDate,
                item.Opportunity.NextAction,
                item.Opportunity.Notes,
                item.Opportunity.VersionNumber))
            .ToListAsync(cancellationToken);

        return new PagedResult<SalesOpportunityRecord>(rows, total, pageNumber, pageSize);
    }

    public Task<SalesOpportunityRecord> SaveAsync(
        SalesOpportunitySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string title = Required(request.Title, "商机名称");
        if (title.Length > 200) throw new ArgumentException("商机名称不能超过 200 个字符。");
        if (request.EstimatedAmount < 0) throw new ArgumentException("预计金额不能小于零。");
        if (request.ProbabilityPercent is < 0 or > 100)
            throw new ArgumentException("成交概率必须在 0 至 100 之间。");
        string currency = CurrencyCodeCatalog.Normalize(request.Currency);
        string quotationNo = Clean(request.QuotationNo);
        if (quotationNo.Length > 100) throw new ArgumentException("报价跟踪编号不能超过 100 个字符。");
        string changeNote = Clean(request.ChangeNote);
        if (changeNote.Length > 1000) throw new ArgumentException("变更备注不能超过 1000 个字符。");
        string nextAction = Clean(request.NextAction);
        if (nextAction.Length > 500) throw new ArgumentException("下一步动作不能超过 500 个字符。");
        string notes = Clean(request.Notes);
        if (notes.Length > 2000) throw new ArgumentException("商机备注不能超过 2000 个字符。");

        return AppDbContextExecution.ExecuteInTransactionAsync(
            _contextFactory,
            async (context, token) =>
            {
                bool isNew = request.Id <= 0;
                string writeAction = isNew ? PermissionAction.Create : PermissionAction.Edit;
                var customer = await _accessScope.ApplyCrmCustomerScopeForPermission(
                        context.CrmCustomers.AsNoTracking(),
                        PermissionResourceCatalog.SalesOpportunities,
                        writeAction)
                    .FirstOrDefaultAsync(item => item.Id == request.CrmCustomerId, token)
                    ?? throw new ResourceNotFoundException("CRM 客户不存在或无权访问。");

                Product? product = null;
                if (request.ProductId is > 0)
                {
                    _accessScope.DemandPermission(
                        PermissionModuleCatalog.CommonProductReference,
                        PermissionAction.View);
                    product = await context.Products.AsNoTracking()
                        .FirstOrDefaultAsync(item => item.Id == request.ProductId, token)
                        ?? throw new ResourceNotFoundException("产品不存在。");
                }

                SalesOpportunity entity;
                if (isNew)
                {
                    entity = new SalesOpportunity
                    {
                        Stage = SalesOpportunityStageCatalog.Lead,
                        VersionNumber = 1,
                        CreatedAt = _clock.UtcNow,
                        UpdatedAt = _clock.UtcNow
                    };
                    _accessScope.ApplyOwner(entity);
                    await context.SalesOpportunities.AddAsync(entity, token);
                }
                else
                {
                    EnsureExpectedVersion(request.ExpectedVersion, "商机");
                    entity = await _accessScope.ApplySalesOpportunityScope(
                            context.SalesOpportunities,
                            action: PermissionAction.Edit)
                        .FirstOrDefaultAsync(item => item.Id == request.Id, token)
                        ?? throw new ResourceNotFoundException("商机不存在、已归档或无权访问。");
                    EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "商机");
                    if (SalesOpportunityStageCatalog.IsClosed(entity.Stage))
                    {
                        throw new ResourceConflictException("已成交或已失单商机不可直接编辑；请先按阶段流转规则重新打开。");
                    }

                    context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                    entity.VersionNumber++;
                }

                string previousQuotationNo = entity.QuotationNo;
                decimal previousAmount = entity.EstimatedAmount;
                string previousCurrency = entity.Currency;
                int previousProbability = entity.ProbabilityPercent;
                DateOnly? previousExpectedCloseDate = entity.ExpectedCloseDate;
                int? previousCustomerId = entity.CrmCustomerId;
                int? previousProductId = entity.ProductId;
                string previousTitle = entity.Title;
                string previousNextAction = entity.NextAction;
                string previousNotes = entity.Notes;

                string? normalizedQuotationNo = NormalizeQuotationNo(quotationNo);
                if (normalizedQuotationNo is not null &&
                    await context.SalesOpportunities.AsNoTracking().AnyAsync(
                        item => item.Id != entity.Id && item.QuotationNoNormalized == normalizedQuotationNo,
                        token))
                {
                    throw new ResourceConflictException("报价跟踪编号已存在；报价编号在所有公司范围内必须唯一。");
                }

                DateTimeOffset now = _clock.UtcNow;
                entity.CrmCustomerId = request.CrmCustomerId;
                entity.ProductId = request.ProductId is > 0 ? request.ProductId : null;
                entity.Title = title;
                entity.QuotationNo = quotationNo;
                entity.QuotationNoNormalized = normalizedQuotationNo;
                entity.EstimatedAmount = request.EstimatedAmount;
                entity.Currency = currency;
                entity.ProbabilityPercent = request.ProbabilityPercent;
                entity.ExpectedCloseDate = request.ExpectedCloseDate;
                entity.NextAction = nextAction;
                entity.Notes = notes;
                entity.IsDeleted = false;
                entity.UpdatedAt = now;

                bool quoteChanged = !string.Equals(previousQuotationNo, quotationNo, StringComparison.Ordinal) ||
                    previousAmount != request.EstimatedAmount ||
                    !string.Equals(previousCurrency, currency, StringComparison.Ordinal) ||
                    previousProbability != request.ProbabilityPercent ||
                    previousExpectedCloseDate != request.ExpectedCloseDate;
                bool detailsChanged = previousCustomerId != request.CrmCustomerId ||
                    previousProductId != entity.ProductId ||
                    !string.Equals(previousTitle, title, StringComparison.Ordinal) ||
                    !string.Equals(previousNextAction, nextAction, StringComparison.Ordinal) ||
                    !string.Equals(previousNotes, notes, StringComparison.Ordinal);
                if (isNew && (quotationNo.Length > 0 || request.EstimatedAmount != 0 ||
                              request.ProbabilityPercent != 0 || request.ExpectedCloseDate.HasValue))
                {
                    _accessScope.DemandPermission(
                        PermissionResourceCatalog.SalesQuotes,
                        PermissionAction.Create);
                }
                else if (!isNew && quoteChanged)
                {
                    _accessScope.DemandPermission(
                        PermissionResourceCatalog.SalesQuotes,
                        PermissionAction.Edit);
                    bool canEditQuote = await _accessScope.ApplySalesOpportunityScopeForPermission(
                            context.SalesOpportunities.AsNoTracking(),
                            PermissionResourceCatalog.SalesQuotes,
                            PermissionAction.Edit)
                        .AnyAsync(item => item.Id == entity.Id, token);
                    if (!canEditQuote)
                    {
                        throw new PermissionDeniedException("当前账号不能修改该商机的报价信息。");
                    }
                }
                string changeType = isNew
                    ? "创建"
                    : quoteChanged
                        ? "报价更新"
                        : changeNote.Length > 0
                            ? "进展备注"
                            : detailsChanged ? "资料更新" : string.Empty;
                if (changeType.Length > 0)
                {
                    await context.SalesOpportunityHistories.AddAsync(new SalesOpportunityHistory
                    {
                        SalesOpportunityId = entity.Id,
                        Opportunity = isNew ? entity : null,
                        VersionNumber = entity.VersionNumber,
                        ChangeType = changeType,
                        Stage = entity.Stage,
                        QuotationNo = quotationNo,
                        EstimatedAmount = request.EstimatedAmount,
                        Currency = currency,
                        ProbabilityPercent = request.ProbabilityPercent,
                        ExpectedCloseDate = request.ExpectedCloseDate,
                        ChangeNote = changeNote,
                        ChangedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty,
                        CreatedAt = now
                    }, token);
                }

                try
                {
                    await context.SaveChangesAsync(token);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。", exception);
                }
                catch (DbUpdateException exception) when (
                    normalizedQuotationNo is not null &&
                    RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
                {
                    throw new ResourceConflictException("报价跟踪编号已存在；报价编号在所有公司范围内必须唯一。", exception);
                }

                return ToRecord(entity, customer.Name, product);
            },
            IsolationLevel.Serializable,
            cancellationToken);
    }

    public Task<SalesOpportunityRecord> TransitionAsync(
        int id,
        SalesOpportunityTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string nextStage = NormalizeStage(request.NextStage);
        string changeNote = Clean(request.ChangeNote);
        if (changeNote.Length > 1000) throw new ArgumentException("阶段流转说明不能超过 1000 个字符。");
        return AppDbContextExecution.ExecuteInTransactionAsync(
            _contextFactory,
            async (context, token) =>
            {
                EnsureExpectedVersion(request.ExpectedVersion, "商机");
                var entity = await _accessScope.ApplySalesOpportunityScope(
                        context.SalesOpportunities,
                        action: PermissionAction.Transition)
                    .FirstOrDefaultAsync(item => item.Id == id, token)
                    ?? throw new ResourceNotFoundException("商机不存在、已归档或无权流转。");
                EnsureExpectedVersion(request.ExpectedVersion, entity.VersionNumber, "商机");
                if (string.Equals(entity.Stage, nextStage, StringComparison.Ordinal))
                {
                    throw new ResourceConflictException("商机已经处于目标阶段。");
                }
                if (!SalesOpportunityStageCatalog.CanTransition(entity.Stage, nextStage))
                {
                    throw new ArgumentException(
                        $"商机阶段不能从“{entity.Stage}”直接变更为“{nextStage}”，请按销售流程逐步推进或退回。");
                }

                var customer = await _accessScope.ApplyCrmCustomerScopeForPermission(
                        context.CrmCustomers.AsNoTracking(),
                        PermissionResourceCatalog.SalesOpportunities,
                        PermissionAction.Transition)
                    .FirstOrDefaultAsync(item => item.Id == entity.CrmCustomerId, token)
                    ?? throw new ResourceNotFoundException("关联客户不存在或无权访问。");
                Product? product = entity.ProductId is > 0
                    ? await LoadProductAsync(context, entity.ProductId.Value, token)
                    : null;
                string previousStage = entity.Stage;
                DateTimeOffset now = _clock.UtcNow;
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                entity.Stage = nextStage;
                entity.VersionNumber++;
                entity.UpdatedAt = now;
                await context.SalesOpportunityHistories.AddAsync(new SalesOpportunityHistory
                {
                    SalesOpportunityId = entity.Id,
                    VersionNumber = entity.VersionNumber,
                    ChangeType = "阶段变更",
                    Stage = entity.Stage,
                    QuotationNo = entity.QuotationNo,
                    EstimatedAmount = entity.EstimatedAmount,
                    Currency = entity.Currency,
                    ProbabilityPercent = entity.ProbabilityPercent,
                    ExpectedCloseDate = entity.ExpectedCloseDate,
                    ChangeNote = changeNote.Length > 0
                        ? changeNote
                        : $"阶段从“{previousStage}”流转到“{nextStage}”。",
                    ChangedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty,
                    CreatedAt = now
                }, token);

                try
                {
                    await context.SaveChangesAsync(token);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。", exception);
                }
                return ToRecord(entity, customer.Name, product);
            },
            IsolationLevel.Serializable,
            cancellationToken);
    }

    public Task<bool> ArchiveAsync(
        int id,
        CancellationToken cancellationToken = default,
        int expectedVersion = 0) =>
        AppDbContextExecution.ExecuteInTransactionAsync(
            _contextFactory,
            async (context, token) =>
            {
                var entity = await _accessScope.ApplySalesOpportunityScope(
                        context.SalesOpportunities,
                        action: PermissionAction.Archive)
                    .FirstOrDefaultAsync(item => item.Id == id, token);
                if (entity == null) return false;
                EnsureExpectedVersion(expectedVersion, entity.VersionNumber, "商机");

                DateTimeOffset now = _clock.UtcNow;
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                entity.IsDeleted = true;
                entity.VersionNumber++;
                entity.UpdatedAt = now;
                await context.SalesOpportunityHistories.AddAsync(new SalesOpportunityHistory
                {
                    SalesOpportunityId = entity.Id,
                    VersionNumber = entity.VersionNumber,
                    ChangeType = "归档",
                    Stage = entity.Stage,
                    QuotationNo = entity.QuotationNo,
                    EstimatedAmount = entity.EstimatedAmount,
                    Currency = entity.Currency,
                    ProbabilityPercent = entity.ProbabilityPercent,
                    ExpectedCloseDate = entity.ExpectedCloseDate,
                    ChangeNote = "商机已归档，历史版本保留。",
                    ChangedBy = _accessScope.CurrentUser?.Username?.Trim() ?? string.Empty,
                    CreatedAt = now
                }, token);

                try
                {
                    await context.SaveChangesAsync(token);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    throw new BusinessConcurrencyException("该商机已被其他用户修改，请刷新后重试。", exception);
                }

                return true;
            },
            IsolationLevel.Serializable,
            cancellationToken);

    public async Task<IReadOnlyList<SalesOpportunityHistoryRecord>> ListHistoryAsync(
        int opportunityId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var visible = _accessScope.ApplySalesOpportunityScope(
                context.SalesOpportunities.AsNoTracking(),
                includeDeleted: true)
            .Where(item => item.Id == opportunityId);
        if (!await visible.AnyAsync(cancellationToken)) return [];

        return await context.SalesOpportunityHistories.AsNoTracking()
            .Where(item => item.SalesOpportunityId == opportunityId)
            .OrderByDescending(item => item.VersionNumber)
            .ThenByDescending(item => item.Id)
            .Select(item => new SalesOpportunityHistoryRecord(
                item.Id,
                item.SalesOpportunityId,
                item.VersionNumber,
                item.ChangeType,
                item.Stage,
                item.QuotationNo,
                item.EstimatedAmount,
                item.Currency,
                item.ProbabilityPercent,
                item.ExpectedCloseDate,
                item.ChangeNote,
                item.ChangedBy,
                item.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesOpportunityDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        _accessScope.DemandPermission(
            PermissionModuleCatalog.CommonProductReference,
            PermissionAction.View);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var scoped = _accessScope.ApplySalesOpportunityScope(
            context.SalesOpportunities.AsNoTracking());
        var stageCounts = await scoped
            .GroupBy(item => item.Stage)
            .Select(group => new SalesOpportunityStageSummary(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        var stages = SalesOpportunityStageCatalog.Values
            .Select(value => new SalesOpportunityStageSummary(
                value,
                stageCounts.FirstOrDefault(item => item.Stage == value)?.Count ?? 0))
            .ToArray();

        var active = scoped.Where(item => item.Stage != SalesOpportunityStageCatalog.Won &&
                                          item.Stage != SalesOpportunityStageCatalog.Lost);
        SalesOpportunityCurrencySummary[] currencies;
        if (_accessScope.UsesPostgreSql)
        {
            currencies = await active
                .GroupBy(item => item.Currency)
                .Select(group => new SalesOpportunityCurrencySummary(
                    group.Key,
                    group.Count(),
                    group.Sum(item => item.EstimatedAmount),
                    group.Sum(item => item.EstimatedAmount * item.ProbabilityPercent / 100m)))
                .OrderBy(item => item.Currency)
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            // SQLite is the bounded single-user desktop mode. Its provider
            // cannot aggregate decimal values without lossy conversion, so
            // keep exact arithmetic locally while PostgreSQL performs the
            // scalable team-mode aggregation in the database.
            var currencyTotals = new Dictionary<string, (int Count, decimal EstimatedAmount, decimal WeightedAmount)>(
                StringComparer.OrdinalIgnoreCase);
            await foreach (var row in active
                               .Select(item => new { item.Currency, item.EstimatedAmount, item.ProbabilityPercent })
                               .AsAsyncEnumerable()
                               .WithCancellation(cancellationToken))
            {
                string normalizedCurrency = Clean(row.Currency).ToUpperInvariant();
                currencyTotals.TryGetValue(normalizedCurrency, out var total);
                total.Count++;
                total.EstimatedAmount += row.EstimatedAmount;
                total.WeightedAmount += row.EstimatedAmount * row.ProbabilityPercent / 100m;
                currencyTotals[normalizedCurrency] = total;
            }

            currencies = currencyTotals
                .Select(item => new SalesOpportunityCurrencySummary(
                    item.Key,
                    item.Value.Count,
                    item.Value.EstimatedAmount,
                    item.Value.WeightedAmount))
                .OrderBy(item => item.Currency)
                .ToArray();
        }

        DateOnly today = _clock.Today;
        var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
        var upcoming = await (from opportunity in active
                              where opportunity.ExpectedCloseDate.HasValue &&
                                    opportunity.ExpectedCloseDate.Value >= today &&
                                    opportunity.ExpectedCloseDate.Value <= today.AddDays(30)
                              join customer in customers on opportunity.CrmCustomerId equals customer.Id
                              join product in context.Products.AsNoTracking()
                                  on opportunity.ProductId equals product.Id into products
                              from product in products.DefaultIfEmpty()
                              orderby opportunity.ExpectedCloseDate,
                                  opportunity.ProbabilityPercent descending,
                                  opportunity.Id descending
                              select new SalesOpportunityRecord(
                                  opportunity.Id,
                                  opportunity.CrmCustomerId,
                                  customer.Name,
                                  opportunity.ProductId,
                                  product != null ? product.ProductCode ?? string.Empty : string.Empty,
                                  product != null ? product.NameCN ?? product.NameEN ?? string.Empty : string.Empty,
                                  opportunity.Title,
                                  opportunity.Stage,
                                  opportunity.QuotationNo,
                                  opportunity.EstimatedAmount,
                                  opportunity.Currency,
                                  opportunity.ProbabilityPercent,
                                  opportunity.ExpectedCloseDate,
                                  opportunity.NextAction,
                                  opportunity.Notes,
                                  opportunity.VersionNumber))
            .Take(8)
            .ToListAsync(cancellationToken);

        return new SalesOpportunityDashboard(stages, currencies, upcoming);
    }

    private async Task<Product> LoadProductAsync(
        AppDbContext context,
        int productId,
        CancellationToken cancellationToken)
    {
        _accessScope.DemandPermission(
            PermissionModuleCatalog.CommonProductReference,
            PermissionAction.View);
        return await context.Products.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken)
            ?? throw new ResourceNotFoundException("关联产品不存在。");
    }

    private static SalesOpportunityRecord ToRecord(
        SalesOpportunity opportunity,
        string customerName,
        Product? product) =>
        new(
            opportunity.Id,
            opportunity.CrmCustomerId,
            customerName,
            opportunity.ProductId,
            product?.ProductCode ?? string.Empty,
            product?.NameCN ?? product?.NameEN ?? string.Empty,
            opportunity.Title,
            opportunity.Stage,
            opportunity.QuotationNo,
            opportunity.EstimatedAmount,
            opportunity.Currency,
            opportunity.ProbabilityPercent,
            opportunity.ExpectedCloseDate,
            opportunity.NextAction,
            opportunity.Notes,
            opportunity.VersionNumber);

    private static string NormalizeStage(string? stage) =>
        SalesOpportunityStageCatalog.Normalize(stage);

    private static string? NormalizeQuotationNo(string value)
    {
        string normalized = Clean(value).Normalize(NormalizationForm.FormC).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field}不能为空。")
            : value.Trim().Normalize(NormalizationForm.FormC);

    private static string Clean(string? value) =>
        (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);

    private static void EnsureExpectedVersion(int expectedVersion, string entityName)
    {
        if (expectedVersion <= 0)
            throw new BusinessConcurrencyException($"操作现有{entityName}时必须提供版本号，请刷新后重试。");
    }

    private static void EnsureExpectedVersion(int expectedVersion, int currentVersion, string entityName)
    {
        EnsureExpectedVersion(expectedVersion, entityName);
        if (expectedVersion != currentVersion)
            throw new BusinessConcurrencyException($"该{entityName}已被其他用户修改，请刷新后重试。");
    }
}
