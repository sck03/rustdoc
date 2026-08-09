using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        public async Task<HsCodeHistoryCandidatePage> DiscoverHistoryCandidatesAsync(
            string keyword, int pageNumber = 1, int pageSize = 30, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);
            string rawFilter = (keyword ?? string.Empty).Trim();
            if (rawFilter.Length > MaximumHistoryKeywordLength)
                throw new ArgumentException($"历史资料筛选条件不能超过 {MaximumHistoryKeywordLength} 个字符。", nameof(keyword));
            string filter = NormalizeSearchText(rawFilter);
            int sourceLimit = string.IsNullOrWhiteSpace(rawFilter)
                ? HistoryRecentSourceLimit
                : HistoryKeywordSourceLimit;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Read only the columns needed by the learning screen. The previous implementation
            // loaded complete entities from all three history sources before filtering in memory.
            // Each source is now filtered and bounded in SQL; the extra row is used only to tell
            // the UI that a narrower keyword is needed for a complete review window.
            var rows = new List<HistorySourceRow>();

            var productRows = await ReadProductHistoryRowsAsync(context, rawFilter, sourceLimit, cancellationToken);
            var itemRows = await ReadInvoiceHistoryRowsAsync(context, rawFilter, sourceLimit, cancellationToken);
            var customsRows = await ReadCustomsHistoryRowsAsync(context, rawFilter, sourceLimit, cancellationToken);
            rows.AddRange(productRows.Rows.Select(ToHistorySourceRow));
            rows.AddRange(itemRows.Rows.Select(ToHistorySourceRow));
            rows.AddRange(customsRows.Rows.Select(ToHistorySourceRow));

            var groupedRows = rows.Where(item => !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item with
                {
                    Code = HsCodeTextHelper.NormalizeCode(item.Code),
                    Name = NormalizeHistoryProductName(item.Name.Trim()),
                    Specification = (item.Specification ?? string.Empty).Trim()
                })
                .Where(item => string.IsNullOrWhiteSpace(filter) ||
                    NormalizeSearchText($"{item.Name} {item.Specification} {item.Code}").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => new
                {
                    item.Code,
                    Name = NormalizeSearchText(item.Name),
                    Specification = NormalizeSearchText(item.Specification)
                })
                .Select(group =>
                {
                    var first = group.First();
                    var variants = group.Select(item => (item.Variant ?? string.Empty).Trim())
                        .Where(item => item.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    string fingerprint = BuildFingerprint(first.Code, first.Name, first.Specification);
                    return new HistoryCandidateGroup(
                        fingerprint,
                        first.Code,
                        first.Name,
                        first.Specification,
                        string.Join("、", group.Select(item => item.Source).Distinct()),
                        group.Count(),
                        variants.Count,
                        variants.Take(5).ToList());
                })
                .ToList();

            // Resolve only codes actually present in the bounded candidate set. This keeps the
            // formal tax table and replacement graph out of the history page's hot path.
            var rawCodes = groupedRows.Select(item => item.RawCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var relations = await LoadReplacementRelationsAsync(context, rawCodes, cancellationToken);
            var lookupCodes = rawCodes
                .Concat(relations.Select(item => item.NewCode))
                .Select(HsCodeTextHelper.NormalizeCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var codes = await LoadHsCodesByNormalizedCodesAsync(context, lookupCodes, cancellationToken);
            var codeMap = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode))
                .GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var known = await LoadKnownFingerprintsAsync(
                context,
                groupedRows.Select(item => item.Fingerprint),
                cancellationToken);

            var candidates = groupedRows
                .Where(item => !known.Contains(item.Fingerprint))
                .Select(item =>
                {
                    var resolution = ResolveCurrentCode(
                        new HsCodeDeclarationExample { RawReportedHsCode = item.RawCode },
                        codeMap,
                        relations);
                    return new HsCodeHistoryLearningCandidate(
                        item.Fingerprint,
                        item.RawCode,
                        resolution.CurrentCode ?? string.Empty,
                        item.ProductName,
                        item.Specification,
                        item.Source,
                        item.SourceCount,
                        item.VariantCount,
                        item.VariantSamples,
                        resolution.Status,
                        resolution.Replacements,
                        resolution.CanUse);
                })
                .OrderByDescending(item => item.CanConfirm).ThenByDescending(item => item.SourceCount).ThenBy(item => item.ProductName)
                .ToList();
            int totalCount = candidates.Count;
            var items = candidates.Skip(PagingHelper.CalculateOffset(pageNumber, pageSize)).Take(pageSize).ToList();
            bool isTruncated = productRows.HasMore || itemRows.HasMore || customsRows.HasMore;
            string notice = isTruncated
                ? $"历史资料量较大，本次按每类最多 {sourceLimit:N0} 条近期记录分析；请输入更具体的品名、款号或 HS 编码以缩小范围。"
                : string.Empty;
            return new HsCodeHistoryCandidatePage(items, totalCount, pageNumber, pageSize, isTruncated, rows.Count, notice);
        }

        private static async Task<HistorySourceReadResult> ReadProductHistoryRowsAsync(
            AppDbContext context,
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            var query = context.Products.AsNoTracking()
                .Where(item => item.HSCode != null && item.HSCode != "");
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string codePrefix = HsCodeTextHelper.NormalizeCodeSearchKeyword(keyword);
                query = !string.IsNullOrWhiteSpace(codePrefix) && codePrefix.All(char.IsDigit)
                    ? query.Where(item => item.HSCode.StartsWith(codePrefix))
                    : query.Where(item =>
                        (item.HSCode != null && item.HSCode.Contains(keyword)) ||
                        (item.ProductCode != null && item.ProductCode.Contains(keyword)) ||
                        (item.NameCN != null && item.NameCN.Contains(keyword)) ||
                        (item.NameEN != null && item.NameEN.Contains(keyword)) ||
                        (item.Material != null && item.Material.Contains(keyword)) ||
                        (item.Brand != null && item.Brand.Contains(keyword)));
            }

            var rows = await query
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id)
                .Take(limit + 1)
                .Select(item => new HistorySourceProjection(
                    item.HSCode,
                    item.NameCN,
                    item.NameEN,
                    item.Material,
                    item.Brand,
                    item.Elements,
                    item.Description,
                    "商品主数据",
                    string.Empty))
                .ToListAsync(cancellationToken);
            return TrimHistoryRows(rows, limit);
        }

        private async Task<HistorySourceReadResult> ReadInvoiceHistoryRowsAsync(
            AppDbContext context,
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            IQueryable<Item> query = context.Items.AsNoTracking()
                .Where(item => item.HSCode != null && item.HSCode != "");
            if (_businessDataAccessScope.ShouldFilterBusinessData())
            {
                var scopedInvoices = _businessDataAccessScope.ApplyInvoiceScope(context.Invoices.AsNoTracking());
                query = query.Where(item => scopedInvoices.Any(invoice => invoice.Id == item.InvoiceId));
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string codePrefix = HsCodeTextHelper.NormalizeCodeSearchKeyword(keyword);
                query = !string.IsNullOrWhiteSpace(codePrefix) && codePrefix.All(char.IsDigit)
                    ? query.Where(item => item.HSCode.StartsWith(codePrefix))
                    : query.Where(item =>
                        (item.HSCode != null && item.HSCode.Contains(keyword)) ||
                        (item.StyleNo != null && item.StyleNo.Contains(keyword)) ||
                        (item.StyleNameCN != null && item.StyleNameCN.Contains(keyword)) ||
                        (item.StyleName != null && item.StyleName.Contains(keyword)) ||
                        (item.FabricComposition != null && item.FabricComposition.Contains(keyword)) ||
                        (item.Brand != null && item.Brand.Contains(keyword)));
            }

            var rows = await query
                .OrderByDescending(item => item.InvoiceId)
                .ThenByDescending(item => item.Id)
                .Take(limit + 1)
                .Select(item => new HistorySourceProjection(
                    item.HSCode,
                    item.StyleNameCN,
                    item.StyleName,
                    item.FabricComposition,
                    item.Brand,
                    string.Empty,
                    string.Empty,
                    "历史商业发票",
                    item.StyleNo))
                .ToListAsync(cancellationToken);
            return TrimHistoryRows(rows, limit);
        }

        private async Task<HistorySourceReadResult> ReadCustomsHistoryRowsAsync(
            AppDbContext context,
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            IQueryable<CustomsCooItem> query = context.CustomsCooItems.AsNoTracking()
                .Where(item => item.HSCode != null && item.HSCode != "");
            if (_businessDataAccessScope.ShouldFilterBusinessData())
            {
                var scopedInvoices = _businessDataAccessScope.ApplyInvoiceScope(context.Invoices.AsNoTracking());
                query = from item in query
                        join document in context.CustomsCooDocuments.AsNoTracking()
                            on item.DocumentId equals document.Id
                        join invoice in scopedInvoices
                            on document.SourceInvoiceId equals invoice.Id
                        select item;
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string codePrefix = HsCodeTextHelper.NormalizeCodeSearchKeyword(keyword);
                query = !string.IsNullOrWhiteSpace(codePrefix) && codePrefix.All(char.IsDigit)
                    ? query.Where(item => item.HSCode.StartsWith(codePrefix))
                    : query.Where(item =>
                        item.HSCode.Contains(keyword) ||
                        item.SourceStyleNo.Contains(keyword) ||
                        item.GoodsName.Contains(keyword) ||
                        item.GoodsNameE.Contains(keyword) ||
                        item.GoodsDesc.Contains(keyword));
            }

            var rows = await query
                .OrderByDescending(item => item.Id)
                .Take(limit + 1)
                .Select(item => new HistorySourceProjection(
                    item.HSCode,
                    item.GoodsName,
                    item.GoodsNameE,
                    item.GoodsDesc,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "历史报关资料",
                    item.SourceStyleNo))
                .ToListAsync(cancellationToken);
            return TrimHistoryRows(rows, limit);
        }

        private static HistorySourceReadResult TrimHistoryRows(
            List<HistorySourceProjection> rows,
            int limit)
        {
            bool hasMore = rows.Count > limit;
            if (hasMore)
                rows.RemoveRange(limit, rows.Count - limit);
            return new HistorySourceReadResult(rows, hasMore);
        }

        private static HistorySourceRow ToHistorySourceRow(HistorySourceProjection row) =>
            new(
                row.Code,
                Prefer(row.NamePrimary, row.NameFallback),
                JoinHistorySpecification(
                    row.SpecificationOne,
                    row.SpecificationTwo,
                    row.SpecificationThree,
                    row.SpecificationFour),
                row.Source,
                row.Variant);

    }
}
