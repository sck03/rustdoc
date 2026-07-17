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

            var monthlyInvoicesRaw = await scopedInvoices
                .Where(invoice =>
                    invoice.InvoiceDate >= startOfMonth &&
                    invoice.InvoiceDate < endOfMonth &&
                    invoice.Status != InvoiceStatusCatalog.Cancelled)
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
                    CustomerNameEN = invoice.CustomerNameEN
                })
                .ToListAsync(cancellationToken);

            var uniqueMonthlyInvoices = DeduplicateByInvoiceNo(monthlyInvoicesRaw);

            var allActiveInvoices = await scopedInvoices
                .Where(invoice => invoice.Status != InvoiceStatusCatalog.Cancelled)
                .Select(invoice => new DashboardInvoiceSnapshot
                {
                    Id = invoice.Id,
                    InvoiceNo = invoice.InvoiceNo,
                    Status = invoice.Status,
                    Type = invoice.Type
                })
                .ToListAsync(cancellationToken);

            var uniqueActiveInvoices = DeduplicateByInvoiceNo(allActiveInvoices);

            var recentRaw = await scopedInvoices
                .OrderByDescending(invoice => invoice.Id)
                .Take(80)
                .Select(invoice => new DashboardInvoiceSnapshot
                {
                    Id = invoice.Id,
                    InvoiceNo = invoice.InvoiceNo,
                    Status = invoice.Status,
                    Type = invoice.Type,
                    InvoiceDate = invoice.InvoiceDate,
                    TotalAmount = invoice.TotalAmount,
                    CustomerNameEN = invoice.CustomerNameEN
                })
                .ToListAsync(cancellationToken);

            var recentInvoices = DeduplicateByInvoiceNo(recentRaw)
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
                .ToList();

            var todoItems = BuildTodoItems(uniqueActiveInvoices);
            string singleWindowStatusSummary = await BuildSingleWindowStatusSummaryAsync(context, cancellationToken);

            return new DashboardSnapshot(
                uniqueMonthlyInvoices.Sum(invoice => invoice.TotalAmount),
                uniqueMonthlyInvoices.Sum(invoice => invoice.TotalProfit),
                uniqueMonthlyInvoices.Sum(invoice => invoice.TotalTaxRefundAmount),
                uniqueActiveInvoices.Count(invoice =>
                    invoice.Status == InvoiceStatusCatalog.Draft ||
                    invoice.Status == InvoiceStatusCatalog.Verified),
                uniqueActiveInvoices.Count(invoice => invoice.Status == InvoiceStatusCatalog.Shipped),
                uniqueActiveInvoices.Count,
                singleWindowStatusSummary,
                recentInvoices,
                todoItems);
        }

        private async Task<string> BuildSingleWindowStatusSummaryAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            var batchSummaries = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context)
                .Select(batch => new
                {
                    batch.InvoiceNo,
                    batch.Status,
                    batch.LastReceiptAt
                })
                .ToListAsync(cancellationToken);

            int pendingBatchCount = batchSummaries.Count(batch =>
                batch.Status == SingleWindowBatchStatusCatalog.SubmitPackageExported ||
                batch.Status == SingleWindowBatchStatusCatalog.SubmitPackageImported ||
                batch.Status == SingleWindowBatchStatusCatalog.QueuedToClient ||
                batch.Status == SingleWindowBatchStatusCatalog.Received ||
                batch.Status == SingleWindowBatchStatusCatalog.Accepted ||
                batch.Status == SingleWindowBatchStatusCatalog.PendingReview);
            int failedBatchCount = batchSummaries.Count(batch =>
                batch.Status == SingleWindowBatchStatusCatalog.Rejected ||
                batch.Status == SingleWindowBatchStatusCatalog.Failed);
            var latestReceiptBatch = batchSummaries
                .Where(batch => batch.LastReceiptAt.HasValue)
                .OrderByDescending(batch => batch.LastReceiptAt)
                .FirstOrDefault();

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

        private static IReadOnlyList<DashboardTodoItem> BuildTodoItems(
            IReadOnlyList<DashboardInvoiceSnapshot> uniqueActiveInvoices)
        {
            var todoItems = new List<DashboardTodoItem>();

            todoItems.AddRange(uniqueActiveInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Shipped)
                .Take(5)
                .Select(invoice => new DashboardTodoItem(
                    "待收款 (Unpaid)",
                    $"发票 {invoice.InvoiceNo} 已出运，等待结汇。",
                    "ViewInvoice",
                    invoice.Id.ToString())));

            todoItems.AddRange(uniqueActiveInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Verified)
                .Take(5)
                .Select(invoice => new DashboardTodoItem(
                    "待出运 (Pending Shipment)",
                    $"发票 {invoice.InvoiceNo} 已核对，等待安排出运。",
                    "ViewInvoice",
                    invoice.Id.ToString())));

            todoItems.AddRange(uniqueActiveInvoices
                .Where(invoice => invoice.Status == InvoiceStatusCatalog.Draft)
                .Take(3)
                .Select(invoice => new DashboardTodoItem(
                    "待核对 (Pending Verification)",
                    $"发票 {invoice.InvoiceNo} 仍在草稿状态。",
                    "ViewInvoice",
                    invoice.Id.ToString())));

            return todoItems;
        }

        private static IReadOnlyList<DashboardInvoiceSnapshot> DeduplicateByInvoiceNo(
            IEnumerable<DashboardInvoiceSnapshot> source)
        {
            if (source == null)
            {
                return [];
            }

            return source
                .GroupBy(invoice => NormalizeInvoiceKey(invoice.InvoiceNo))
                .Select(SelectPreferredInvoice)
                .Where(invoice => invoice != null)
                .ToList();
        }

        private static DashboardInvoiceSnapshot SelectPreferredInvoice(
            IEnumerable<DashboardInvoiceSnapshot> group)
        {
            var candidates = group?.Where(item => item != null).ToList() ?? [];
            if (candidates.Count == 0)
            {
                return null;
            }

            var actual = candidates.FirstOrDefault(invoice =>
                !string.IsNullOrEmpty(invoice.Type) &&
                invoice.Type.Contains("实际", StringComparison.Ordinal));
            if (actual != null)
            {
                return actual;
            }

            var customs = candidates.FirstOrDefault(invoice =>
                !string.IsNullOrEmpty(invoice.Type) &&
                invoice.Type.Contains("报关", StringComparison.Ordinal));
            if (customs != null)
            {
                return customs;
            }

            return candidates.OrderByDescending(invoice => invoice.Id).First();
        }

        private static string NormalizeInvoiceKey(string invoiceNo)
        {
            return string.IsNullOrWhiteSpace(invoiceNo) ? string.Empty : invoiceNo.Trim();
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
