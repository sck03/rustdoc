using ExportDocManager.Models;

namespace ExportDocManager.Services.Worklist;

public enum WorklistDueFilter { All, Overdue, Upcoming, Undated }
public sealed record WorklistQuery(string? Source = null, WorklistDueFilter Due = WorklistDueFilter.All, int PageNumber = 1, int PageSize = 20);
public sealed record WorklistItem(string Source, int RecordId, int? ParentId, string Title, string Description,
    DateOnly? DueDate, DateTimeOffset? DueAt, bool IsOverdue);
public sealed record WorklistSourceCount(string Key, string Name, int Count);
public sealed record WorklistPage(PagedResult<WorklistItem> Page, IReadOnlyList<WorklistSourceCount> Sources,
    DateOnly BusinessDate, DateTimeOffset AsOf);

public interface IWorklistService
{
    Task<WorklistPage> QueryAsync(WorklistQuery query, CancellationToken cancellationToken = default);
}
