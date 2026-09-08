using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Worklist;

public sealed class WorklistService(IDbContextFactory<AppDbContext> factory, BusinessDataAccessScope access,
    IBusinessClock clock, IEnumerable<IWorklistSource> sources) : IWorklistService
{
    public async Task<WorklistPage> QueryAsync(WorklistQuery query, CancellationToken cancellationToken = default)
    {
        if (query.PageNumber is < 1 or > 1000000 || query.PageSize is < 1 or > 100 || !Enum.IsDefined(query.Due))
            throw new ServiceValidationException("待办筛选、页码或每页数量无效。");
        var actor = access.CurrentUser;
        if (actor is not { Id: > 0, IsActive: true }) throw new PermissionDeniedException("请先登录。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                // Counts and rows share one database snapshot. Paging across source
                // groups reads at most one page of rows, even at a large page offset.
                await using var transaction = await db.Database.BeginTransactionAsync(
                    db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, token);
                var businessNow = clock.Now;
                var today = DateOnly.FromDateTime(businessNow.DateTime);
                var now = businessNow.ToUniversalTime();
                var groups = sources.SelectMany(source => source.Build(db, actor)).ToArray();
                string selected = query.Source?.Trim() ?? string.Empty;
                if (selected.Length > 0 && !groups.Any(group => group.Key == selected))
                    throw new ServiceValidationException("该待办来源不存在或当前账号无权查看。");
                var counts = new List<WorklistSourceCount>(groups.Length);
                var items = new List<WorklistItem>(query.PageSize);
                int offset = (query.PageNumber - 1) * query.PageSize;
                int total = 0;
                foreach (var group in groups)
                {
                    var rows = Filter(group, query.Due, today, now);
                    int count = await rows.CountAsync(token);
                    counts.Add(new WorklistSourceCount(group.Key, group.Name, count));
                    if (selected.Length > 0 && selected != group.Key) continue;
                    total = checked(total + count);
                    if (offset >= count) { offset -= count; continue; }
                    if (items.Count == query.PageSize) continue;
                    var page = await Order(rows, group.Deadline).Skip(offset).Take(query.PageSize - items.Count).ToListAsync(token);
                    items.AddRange(page.Select(row => new WorklistItem(group.Key, row.RecordId, row.ParentId, row.Title,
                        row.Description, row.DueDate, row.DueAt, row.DueDate < today || row.DueAt < now)));
                    offset = 0;
                }
                await transaction.CommitAsync(token);
                return new WorklistPage(new PagedResult<WorklistItem>(items, total, query.PageNumber, query.PageSize), counts, today, now);
            }, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new ServiceTimeoutException("待办查询超时，请稍后刷新。", ex); }
    }

    private static IOrderedQueryable<WorklistRow> Order(IQueryable<WorklistRow> rows, WorklistDeadlineKind deadline) => deadline switch
    {
        WorklistDeadlineKind.Date => rows.OrderBy(row => row.DueDate == null).ThenBy(row => row.DueDate).ThenBy(row => row.RecordId),
        WorklistDeadlineKind.Instant => rows.OrderBy(row => row.DueAt == null).ThenBy(row => row.DueAt).ThenBy(row => row.RecordId),
        _ => rows.OrderBy(row => row.RecordId)
    };

    private static IQueryable<WorklistRow> Filter(WorklistQueryGroup group, WorklistDueFilter due, DateOnly today, DateTimeOffset now)
    {
        var rows = group.Rows;
        if (due == WorklistDueFilter.All) return rows;
        if (group.Deadline == WorklistDeadlineKind.None)
            return due == WorklistDueFilter.Undated ? rows : rows.Where(_ => false);
        DateOnly soon = today.AddDays(30);
        DateTimeOffset soonAt = now.AddDays(30);
        return (group.Deadline, due) switch
        {
            (WorklistDeadlineKind.Date, WorklistDueFilter.Overdue) => rows.Where(row => row.DueDate < today),
            (WorklistDeadlineKind.Date, WorklistDueFilter.Upcoming) => rows.Where(row => row.DueDate >= today && row.DueDate <= soon),
            (WorklistDeadlineKind.Date, WorklistDueFilter.Undated) => rows.Where(row => row.DueDate == null),
            (WorklistDeadlineKind.Instant, WorklistDueFilter.Overdue) => rows.Where(row => row.DueAt < now),
            (WorklistDeadlineKind.Instant, WorklistDueFilter.Upcoming) => rows.Where(row => row.DueAt >= now && row.DueAt <= soonAt),
            (WorklistDeadlineKind.Instant, WorklistDueFilter.Undated) => rows.Where(row => row.DueAt == null),
            _ => rows
        };
    }
}

public interface IWorklistSource
{
    IEnumerable<WorklistQueryGroup> Build(AppDbContext db, User actor);
}

public enum WorklistDeadlineKind { None, Date, Instant }
public sealed record WorklistQueryGroup(string Key, string Name, IQueryable<WorklistRow> Rows, WorklistDeadlineKind Deadline = WorklistDeadlineKind.None);
public sealed class WorklistRow
{
    public int RecordId { get; init; }
    public int? ParentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public required DateOnly? DueDate { get; init; }
    public required DateTimeOffset? DueAt { get; init; }
}
