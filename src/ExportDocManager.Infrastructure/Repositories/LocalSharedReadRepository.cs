using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class LocalSharedReadRepository :
        IInvoiceListReadRepository,
        IPaymentReadRepository,
        IPaymentDetailReadRepository,
        IQueryReadRepository,
        IAuditLogReadRepository
    {
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public LocalSharedReadRepository(IDbContextFactory<AppDbContext> contextFactory)
            : this(contextFactory, new DatabaseConnectionSettings())
        {
        }

        public LocalSharedReadRepository(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings)
            : this(contextFactory, databaseSettings, null)
        {
        }

        public LocalSharedReadRepository(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope? businessDataAccessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<PagedResult<Invoice>> QueryPageAsync(
            InvoiceListPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            var invoiceQuery = BuildInvoiceListQuery(context, normalizedQuery);
            invoiceQuery = ApplyInvoiceAccessScope(invoiceQuery);

            var totalCount = await invoiceQuery.CountAsync(cancellationToken);
            var items = await invoiceQuery
                .Skip(PagingHelper.CalculateOffset(normalizedQuery.PageNumber, normalizedQuery.PageSize))
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<Invoice>(items, totalCount, normalizedQuery.PageNumber, normalizedQuery.PageSize);
        }

        public async Task<PagedResult<Payment>> QueryPageAsync(
            PaymentPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            var paymentQuery = BuildPaymentQuery(context, normalizedQuery);
            paymentQuery = ApplyPaymentAccessScope(paymentQuery);

            var totalCount = await paymentQuery.CountAsync(cancellationToken);
            var items = await paymentQuery
                .Skip(PagingHelper.CalculateOffset(normalizedQuery.PageNumber, normalizedQuery.PageSize))
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<Payment>(items, totalCount, normalizedQuery.PageNumber, normalizedQuery.PageSize);
        }

        public async Task<Payment?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await ApplyPaymentAccessScope(context.Payments.AsNoTracking())
                .FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);
        }

        public async Task<PagedResult<Invoice>> QueryPageAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            var invoiceQuery = BuildQueryFormQuery(context, normalizedQuery);
            invoiceQuery = ApplyInvoiceAccessScope(invoiceQuery);

            var totalCount = await invoiceQuery.CountAsync(cancellationToken);
            var items = await invoiceQuery
                .Skip(PagingHelper.CalculateOffset(normalizedQuery.PageNumber, normalizedQuery.PageSize))
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<Invoice>(items, totalCount, normalizedQuery.PageNumber, normalizedQuery.PageSize);
        }

        public async Task<int> CountAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            return await ApplyInvoiceAccessScope(BuildQueryFormQuery(context, normalizedQuery))
                .CountAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<QueryResultRow>> QueryExportBatchAsync(
            QueryPageQuery query,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            var rows = await ApplyInvoiceAccessScope(BuildQueryFormQuery(context, normalizedQuery))
                .Skip(Math.Max(0, skip))
                .Take(Math.Clamp(take, 1, 1000))
                .Select(invoice => new
                {
                    invoice.Id,
                    invoice.InvoiceNo,
                    invoice.InvoiceDate,
                    invoice.ContractNo,
                    invoice.CustomerNameEN,
                    invoice.ExporterNameEN,
                    invoice.ExporterNameCN,
                    invoice.DestinationCountry,
                    invoice.TradeTerms,
                    invoice.ShipmentDate,
                    invoice.TransportMode,
                    invoice.TotalCartons,
                    invoice.TotalQuantity,
                    invoice.TotalAmount,
                    invoice.Currency,
                    invoice.Type
                })
                .ToListAsync(cancellationToken);

            return rows.Select(invoice => new QueryResultRow
            {
                Id = invoice.Id,
                InvoiceNo = invoice.InvoiceNo ?? string.Empty,
                InvoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
                ContractNo = invoice.ContractNo ?? string.Empty,
                CustomerName = invoice.CustomerNameEN ?? string.Empty,
                ExporterName = invoice.ExporterNameEN ?? invoice.ExporterNameCN ?? string.Empty,
                DestinationCountry = invoice.DestinationCountry ?? string.Empty,
                TradeTerms = invoice.TradeTerms ?? string.Empty,
                ShipmentDate = invoice.ShipmentDate == default
                    ? string.Empty
                    : invoice.ShipmentDate.ToString("yyyy-MM-dd"),
                TransportMode = invoice.TransportMode ?? string.Empty,
                TotalCartons = invoice.TotalCartons,
                TotalQuantity = invoice.TotalQuantity,
                TotalAmount = invoice.TotalAmount,
                Currency = invoice.Currency ?? string.Empty,
                Type = invoice.Type ?? string.Empty
            }).ToList();
        }

        public async Task<PagedResult<AuditLog>> QueryPageAsync(
            AuditLogPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            var auditQuery = BuildAuditLogQuery(context, normalizedQuery);

            var totalCount = await auditQuery.CountAsync(cancellationToken);
            var items = await auditQuery
                .Skip(PagingHelper.CalculateOffset(normalizedQuery.PageNumber, normalizedQuery.PageSize))
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<AuditLog>(items, totalCount, normalizedQuery.PageNumber, normalizedQuery.PageSize);
        }

        public async Task<IReadOnlyList<AuditLog>> QueryAllAsync(
            AuditLogPageQuery query,
            int maxCount = 2000,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            return await BuildAuditLogQuery(context, normalizedQuery)
                .Take(Math.Max(1, maxCount))
                .ToListAsync(cancellationToken);
        }

        private static InvoiceListPageQuery Normalize(InvoiceListPageQuery query)
        {
            query ??= new InvoiceListPageQuery();
            return query with
            {
                Keyword = TextSearchHelper.NormalizeFilter(query.Keyword),
                SortColumn = TextSearchHelper.NormalizeFilter(query.SortColumn),
                PageNumber = NormalizePageNumber(query.PageNumber),
                PageSize = NormalizePageSize(query.PageSize)
            };
        }

        private static PaymentPageQuery Normalize(PaymentPageQuery query)
        {
            query ??= new PaymentPageQuery();
            return query with
            {
                Keyword = TextSearchHelper.NormalizeFilter(query.Keyword),
                PageNumber = NormalizePageNumber(query.PageNumber),
                PageSize = NormalizePageSize(query.PageSize)
            };
        }

        private static QueryPageQuery Normalize(QueryPageQuery query)
        {
            query ??= new QueryPageQuery();
            return query with
            {
                Keyword = TextSearchHelper.NormalizeFilter(query.Keyword),
                ContractNo = TextSearchHelper.NormalizeFilter(query.ContractNo),
                InvoiceType = TextSearchHelper.NormalizeFilter(query.InvoiceType),
                TransportMode = TextSearchHelper.NormalizeFilter(query.TransportMode),
                StyleName = TextSearchHelper.NormalizeFilter(query.StyleName),
                StyleNo = TextSearchHelper.NormalizeFilter(query.StyleNo),
                PageNumber = NormalizePageNumber(query.PageNumber),
                PageSize = NormalizePageSize(query.PageSize)
            };
        }

        private static AuditLogPageQuery Normalize(AuditLogPageQuery query)
        {
            return AuditLogQueryHelper.NormalizePageQuery(query) with
            {
                PageNumber = NormalizePageNumber(query?.PageNumber ?? 1),
                PageSize = NormalizePageSize(query?.PageSize ?? DefaultPageSize)
            };
        }

        private static IQueryable<Invoice> BuildInvoiceListQuery(AppDbContext context, InvoiceListPageQuery query)
        {
            var invoiceQuery = context.Invoices.AsNoTracking().AsQueryable();
            invoiceQuery = invoiceQuery.ApplyKeywordSearch(
                query.Keyword,
                invoice => invoice.InvoiceNo,
                invoice => invoice.ContractNo,
                invoice => invoice.CustomerNameEN,
                invoice => invoice.NotifyPartyName,
                invoice => invoice.ExporterNameEN,
                invoice => invoice.ExporterNameCN,
                invoice => invoice.PortOfLoading,
                invoice => invoice.PortOfDestination,
                invoice => invoice.DestinationCountry);

            return ApplyInvoiceListSort(invoiceQuery, query.SortColumn, query.Ascending);
        }

        private static IQueryable<Payment> BuildPaymentQuery(AppDbContext context, PaymentPageQuery query)
        {
            return context.Payments
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(
                    query.Keyword,
                    payment => payment.InvoiceNo,
                    payment => payment.PayerName,
                    payment => payment.Project,
                    payment => payment.Department,
                    payment => payment.PayeeName,
                    payment => payment.BankName,
                    payment => payment.AccountNo,
                    payment => payment.GoodsName,
                    payment => payment.ShipmentCountry,
                    payment => payment.Notes)
                .OrderByDescending(payment => payment.PaymentDate)
                .ThenByDescending(payment => payment.Id);
        }

        private static IQueryable<Invoice> BuildQueryFormQuery(AppDbContext context, QueryPageQuery query)
        {
            var invoiceQuery = context.Invoices.AsNoTracking().AsQueryable();

            if (query.StartDate.HasValue)
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.ShipmentDate >= query.StartDate.Value);
            }

            if (query.EndDateExclusive.HasValue)
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.ShipmentDate < query.EndDateExclusive.Value);
            }

            if (query.CustomerId.HasValue && query.CustomerId.Value > 0)
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.CustomerId == query.CustomerId.Value);
            }

            if (query.ExporterId.HasValue && query.ExporterId.Value > 0)
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.ExporterId == query.ExporterId.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.InvoiceType))
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.Type == query.InvoiceType);
            }

            if (!string.IsNullOrWhiteSpace(query.TransportMode))
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.TransportMode == query.TransportMode);
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                invoiceQuery = ApplyQueryKeywordSearch(invoiceQuery, query.Keyword);
            }

            if (!string.IsNullOrWhiteSpace(query.ContractNo) &&
                !string.Equals(query.ContractNo, query.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                invoiceQuery = invoiceQuery.ApplyKeywordSearch(query.ContractNo, invoice => invoice.ContractNo);
            }

            if (!string.IsNullOrWhiteSpace(query.StyleName))
            {
                invoiceQuery = invoiceQuery.Where(invoice =>
                    invoice.Items.Any(item => item.StyleName != null && item.StyleName.Contains(query.StyleName)));
            }

            if (!string.IsNullOrWhiteSpace(query.StyleNo))
            {
                invoiceQuery = invoiceQuery.Where(invoice =>
                    invoice.Items.Any(item => item.StyleNo != null && item.StyleNo.Contains(query.StyleNo)));
            }

            return invoiceQuery
                .OrderByDescending(invoice => invoice.InvoiceDate)
                .ThenByDescending(invoice => invoice.Id);
        }

        private static IQueryable<Invoice> ApplyQueryKeywordSearch(
            IQueryable<Invoice> invoiceQuery,
            string keyword)
        {
            foreach (var token in TextSearchHelper.Tokenize(keyword))
            {
                var normalizedToken = token.ToUpperInvariant();
                if (normalizedToken.Any(char.IsDigit))
                {
                    invoiceQuery = invoiceQuery.Where(invoice =>
                        (invoice.InvoiceNo != null && invoice.InvoiceNo.ToUpper().StartsWith(normalizedToken)) ||
                        (invoice.ContractNo != null && invoice.ContractNo.ToUpper().StartsWith(normalizedToken)) ||
                        invoice.Items.Any(item =>
                            (item.PoNumber != null && item.PoNumber.ToUpper().StartsWith(normalizedToken)) ||
                            (item.StyleNo != null && item.StyleNo.ToUpper().StartsWith(normalizedToken)) ||
                            (item.HSCode != null && item.HSCode.ToUpper().StartsWith(normalizedToken))));
                    continue;
                }

                invoiceQuery = ApplyCaseInsensitiveQueryTextSearch(invoiceQuery, normalizedToken);
            }

            return invoiceQuery;
        }

        private static IQueryable<Invoice> ApplyCaseInsensitiveQueryTextSearch(
            IQueryable<Invoice> query,
            string token) => query.Where(invoice =>
                (invoice.InvoiceNo != null && invoice.InvoiceNo.ToUpper().Contains(token)) ||
                (invoice.ContractNo != null && invoice.ContractNo.ToUpper().Contains(token)) ||
                (invoice.CustomerNameEN != null && invoice.CustomerNameEN.ToUpper().Contains(token)) ||
                (invoice.NotifyPartyName != null && invoice.NotifyPartyName.ToUpper().Contains(token)) ||
                (invoice.ExporterNameEN != null && invoice.ExporterNameEN.ToUpper().Contains(token)) ||
                (invoice.ExporterNameCN != null && invoice.ExporterNameCN.ToUpper().Contains(token)) ||
                (invoice.DestinationCountry != null && invoice.DestinationCountry.ToUpper().Contains(token)) ||
                (invoice.PortOfLoading != null && invoice.PortOfLoading.ToUpper().Contains(token)) ||
                (invoice.PortOfDestination != null && invoice.PortOfDestination.ToUpper().Contains(token)) ||
                (invoice.TradeTerms != null && invoice.TradeTerms.ToUpper().Contains(token)) ||
                (invoice.TransportMode != null && invoice.TransportMode.ToUpper().Contains(token)) ||
                invoice.Items.Any(item =>
                    (item.PoNumber != null && item.PoNumber.ToUpper().Contains(token)) ||
                    (item.StyleName != null && item.StyleName.ToUpper().Contains(token)) ||
                    (item.StyleNameCN != null && item.StyleNameCN.ToUpper().Contains(token)) ||
                    (item.StyleNo != null && item.StyleNo.ToUpper().Contains(token)) ||
                    (item.HSCode != null && item.HSCode.ToUpper().Contains(token)) ||
                    (item.Brand != null && item.Brand.ToUpper().Contains(token)) ||
                    (item.Origin != null && item.Origin.ToUpper().Contains(token))));

        private static IQueryable<AuditLog> BuildAuditLogQuery(AppDbContext context, AuditLogPageQuery query)
        {
            return AuditLogQueryHelper
                .ApplyCriteria(context.AuditLogs.AsNoTracking().AsQueryable(), query)
                .OrderByDescending(log => log.Timestamp);
        }

        private static IQueryable<Invoice> ApplyInvoiceListSort(
            IQueryable<Invoice> query,
            string sortColumn,
            bool ascending)
        {
            return sortColumn?.ToLowerInvariant() switch
            {
                "invoicedate" => ascending
                    ? query.OrderBy(invoice => invoice.InvoiceDate).ThenBy(invoice => invoice.Id)
                    : query.OrderByDescending(invoice => invoice.InvoiceDate).ThenByDescending(invoice => invoice.Id),
                "invoiceno" => ascending
                    ? query.OrderBy(invoice => invoice.InvoiceNo).ThenBy(invoice => invoice.Id)
                    : query.OrderByDescending(invoice => invoice.InvoiceNo).ThenByDescending(invoice => invoice.Id),
                "totalamount" => ascending
                    ? query.OrderBy(invoice => invoice.TotalAmount).ThenBy(invoice => invoice.Id)
                    : query.OrderByDescending(invoice => invoice.TotalAmount).ThenByDescending(invoice => invoice.Id),
                _ => query.OrderByDescending(invoice => invoice.InvoiceDate).ThenByDescending(invoice => invoice.Id)
            };
        }

        private IQueryable<Invoice> ApplyInvoiceAccessScope(IQueryable<Invoice> query)
        {
            return _businessDataAccessScope.ApplyInvoiceScope(query);
        }

        private IQueryable<Payment> ApplyPaymentAccessScope(IQueryable<Payment> query)
        {
            return _businessDataAccessScope.ApplyPaymentScope(query);
        }

        private static int NormalizePageNumber(int pageNumber)
        {
            return Math.Max(1, pageNumber);
        }

        private static int NormalizePageSize(int pageSize)
        {
            var normalizedPageSize = pageSize <= 0 ? DefaultPageSize : pageSize;
            return Math.Clamp(normalizedPageSize, 1, MaxPageSize);
        }
    }
}
