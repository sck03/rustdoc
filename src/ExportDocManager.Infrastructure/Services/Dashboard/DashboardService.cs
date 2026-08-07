using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Dashboard;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class DashboardService : IDashboardService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public DashboardService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope businessDataAccessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
        }

        public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var localNow = DateTime.Now;
            var localStartOfMonth = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var localEndOfMonth = localStartOfMonth.AddMonths(1);
            var startOfMonth = TimeZoneInfo.ConvertTimeToUtc(localStartOfMonth, TimeZoneInfo.Local);
            var endOfMonth = TimeZoneInfo.ConvertTimeToUtc(localEndOfMonth, TimeZoneInfo.Local);
            var previousStartOfMonth = TimeZoneInfo.ConvertTimeToUtc(localStartOfMonth.AddMonths(-1), TimeZoneInfo.Local);
            var scopedInvoices = _businessDataAccessScope.ApplyInvoiceScope(context.Invoices.AsNoTracking());
            var activeInvoices = BuildPreferredInvoiceQuery(
                scopedInvoices.Where(invoice => invoice.Status != InvoiceStatusCatalog.Cancelled));

            var periodAggregates = await LoadPeriodAggregatesAsync(
                context,
                activeInvoices,
                previousStartOfMonth,
                startOfMonth,
                endOfMonth,
                cancellationToken);
            DashboardPeriodAggregate currentPeriod = periodAggregates.Current;
            DashboardPeriodAggregate previousPeriod = periodAggregates.Previous;

            var statusCounts = await activeInvoices
                .GroupBy(invoice => invoice.Status ?? string.Empty)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);

            int CountStatus(string status) => statusCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
            int draftCount = CountStatus(InvoiceStatusCatalog.Draft);
            int verifiedCount = CountStatus(InvoiceStatusCatalog.Verified);
            int shippedCount = CountStatus(InvoiceStatusCatalog.Shipped);
            int completedCount = CountStatus(InvoiceStatusCatalog.Completed);
            int totalActiveCount = statusCounts.Sum(item => item.Count);

            var recentInvoices = await activeInvoices
                .OrderByDescending(invoice => invoice.Id)
                .Take(10)
                .Select(invoice => new DashboardRecentInvoice(
                    invoice.Id,
                    invoice.InvoiceNo ?? string.Empty,
                    invoice.Status ?? string.Empty,
                    invoice.Type ?? string.Empty,
                    invoice.InvoiceDate,
                    invoice.TotalAmount,
                    invoice.CustomerNameEN ?? string.Empty))
                .ToListAsync(cancellationToken);

            var todoItems = await BuildTodoItemsAsync(activeInvoices, cancellationToken);
            string singleWindowStatusSummary = await BuildSingleWindowStatusSummaryAsync(context, cancellationToken);

            return new DashboardSnapshot(
                currentPeriod.TotalAmount,
                currentPeriod.TotalProfit,
                currentPeriod.TotalTaxRefundAmount,
                draftCount + verifiedCount,
                shippedCount,
                totalActiveCount,
                singleWindowStatusSummary,
                recentInvoices,
                todoItems,
                $"{localNow:yyyy年M月}",
                previousPeriod.TotalAmount,
                previousPeriod.TotalProfit,
                previousPeriod.TotalTaxRefundAmount,
                currentPeriod.InvoiceCount,
                draftCount,
                verifiedCount,
                completedCount);
        }

        private static async Task<DashboardPeriodAggregates> LoadPeriodAggregatesAsync(
            AppDbContext context,
            IQueryable<Invoice> activeInvoices,
            DateTime previousStartOfMonth,
            DateTime startOfMonth,
            DateTime endOfMonth,
            CancellationToken cancellationToken)
        {
            var periodQuery = activeInvoices.Where(invoice =>
                invoice.InvoiceDate >= previousStartOfMonth &&
                invoice.InvoiceDate < endOfMonth);

            if (!context.Database.IsSqlite())
            {
                var rows = await periodQuery
                    .GroupBy(invoice => invoice.InvoiceDate >= startOfMonth)
                    .Select(group => new DashboardPeriodAggregate
                    {
                        IsCurrent = group.Key,
                        InvoiceCount = group.Count(),
                        TotalAmount = group.Sum(invoice => invoice.TotalAmount),
                        TotalProfit = group.Sum(invoice => invoice.TotalProfit),
                        TotalTaxRefundAmount = group.Sum(invoice => invoice.TotalTaxRefundAmount)
                    })
                    .ToListAsync(cancellationToken);
                return new DashboardPeriodAggregates(
                    rows.FirstOrDefault(row => row.IsCurrent) ?? new DashboardPeriodAggregate { IsCurrent = true },
                    rows.FirstOrDefault(row => !row.IsCurrent) ?? new DashboardPeriodAggregate());
            }

            // SQLite cannot translate decimal SUM without lossy casts. Keep
            // exact decimal arithmetic while limiting materialization to the
            // two displayed months and only the four required columns.
            var sqliteRows = await periodQuery
                .Select(invoice => new DashboardPeriodValue
                {
                    IsCurrent = invoice.InvoiceDate >= startOfMonth,
                    TotalAmount = invoice.TotalAmount,
                    TotalProfit = invoice.TotalProfit,
                    TotalTaxRefundAmount = invoice.TotalTaxRefundAmount
                })
                .ToListAsync(cancellationToken);

            DashboardPeriodAggregate Aggregate(bool isCurrent)
            {
                var values = sqliteRows.Where(row => row.IsCurrent == isCurrent).ToArray();
                return new DashboardPeriodAggregate
                {
                    IsCurrent = isCurrent,
                    InvoiceCount = values.Length,
                    TotalAmount = values.Sum(row => row.TotalAmount),
                    TotalProfit = values.Sum(row => row.TotalProfit),
                    TotalTaxRefundAmount = values.Sum(row => row.TotalTaxRefundAmount)
                };
            }

            return new DashboardPeriodAggregates(Aggregate(true), Aggregate(false));
        }

        private async Task<string> BuildSingleWindowStatusSummaryAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            var batches = _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context);
            int pendingBatchCount = await batches.CountAsync(batch =>
                batch.Status == SingleWindowBatchStatusCatalog.SubmitPackageExported ||
                batch.Status == SingleWindowBatchStatusCatalog.SubmitPackageImported ||
                batch.Status == SingleWindowBatchStatusCatalog.QueuedToClient ||
                batch.Status == SingleWindowBatchStatusCatalog.Received ||
                batch.Status == SingleWindowBatchStatusCatalog.Accepted ||
                batch.Status == SingleWindowBatchStatusCatalog.PendingReview, cancellationToken);
            int failedBatchCount = await batches.CountAsync(batch =>
                batch.Status == SingleWindowBatchStatusCatalog.Rejected ||
                batch.Status == SingleWindowBatchStatusCatalog.Failed, cancellationToken);
            var latestReceiptBatch = await batches
                .Where(batch => batch.LastReceiptAt.HasValue)
                .OrderByDescending(batch => batch.LastReceiptAt)
                .Select(batch => new { batch.InvoiceNo, batch.Status, batch.LastReceiptAt })
                .FirstOrDefaultAsync(cancellationToken);

            var singleWindowParts = new List<string>();
            if (pendingBatchCount > 0)
            {
                singleWindowParts.Add($"待处理 {pendingBatchCount} 批");
            }

            if (failedBatchCount > 0)
            {
                singleWindowParts.Add($"异常 {failedBatchCount} 批");
            }

            if (latestReceiptBatch != null)
            {
                singleWindowParts.Add($"最近回执 {latestReceiptBatch.InvoiceNo} {SingleWindowBatchStatusCatalog.GetDisplayName(latestReceiptBatch.Status)}");
            }

            return singleWindowParts.Count == 0
                ? "单一窗口近况：当前没有待处理批次。"
                : "单一窗口近况：" + string.Join("；", singleWindowParts) + "。";
        }

        private static async Task<IReadOnlyList<DashboardTodoItem>> BuildTodoItemsAsync(
            IQueryable<Invoice> activeInvoices,
            CancellationToken cancellationToken)
        {
            var todoItems = new List<DashboardTodoItem>();

            todoItems.AddRange(await activeInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Shipped)
                .OrderByDescending(invoice => invoice.Id)
                .Take(5)
                .Select(invoice => new DashboardTodoItem(
                    "待收款 (Unpaid)",
                    $"发票 {invoice.InvoiceNo} 已出运，等待结汇。",
                    "ViewInvoice",
                    invoice.Id.ToString()))
                .ToListAsync(cancellationToken));

            todoItems.AddRange(await activeInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Verified)
                .OrderByDescending(invoice => invoice.Id)
                .Take(5)
                .Select(invoice => new DashboardTodoItem(
                    "待出运 (Pending Shipment)",
                    $"发票 {invoice.InvoiceNo} 已核对，等待安排出运。",
                    "ViewInvoice",
                    invoice.Id.ToString()))
                .ToListAsync(cancellationToken));

            todoItems.AddRange(await activeInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Draft)
                .OrderByDescending(invoice => invoice.Id)
                .Take(3)
                .Select(invoice => new DashboardTodoItem(
                    "待核对 (Pending Verification)",
                    $"发票 {invoice.InvoiceNo} 仍在草稿状态。",
                    "ViewInvoice",
                    invoice.Id.ToString()))
                .ToListAsync(cancellationToken));

            return todoItems;
        }

        private static IQueryable<Invoice> BuildPreferredInvoiceQuery(IQueryable<Invoice> source)
        {
            // Keep the existing business rule (actual data wins over customs
            // data, then newest row wins) while letting EF translate the
            // deduplication to SQL instead of materializing all rows.
            var preferredIds = source
                .GroupBy(invoice => new
                {
                    CompanyScope = (invoice.CompanyScope ?? string.Empty).Trim(),
                    InvoiceNo = (invoice.InvoiceNo ?? string.Empty).Trim()
                })
                .Select(group => group
                    .OrderByDescending(invoice => invoice.Type != null && invoice.Type.Contains("实际"))
                    .ThenByDescending(invoice => invoice.Type != null && invoice.Type.Contains("报关"))
                    .ThenByDescending(invoice => invoice.Id)
                    .Select(invoice => invoice.Id)
                    .First());

            return source.Where(invoice => preferredIds.Contains(invoice.Id));
        }

        private sealed record DashboardPeriodAggregates(
            DashboardPeriodAggregate Current,
            DashboardPeriodAggregate Previous);

        private sealed class DashboardPeriodAggregate
        {
            public bool IsCurrent { get; init; }
            public int InvoiceCount { get; init; }
            public decimal TotalAmount { get; init; }
            public decimal TotalProfit { get; init; }
            public decimal TotalTaxRefundAmount { get; init; }
        }

        private sealed class DashboardPeriodValue
        {
            public bool IsCurrent { get; init; }
            public decimal TotalAmount { get; init; }
            public decimal TotalProfit { get; init; }
            public decimal TotalTaxRefundAmount { get; init; }
        }
    }
}
