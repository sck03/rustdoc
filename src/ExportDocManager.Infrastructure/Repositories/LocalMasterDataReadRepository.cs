using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class LocalMasterDataReadRepository :
        ICustomerReadRepository,
        IExporterReadRepository,
        IPayeeReadRepository,
        IProductReadRepository,
        IPortReadRepository,
        IUnitReadRepository,
        IHsCodeReadRepository
    {
        private const int DatabaseIdBatchSize = 400;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public LocalMasterDataReadRepository(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<IReadOnlyList<Customer>> QueryAsync(CustomerReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            return await context.Customers
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(
                    keyword,
                    customer => customer.CustomerNameEN,
                    customer => customer.NotifyPartyName,
                    customer => customer.ContactPerson,
                    customer => customer.Phone,
                    customer => customer.Email,
                    customer => customer.TaxId)
                .OrderBy(customer => customer.CustomerNameEN)
                .ThenBy(customer => customer.NotifyPartyName)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Exporter>> QueryAsync(ExporterReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            return await context.Exporters
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(
                    keyword,
                    exporter => exporter.ExporterNameEN,
                    exporter => exporter.ExporterNameCN,
                    exporter => exporter.ContactPerson,
                    exporter => exporter.CreditCode,
                    exporter => exporter.CustomsCode,
                    exporter => exporter.Phone,
                    exporter => exporter.BankName)
                .OrderBy(exporter => exporter.ExporterNameEN)
                .ThenBy(exporter => exporter.ExporterNameCN)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Payee>> QueryAsync(PayeeReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            return await context.Payees
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(
                    keyword,
                    payee => payee.Category,
                    payee => payee.Name,
                    payee => payee.BankName,
                    payee => payee.RMBAccount,
                    payee => payee.USDAccount,
                    payee => payee.ContactPerson,
                    payee => payee.Phone,
                    payee => payee.Notes)
                .OrderBy(payee => payee.Category)
                .ThenBy(payee => payee.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Product>> QueryAsync(ProductReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            IQueryable<Product> productQuery = context.Products
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(
                    keyword,
                    product => product.ProductCode,
                    product => product.NameEN,
                    product => product.NameCN,
                    product => product.HSCode,
                    product => product.Material,
                    product => product.Brand)
                .OrderBy(product => product.ProductCode)
                .ThenBy(product => product.NameEN)
                .ThenByDescending(product => product.UpdatedAt);

            if (query?.ReturnAll != true)
            {
                productQuery = productQuery.Take(Math.Clamp(query?.MaxCount ?? 200, 1, 500));
            }

            return await productQuery.ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<Product>> QueryPageAsync(ProductReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = query ?? new ProductReadQuery();
            int pageNumber = Math.Max(1, normalizedQuery.PageNumber);
            int pageSize = Math.Clamp(normalizedQuery.PageSize <= 0 ? 50 : normalizedQuery.PageSize, 1, 200);
            var keyword = TextSearchHelper.NormalizeFilter(normalizedQuery.Keyword);
            var productQuery = context.Products
                .AsNoTracking()
                .ApplyKeywordSearch(
                    keyword,
                    product => product.ProductCode,
                    product => product.NameEN,
                    product => product.NameCN,
                    product => product.HSCode,
                    product => product.Material,
                    product => product.Brand)
                .OrderBy(product => product.ProductCode)
                .ThenBy(product => product.NameEN)
                .ThenByDescending(product => product.UpdatedAt)
                .ThenBy(product => product.Id);

            int totalCount = await productQuery.CountAsync(cancellationToken);
            var items = await productQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return new PagedResult<Product>(items, totalCount, pageNumber, pageSize);
        }

        public async Task<IReadOnlyList<Port>> QueryAsync(PortReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            return await context.Ports
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(keyword, port => port.NameEN, port => port.NameCN, port => port.Country, port => port.Code)
                .OrderBy(port => port.NameEN)
                .ThenBy(port => port.NameCN)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Unit>> QueryAsync(UnitReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            return await context.Units
                .AsNoTracking()
                .AsQueryable()
                .ApplyKeywordSearch(keyword, unit => unit.NameEN, unit => unit.NameCN, unit => unit.Code)
                .OrderBy(unit => unit.NameEN)
                .ThenBy(unit => unit.NameCN)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<HsCode>> QueryAsync(HsCodeReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = query ?? new HsCodeReadQuery();
            var hsCodeQuery = BuildHsCodeQuery(context, normalizedQuery);

            if (!normalizedQuery.ReturnAll)
            {
                hsCodeQuery = hsCodeQuery.Take(Math.Max(1, normalizedQuery.MaxCount));
            }

            return await hsCodeQuery.ToListAsync(cancellationToken);
        }

        public async Task<PagedResult<HsCode>> QueryPageAsync(HsCodeReadQuery query, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var normalizedQuery = query ?? new HsCodeReadQuery();
            var normalizedPageNumber = Math.Max(1, normalizedQuery.PageNumber);
            var normalizedPageSize = Math.Clamp(normalizedQuery.PageSize <= 0 ? 50 : normalizedQuery.PageSize, 1, 200);
            var hsCodeQuery = BuildHsCodeQuery(context, normalizedQuery);

            var totalCount = await hsCodeQuery.CountAsync(cancellationToken);
            var items = await hsCodeQuery
                .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<HsCode>(items, totalCount, normalizedPageNumber, normalizedPageSize);
        }

        public async Task<HsCode> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            var normalizedCode = TextSearchHelper.NormalizeFilter(code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return null;
            }

            var normalizedCodeKey = HsCodeTextHelper.NormalizeCode(normalizedCode);
            if (string.IsNullOrWhiteSpace(normalizedCodeKey))
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.HsCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(hsCode => hsCode.NormalizedCode == normalizedCodeKey, cancellationToken);
        }

        public async Task<HsCode> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.HsCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<int>> FindExistingIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default)
        {
            var validIds = (ids ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
            if (validIds.Length == 0)
            {
                return Array.Empty<int>();
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var result = new List<int>(validIds.Length);
            foreach (var batch in validIds.Chunk(DatabaseIdBatchSize))
            {
                int[] batchIds = batch.ToArray();
                result.AddRange(await context.HsCodes
                    .AsNoTracking()
                    .Where(item => batchIds.Contains(item.Id))
                    .Select(item => item.Id)
                    .ToListAsync(cancellationToken));
            }

            return result;
        }

        private static IQueryable<HsCode> BuildHsCodeQuery(AppDbContext context, HsCodeReadQuery query)
        {
            var keyword = TextSearchHelper.NormalizeFilter(query?.Keyword);
            var normalizedCodeKeyword = HsCodeTextHelper.NormalizeCodeSearchKeyword(keyword);
            var hsCodeQuery = context.HsCodes.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                bool isNumericCodePrefix = !string.IsNullOrWhiteSpace(normalizedCodeKeyword) &&
                    normalizedCodeKeyword.All(char.IsDigit);
                hsCodeQuery = isNumericCodePrefix
                    ? hsCodeQuery.Where(hsCode => hsCode.NormalizedCode.StartsWith(normalizedCodeKeyword))
                    : hsCodeQuery.Where(hsCode =>
                        hsCode.Code.Contains(keyword) ||
                        hsCode.Name.Contains(keyword) ||
                        (hsCode.Description != null && hsCode.Description.Contains(keyword)));
            }

            return hsCodeQuery.OrderByDescending(hsCode => hsCode.UpdateTime).ThenBy(hsCode => hsCode.Code);
        }
    }
}
