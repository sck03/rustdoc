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
        private static readonly string[] InvoiceListSearchColumns =
        [
            "InvoiceNo", "ContractNo", "CustomerNameEN", "NotifyPartyName", "ExporterNameEN",
            "ExporterNameCN", "PortOfLoading", "PortOfDestination", "DestinationCountry"
        ];
        private static readonly string[] PaymentSearchColumns =
        [
            "InvoiceNo", "PayerName", "Project", "Department", "PayeeName", "BankName",
            "AccountNo", "GoodsName", "ShipmentCountry", "Notes"
        ];
        private static readonly string[] QueryTextSearchColumns =
        [
            "InvoiceNo", "ContractNo", "CustomerNameEN", "NotifyPartyName", "ExporterNameEN",
            "ExporterNameCN", "DestinationCountry", "PortOfLoading", "PortOfDestination",
            "TradeTerms", "TransportMode", "ItemPoNumber", "ItemStyleName", "ItemStyleNameCN",
            "ItemStyleNo", "ItemHSCode", "ItemBrand", "ItemOrigin"
        ];
        private static readonly string[] QueryIdentifierSearchColumns =
            ["InvoiceNo", "ContractNo", "ItemPoNumber", "ItemStyleNo", "ItemHSCode"];
        private static readonly string[] ContractSearchColumn = ["ContractNo"];
        private static readonly string[] StyleNameSearchColumn = ["ItemStyleName"];
        private static readonly string[] StyleNoSearchColumn = ["ItemStyleNo"];
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        internal LocalSharedReadRepository(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope)
            : this(contextFactory, businessDataAccessScope)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);
        }

        public LocalSharedReadRepository(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope businessDataAccessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
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
            invoiceQuery = ApplyQueryAccessScope(invoiceQuery, PermissionAction.View);

            var totalCount = await invoiceQuery.CountAsync(cancellationToken);
            var items = await invoiceQuery
                .Skip(PagingHelper.CalculateOffset(normalizedQuery.PageNumber, normalizedQuery.PageSize))
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<Invoice>(items, totalCount, normalizedQuery.PageNumber, normalizedQuery.PageSize);
        }

        public async Task<int> CountExportAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = Normalize(query);
            return await ApplyQueryAccessScope(BuildQueryFormQuery(context, normalizedQuery), PermissionAction.Operate)
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
            var rows = await ApplyQueryAccessScope(BuildQueryFormQuery(context, normalizedQuery), PermissionAction.Operate)
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
            invoiceQuery = context.Database.IsSqlite()
                ? ApplySqliteInvoiceSearch(context, invoiceQuery, query.Keyword, InvoiceListSearchColumns)
                : invoiceQuery.ApplyKeywordSearch(
                    context,
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
            var paymentQuery = context.Payments.AsNoTracking().AsQueryable();
            paymentQuery = context.Database.IsSqlite()
                ? ApplySqlitePaymentSearch(context, paymentQuery, query.Keyword)
                : paymentQuery.ApplyKeywordSearch(
                    context,
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
                    payment => payment.Notes);

            return paymentQuery
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
                invoiceQuery = context.Database.IsSqlite()
                    ? ApplySqliteInvoiceSearch(
                        context,
                        invoiceQuery,
                        query.Keyword,
                        QueryTextSearchColumns,
                        QueryIdentifierSearchColumns)
                    : ApplyPostgreSqlQueryKeywordSearch(invoiceQuery, query.Keyword);
            }

            if (!string.IsNullOrWhiteSpace(query.ContractNo) &&
                !string.Equals(query.ContractNo, query.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                invoiceQuery = context.Database.IsSqlite()
                    ? ApplySqliteInvoiceSearch(context, invoiceQuery, query.ContractNo, ContractSearchColumn)
                    : invoiceQuery.ApplyKeywordSearch(context, query.ContractNo, invoice => invoice.ContractNo);
            }

            if (!string.IsNullOrWhiteSpace(query.StyleName))
            {
                if (context.Database.IsSqlite())
                {
                    invoiceQuery = ApplySqliteInvoiceSearch(
                        context,
                        invoiceQuery,
                        query.StyleName,
                        StyleNameSearchColumn);
                }
                else
                {
                    string pattern = $"%{EfTextSearchExtensions.EscapeLikePattern(query.StyleName)}%";
                    invoiceQuery = invoiceQuery.Where(invoice => invoice.Items.Any(item =>
                        item.StyleName != null && EF.Functions.ILike(item.StyleName, pattern, "\\")));
                }
            }

            if (!string.IsNullOrWhiteSpace(query.StyleNo))
            {
                if (context.Database.IsSqlite())
                {
                    invoiceQuery = ApplySqliteInvoiceSearch(
                        context,
                        invoiceQuery,
                        query.StyleNo,
                        StyleNoSearchColumn);
                }
                else
                {
                    string pattern = $"%{EfTextSearchExtensions.EscapeLikePattern(query.StyleNo)}%";
                    invoiceQuery = invoiceQuery.Where(invoice => invoice.Items.Any(item =>
                        item.StyleNo != null && EF.Functions.ILike(item.StyleNo, pattern, "\\")));
                }
            }

            return invoiceQuery
                .OrderByDescending(invoice => invoice.InvoiceDate)
                .ThenByDescending(invoice => invoice.Id);
        }

        private static IQueryable<Invoice> ApplyPostgreSqlQueryKeywordSearch(
            IQueryable<Invoice> invoiceQuery,
            string keyword)
        {
            foreach (var token in TextSearchHelper.Tokenize(keyword))
            {
                if (token.Any(char.IsDigit))
                {
                    string pattern = $"{EfTextSearchExtensions.EscapeLikePattern(token)}%";
                    invoiceQuery = invoiceQuery.Where(invoice =>
                        (invoice.InvoiceNo != null && EF.Functions.ILike(invoice.InvoiceNo, pattern, "\\")) ||
                        (invoice.ContractNo != null && EF.Functions.ILike(invoice.ContractNo, pattern, "\\")) ||
                        invoice.Items.Any(item =>
                            (item.PoNumber != null && EF.Functions.ILike(item.PoNumber, pattern, "\\")) ||
                            (item.StyleNo != null && EF.Functions.ILike(item.StyleNo, pattern, "\\")) ||
                            (item.HSCode != null && EF.Functions.ILike(item.HSCode, pattern, "\\"))));
                    continue;
                }

                invoiceQuery = ApplyPostgreSqlQueryTextSearch(invoiceQuery, token);
            }

            return invoiceQuery;
        }

        private static IQueryable<Invoice> ApplyPostgreSqlQueryTextSearch(
            IQueryable<Invoice> query,
            string token)
        {
            string pattern = $"%{EfTextSearchExtensions.EscapeLikePattern(token)}%";
            return query.Where(invoice =>
                (invoice.InvoiceNo != null && EF.Functions.ILike(invoice.InvoiceNo, pattern, "\\")) ||
                (invoice.ContractNo != null && EF.Functions.ILike(invoice.ContractNo, pattern, "\\")) ||
                (invoice.CustomerNameEN != null && EF.Functions.ILike(invoice.CustomerNameEN, pattern, "\\")) ||
                (invoice.NotifyPartyName != null && EF.Functions.ILike(invoice.NotifyPartyName, pattern, "\\")) ||
                (invoice.ExporterNameEN != null && EF.Functions.ILike(invoice.ExporterNameEN, pattern, "\\")) ||
                (invoice.ExporterNameCN != null && EF.Functions.ILike(invoice.ExporterNameCN, pattern, "\\")) ||
                (invoice.DestinationCountry != null && EF.Functions.ILike(invoice.DestinationCountry, pattern, "\\")) ||
                (invoice.PortOfLoading != null && EF.Functions.ILike(invoice.PortOfLoading, pattern, "\\")) ||
                (invoice.PortOfDestination != null && EF.Functions.ILike(invoice.PortOfDestination, pattern, "\\")) ||
                (invoice.TradeTerms != null && EF.Functions.ILike(invoice.TradeTerms, pattern, "\\")) ||
                (invoice.TransportMode != null && EF.Functions.ILike(invoice.TransportMode, pattern, "\\")) ||
                invoice.Items.Any(item =>
                    (item.PoNumber != null && EF.Functions.ILike(item.PoNumber, pattern, "\\")) ||
                    (item.StyleName != null && EF.Functions.ILike(item.StyleName, pattern, "\\")) ||
                    (item.StyleNameCN != null && EF.Functions.ILike(item.StyleNameCN, pattern, "\\")) ||
                    (item.StyleNo != null && EF.Functions.ILike(item.StyleNo, pattern, "\\")) ||
                    (item.HSCode != null && EF.Functions.ILike(item.HSCode, pattern, "\\")) ||
                    (item.Brand != null && EF.Functions.ILike(item.Brand, pattern, "\\")) ||
                    (item.Origin != null && EF.Functions.ILike(item.Origin, pattern, "\\"))));
        }

        private static IQueryable<Invoice> ApplySqliteInvoiceSearch(
            AppDbContext context,
            IQueryable<Invoice> query,
            string? keyword,
            IReadOnlyList<string> containsColumns,
            IReadOnlyList<string>? numericPrefixColumns = null)
        {
            IQueryable<int> matchingIds = SqliteFtsSearch.QueryIds(
                context,
                "InvoiceSearch",
                "InvoiceId",
                keyword,
                containsColumns,
                numericPrefixColumns);
            return query.Where(invoice => matchingIds.Contains(invoice.Id));
        }

        private static IQueryable<Payment> ApplySqlitePaymentSearch(
            AppDbContext context,
            IQueryable<Payment> query,
            string? keyword)
        {
            IQueryable<int> matchingIds = SqliteFtsSearch.QueryIds(
                context,
                "PaymentSearch",
                "PaymentId",
                keyword,
                PaymentSearchColumns);
            return query.Where(payment => matchingIds.Contains(payment.Id));
        }

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

        private IQueryable<Invoice> ApplyQueryAccessScope(IQueryable<Invoice> query, string action)
        {
            if (_businessDataAccessScope.UsesPostgreSql)
                _businessDataAccessScope.DemandPermission(PermissionModuleCatalog.DocumentQuery, action);
            return _businessDataAccessScope.ApplyInvoiceScopeForPermission(
                ApplyInvoiceAccessScope(query), PermissionModuleCatalog.DocumentQuery, action);
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
