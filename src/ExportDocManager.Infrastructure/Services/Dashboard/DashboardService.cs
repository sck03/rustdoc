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
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1);
            var scopedInvoices = _businessDataAccessScope.ApplyInvoiceScope(context.Invoices.AsNoTracking());
            var activeInvoices = BuildPreferredInvoiceQuery(
                scopedInvoices.Where(invoice => invoice.Status != InvoiceStatusCatalog.Cancelled));

            // Only the current and previous period rows are materialized.  The
            // former implementation loaded every active invoice into the API
            // process in order to deduplicate and count statuses, which made
            // the dashboard increasingly expensive as history grew.
            var monthlyInvoices = await SelectDashboardInvoiceSnapshots(activeInvoices
                    .Where(invoice => invoice.InvoiceDate >= startOfMonth && invoice.InvoiceDate < endOfMonth),
                includeCustomer: true,
                cancellationToken);
            var previousMonthlyInvoices = await SelectDashboardInvoiceSnapshots(activeInvoices
                    .Where(invoice => invoice.InvoiceDate >= startOfMonth.AddMonths(-1) && invoice.InvoiceDate < startOfMonth),
                includeCustomer: false,
                cancellationToken);

            var statusCounts = await activeInvoices
                .GroupBy(invoice => invoice.Status ?? string.Empty)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);

            int CountStatus(string status) => statusCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
            int draftCount = CountStatus(InvoiceStatusCatalog.Draft);
            int verifiedCount = CountStatus(InvoiceStatusCatalog.Verified);
            int shippedCount = CountStatus(InvoiceStatusCatalog.Shipped);
            int completedCount = CountStatus(InvoiceStatusCatalog.Completed);
            int totalActiveCount = await activeInvoices.CountAsync(cancellationToken);

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
                monthlyInvoices.Sum(invoice => invoice.TotalAmount),
                monthlyInvoices.Sum(invoice => invoice.TotalProfit),
                monthlyInvoices.Sum(invoice => invoice.TotalTaxRefundAmount),
                draftCount + verifiedCount,
                shippedCount,
                totalActiveCount,
                singleWindowStatusSummary,
                recentInvoices,
                todoItems,
                $"{now:yyyy年M月}",
                previousMonthlyInvoices.Sum(invoice => invoice.TotalAmount),
                previousMonthlyInvoices.Sum(invoice => invoice.TotalProfit),
                previousMonthlyInvoices.Sum(invoice => invoice.TotalTaxRefundAmount),
                monthlyInvoices.Count,
                draftCount,
                verifiedCount,
                completedCount);
        }

        private static async Task<List<DashboardInvoiceSnapshot>> SelectDashboardInvoiceSnapshots(
            IQueryable<Invoice> query,
            bool includeCustomer,
            CancellationToken cancellationToken)
        {
            return await query
                .Select(invoice => new DashboardInvoiceSnapshot
                {
                    Id = invoice.Id,
                    InvoiceNo = invoice.InvoiceNo,
                    Status = invoice.Status,
                    Type = invoice.Type,
                    InvoiceDate = invoice.InvoiceDate,
                    TotalAmount = invoice.TotalAmount,
                    TotalProfit = invoice.TotalProfit,
                    TotalTaxRefundAmount = invoice.TotalTaxRefundAmount,
                    CustomerNameEN = includeCustomer ? invoice.CustomerNameEN : string.Empty
                })
                .ToListAsync(cancellationToken);
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
                .GroupBy(invoice => (invoice.InvoiceNo ?? string.Empty).Trim())
                .Select(group => group
                    .OrderByDescending(invoice => invoice.Type != null && invoice.Type.Contains("实际"))
                    .ThenByDescending(invoice => invoice.Type != null && invoice.Type.Contains("报关"))
                    .ThenByDescending(invoice => invoice.Id)
                    .Select(invoice => invoice.Id)
                    .First());

            return source.Where(invoice => preferredIds.Contains(invoice.Id));
        }

        private sealed class DashboardInvoiceSnapshot
        {
            public int Id { get; init; }
            public string InvoiceNo { get; init; }
            public string Status { get; init; }
            public string Type { get; init; }
            public DateTime InvoiceDate { get; init; }
            public decimal TotalAmount { get; init; }
            public decimal TotalProfit { get; init; }
            public decimal TotalTaxRefundAmount { get; init; }
            public string CustomerNameEN { get; init; }
        }
    }
}
