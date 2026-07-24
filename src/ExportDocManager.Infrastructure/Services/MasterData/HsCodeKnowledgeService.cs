using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed class HsCodeKnowledgeService : IHsCodeKnowledgeService
    {
        private const string PackageSchemaVersion = "1.0";
        private const long MaximumPackageBytes = 100L * 1024 * 1024;
        private const int SearchExampleCandidateLimit = 2000;
        private const int SearchMasterCandidateLimit = 1000;
        private const int DatabaseInClauseBatchSize = 400;
        private const int KnowledgeResolutionBatchSize = 500;
        private const long MaximumKnowledgeEntryBytes = 100L * 1024L * 1024L;
        private const long MaximumKnowledgeExpandedBytes = 300L * 1024L * 1024L;
        // History discovery is an interactive review screen, not an export job. Keep each
        // request bounded so a growing invoice archive cannot turn one page load into a
        // full-database materialization. Users can narrow the window with a keyword.
        private const int HistoryRecentSourceLimit = 2500;
        private const int HistoryKeywordSourceLimit = 5000;
        private const int MaximumHistoryKeywordLength = 200;
        private const int MaximumKnowledgeQueryLength = 500;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
        private static readonly IReadOnlyDictionary<string, string> Synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["T-SHIRT"] = "T恤衫", ["TSHIRT"] = "T恤衫", ["T恤"] = "T恤衫",
            ["男士"] = "男式", ["男款"] = "男式", ["MENS"] = "男式", ["MEN'S"] = "男式",
            ["女士"] = "女式", ["女款"] = "女式", ["WOMENS"] = "女式", ["WOMEN'S"] = "女式",
            ["全棉"] = "100%棉", ["纯棉"] = "100%棉", ["COTTON"] = "棉",
            ["针织物"] = "针织", ["KNITTED"] = "针织"
        };
        private static readonly IReadOnlyDictionary<string, string> RelatedTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["针织"] = "钩编", ["钩编"] = "针织"
        };

        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public HsCodeKnowledgeService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(new DatabaseConnectionSettings());
        }

        public async Task<HsCodeKnowledgeSearchResponse> SearchAsync(
            string query,
            int maxResults = 20,
            CancellationToken cancellationToken = default)
        {
            string rawQuery = (query ?? string.Empty).Trim();
            if (rawQuery.Length > MaximumKnowledgeQueryLength)
                throw new ArgumentException($"查询条件不能超过 {MaximumKnowledgeQueryLength} 个字符。", nameof(query));
            if (string.IsNullOrWhiteSpace(rawQuery))
                return new HsCodeKnowledgeSearchResponse(string.Empty, [], 0, "请输入商品名称、材质、用途、规格或至少4位HS编码。");
            string normalizedQuery = NormalizeSearchText(rawQuery);
            string normalizedCodePrefix = HsCodeTextHelper.NormalizeCodeSearchKeyword(rawQuery);
            maxResults = Math.Clamp(maxResults, 1, 50);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(normalizedCodePrefix) && normalizedCodePrefix.All(char.IsDigit))
                return await SearchByCodePrefixAsync(context, rawQuery, normalizedCodePrefix, maxResults, cancellationToken);
            string primaryToken = BuildNgrams(normalizedQuery).OrderByDescending(token => token.Length).FirstOrDefault() ?? normalizedQuery;
            var relatedPair = RelatedTerms.FirstOrDefault(pair => normalizedQuery.Contains(pair.Key, StringComparison.OrdinalIgnoreCase));
            string relatedToken = string.IsNullOrWhiteSpace(relatedPair.Key) ? string.Empty : NormalizeSearchText(relatedPair.Value);
            var exampleQuery = context.HsCodeDeclarationExamples.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(primaryToken))
                exampleQuery = string.IsNullOrWhiteSpace(relatedToken)
                    ? exampleQuery.Where(item => item.SearchText.Contains(primaryToken))
                    : exampleQuery.Where(item => item.SearchText.Contains(primaryToken) || item.SearchText.Contains(relatedToken));
            var examples = await exampleQuery.OrderByDescending(item => item.IsManuallyVerified)
                .ThenByDescending(item => item.UseCount).ThenByDescending(item => item.UpdatedAt)
                .Take(SearchExampleCandidateLimit).ToListAsync(cancellationToken);
            if (examples.Count == 0)
            {
                examples = await context.HsCodeDeclarationExamples.AsNoTracking()
                    .OrderByDescending(item => item.IsManuallyVerified).ThenByDescending(item => item.UpdatedAt)
                    .Take(SearchExampleCandidateLimit).ToListAsync(cancellationToken);
            }

            var rawCodes = examples
                .SelectMany(item => new[] { item.RawReportedHsCode, item.ResolvedCurrentHsCode })
                .Select(HsCodeTextHelper.NormalizeCode)
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
            var masterCandidates = await BuildMasterCandidateQuery(context, primaryToken, relatedToken)
                .Take(SearchMasterCandidateLimit)
                .ToListAsync(cancellationToken);
            codes.AddRange(masterCandidates.Where(candidate => codes.All(code => code.Id != candidate.Id)));
            var codeMap = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode))
                .GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var feedback = await LoadFeedbackByCandidateCodesAsync(context, lookupCodes, cancellationToken);
            var feedbackBoosts = feedback
                .Where(item => string.Equals(NormalizeSearchText(item.QueryText), normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => HsCodeTextHelper.NormalizeCode(item.CandidateCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.AcceptedCount * 5 - item.RejectedCount * 4),
                    StringComparer.OrdinalIgnoreCase);
            var candidates = new List<KnowledgeCandidate>();
            foreach (var example in examples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int nameScore = ScoreText(normalizedQuery, NormalizeSearchText(example.ProductName));
                int specificationScore = ScoreText(normalizedQuery, NormalizeSearchText(example.Specification));
                int combinedScore = ScoreText(normalizedQuery, example.SearchText);
                var assessment = AssessAttributes(normalizedQuery, NormalizeSearchText($"{example.ProductName} {example.Specification}"));
                int textScore = Math.Max(combinedScore, (int)Math.Round(nameScore * 0.72d + specificationScore * 0.28d)) - assessment.Penalty;
                if (textScore < 18) continue;
                var resolution = ResolveCurrentCode(example, codeMap, relations);
                string feedbackCode = HsCodeTextHelper.NormalizeCode(resolution.CurrentCode ?? example.RawReportedHsCode);
                int feedbackBoost = feedbackBoosts.GetValueOrDefault(feedbackCode);
                int score = Math.Clamp(textScore + Math.Min(example.UseCount * 2, 15) +
                    (example.IsManuallyVerified ? 15 : 0) + feedbackBoost, 0, 100);
                candidates.Add(new KnowledgeCandidate(example, resolution, score, assessment.MatchReasons, assessment.ConflictWarnings));
            }

            var grouped = candidates
                .GroupBy(item => item.Resolution.CurrentCode ?? $"obsolete:{item.Example.RawReportedHsCode}", StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var best = group.OrderByDescending(item => item.Score).First();
                    string currentCode = best.Resolution.CurrentCode;
                    codeMap.TryGetValue(currentCode ?? string.Empty, out var standard);
                    return new HsCodeKnowledgeSearchResult(
                        currentCode ?? string.Empty,
                        best.Example.RawReportedHsCode,
                        best.Example.ProductName,
                        best.Example.Specification ?? string.Empty,
                        standard?.Name ?? string.Empty,
                        best.Resolution.Status,
                        Math.Min(100, best.Score + Math.Min(group.Count() - 1, 5)),
                        group.Count(),
                        group.Sum(item => item.Example.UseCount),
                        best.Resolution.Replacements,
                        best.MatchReasons,
                        best.ConflictWarnings,
                        standard?.SourceName ?? string.Empty,
                        standard?.EffectiveYear,
                        standard?.LastVerifiedAt,
                        best.Resolution.CanUse && !string.IsNullOrWhiteSpace(currentCode) && HsCodeValidityPolicy.IsTrustedActive(standard));
                })
                .OrderByDescending(item => item.CanUse)
                .ThenByDescending(item => item.Score)
                .Take(maxResults)
                .ToList();

            if (grouped.Count < maxResults)
            {
                var existing = grouped.Select(item => item.CurrentCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var masterFallback = masterCandidates
                    .Where(item => HsCodeValidityPolicy.IsTrustedActive(item) && !existing.Contains(item.NormalizedCode))
                    .Select(item =>
                    {
                        var assessment = AssessAttributes(normalizedQuery, NormalizeSearchText($"{item.Name} {item.Elements} {item.Description}"));
                        return new
                        {
                            Item = item,
                            Score = ScoreText(normalizedQuery, NormalizeSearchText($"{item.Name} {item.Elements} {item.Description}")) - assessment.Penalty,
                            Assessment = assessment
                        };
                    })
                    .Where(item => item.Score >= 22)
                    .OrderByDescending(item => item.Score)
                    .Take(maxResults - grouped.Count)
                    .Select(item => new HsCodeKnowledgeSearchResult(
                        item.Item.NormalizedCode, item.Item.NormalizedCode, item.Item.Name, string.Empty, item.Item.Name,
                        "Active", item.Score, 0, 0, [], item.Assessment.MatchReasons, item.Assessment.ConflictWarnings,
                        item.Item.SourceName ?? string.Empty, item.Item.EffectiveYear, item.Item.LastVerifiedAt, true));
                grouped.AddRange(masterFallback);
            }

            string message = grouped.Count == 0
                ? "本地知识库暂未找到匹配结果，可使用联网补充并保存申报实例。"
                : $"本地找到 {grouped.Count} 个候选；优先展示当前有效且经过使用确认的编码。";
            return new HsCodeKnowledgeSearchResponse(rawQuery, grouped, examples.Count, message);
        }

        private static async Task<HsCodeKnowledgeSearchResponse> SearchByCodePrefixAsync(
            AppDbContext context,
            string rawQuery,
            string codePrefix,
            int maxResults,
            CancellationToken cancellationToken)
        {
            var codes = await context.HsCodes.AsNoTracking()
                .Where(item => item.Status == HsCodeValidityPolicy.ActiveStatus &&
                    item.SourceName != null && item.SourceName != "" &&
                    item.EffectiveYear != null && item.LastVerifiedAt != null &&
                    item.NormalizedCode.StartsWith(codePrefix))
                .OrderByDescending(item => item.EffectiveYear)
                .ThenByDescending(item => item.LastVerifiedAt)
                .ThenBy(item => item.NormalizedCode)
                .Take(maxResults)
                .ToListAsync(cancellationToken);
            var examples = await context.HsCodeDeclarationExamples.AsNoTracking()
                .Where(item => item.RawReportedHsCode.StartsWith(codePrefix) ||
                    (item.ResolvedCurrentHsCode != null && item.ResolvedCurrentHsCode.StartsWith(codePrefix)))
                .OrderByDescending(item => item.IsManuallyVerified)
                .ThenByDescending(item => item.UseCount)
                .Take(SearchExampleCandidateLimit)
                .ToListAsync(cancellationToken);

            var items = codes.Select(code =>
            {
                var relatedExamples = examples.Where(example =>
                    string.Equals(HsCodeTextHelper.NormalizeCode(example.ResolvedCurrentHsCode), code.NormalizedCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(HsCodeTextHelper.NormalizeCode(example.RawReportedHsCode), code.NormalizedCode, StringComparison.OrdinalIgnoreCase)).ToList();
                var bestExample = relatedExamples.FirstOrDefault();
                int score = string.Equals(code.NormalizedCode, codePrefix, StringComparison.OrdinalIgnoreCase)
                    ? 100
                    : Math.Min(96, 72 + codePrefix.Length * 3);
                string specification = bestExample?.Specification?.Trim();
                if (string.IsNullOrWhiteSpace(specification))
                    specification = !string.IsNullOrWhiteSpace(code.Elements) ? code.Elements.Trim() : code.Description?.Trim() ?? string.Empty;
                return new HsCodeKnowledgeSearchResult(
                    code.NormalizedCode,
                    bestExample?.RawReportedHsCode ?? code.NormalizedCode,
                    bestExample?.ProductName ?? code.Name,
                    specification,
                    code.Name,
                    HsCodeValidityPolicy.ActiveStatus,
                    score,
                    relatedExamples.Count,
                    relatedExamples.Sum(item => item.UseCount),
                    [],
                    [$"HS编码前缀匹配：{codePrefix}"],
                    [],
                    code.SourceName,
                    code.EffectiveYear,
                    code.LastVerifiedAt,
                    true);
            }).ToList();

            string message = items.Count == 0
                ? $"本地当前有效税则中未找到以 {codePrefix} 开头的编码。"
                : $"按 HS 编码前缀 {codePrefix} 找到 {items.Count} 个当前有效候选。";
            return new HsCodeKnowledgeSearchResponse(rawQuery, items, examples.Count, message);
        }

        public async Task<IReadOnlyList<HsCodeDeclarationExample>> ListExamplesAsync(
            string keyword, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = BuildExampleQuery(context, keyword);
            return await query.OrderByDescending(item => item.IsManuallyVerified).ThenByDescending(item => item.UpdatedAt)
                .Skip((Math.Max(pageNumber, 1) - 1) * Math.Clamp(pageSize, 1, 200))
                .Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(cancellationToken);
        }

        public async Task<int> CountExamplesAsync(string keyword, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await BuildExampleQuery(context, keyword).CountAsync(cancellationToken);
        }

        public async Task<HsCodeDeclarationExample> SaveExampleAsync(HsCodeExampleInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string rawCode = HsCodeTextHelper.NormalizeCode(input.RawReportedHsCode);
            string currentCode = HsCodeTextHelper.NormalizeCode(input.ResolvedCurrentHsCode);
            string name = ValidateTextLength(input.ProductName, 300, "商品名称");
            string specification = ValidateTextLength(input.Specification, 1500, "规格与申报要素");
            string source = ValidateTextLength(input.Source, 100, "实例来源");
            if (string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("申报实例必须填写历史/原始HS编码和商品名称。");
            if (rawCode.Length > 20 || currentCode.Length > 20)
                throw new ArgumentException("HS 编码不能超过 20 个字符。");
            string fingerprint = BuildFingerprint(rawCode, name, specification);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(currentCode) &&
                !await HasTrustedActiveCodeAsync(context, currentCode, cancellationToken))
                throw new InvalidOperationException("当前有效编码必须来自已验证的本地年度税则，并包含来源、年度和验证时间。");
            var entity = input.Id > 0
                ? await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken)
                : await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (entity == null)
            {
                entity = new HsCodeDeclarationExample { CreatedAt = now };
                await context.HsCodeDeclarationExamples.AddAsync(entity, cancellationToken);
            }
            entity.Fingerprint = fingerprint;
            entity.RawReportedHsCode = rawCode;
            entity.ResolvedCurrentHsCode = string.IsNullOrWhiteSpace(currentCode) ? null : currentCode;
            entity.ProductName = name;
            entity.Specification = specification;
            entity.SearchText = NormalizeSearchText($"{name} {specification}");
            entity.Source = string.IsNullOrWhiteSpace(source) ? "Manual" : source;
            entity.SourceYear = input.SourceYear;
            entity.ResolutionStatus = NormalizeResolutionStatus(input.ResolutionStatus, currentCode, rawCode);
            entity.IsManuallyVerified = input.IsManuallyVerified;
            entity.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public async Task<bool> DeleteExampleAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.HsCodeDeclarationExamples.FindAsync([id], cancellationToken);
            if (entity == null) return false;
            context.HsCodeDeclarationExamples.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<int> DeleteExamplesAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            var normalizedIds = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
            if (normalizedIds.Count == 0) return 0;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entities = await context.HsCodeDeclarationExamples
                .Where(item => normalizedIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
            if (entities.Count == 0) return 0;
            context.HsCodeDeclarationExamples.RemoveRange(entities);
            await context.SaveChangesAsync(cancellationToken);
            return entities.Count;
        }

        public async Task RecordFeedbackAsync(HsCodeKnowledgeFeedbackInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            string queryText = ValidateTextLength(input.QueryText, MaximumKnowledgeQueryLength, "查询条件");
            string productName = ValidateTextLength(input.ProductName, 300, "商品名称");
            string specification = ValidateTextLength(input.Specification, 1500, "规格与申报要素");
            string code = HsCodeTextHelper.NormalizeCode(input.CandidateCode);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("确认结果必须包含HS编码。");
            if (code.Length > 20) throw new ArgumentException("HS 编码不能超过 20 个字符。", nameof(input));
            string fingerprint = BuildFingerprint(NormalizeSearchText(queryText), code, productName, specification);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (input.Accepted &&
                !await HasTrustedActiveCodeAsync(context, code, cancellationToken))
                throw new InvalidOperationException("确认适用前必须选择已验证年度税则中的当前有效编码。");
            var entity = await context.HsCodeSearchFeedback.FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (entity == null)
            {
                entity = new HsCodeSearchFeedback { Fingerprint = fingerprint };
                await context.HsCodeSearchFeedback.AddAsync(entity, cancellationToken);
            }
            entity.QueryText = queryText;
            entity.ProductName = productName;
            entity.Specification = specification;
            entity.CandidateCode = code;
            if (input.Accepted) { entity.AcceptedCount++; entity.LastConfirmedAt = now; }
            else entity.RejectedCount++;
            entity.UpdatedAt = now;
            if (input.Accepted)
            {
                var exampleInput = new HsCodeExampleInput(0, code, code,
                    string.IsNullOrWhiteSpace(productName) ? queryText : productName,
                    specification, "UserConfirmed", DateTime.Now.Year, "ManuallyVerified", true);
                await UpsertExampleInContextAsync(context, exampleInput, now, cancellationToken, incrementUseCount: false);
            }
            await context.SaveChangesAsync(cancellationToken);
        }

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
            var items = candidates.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
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

        public async Task<int> CaptureRemoteExamplesAsync(
            string query, IEnumerable<HsCode> remoteRows, CancellationToken cancellationToken = default)
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            var records = (remoteRows ?? Enumerable.Empty<HsCode>())
                .Where(item => item != null)
                .Select(item => new HsCodeRemoteSearchRecord(
                    item,
                    HsCodeRemoteRecordKind.DeclarationExample,
                    false,
                    null,
                    string.Empty,
                    item.DetailUrl ?? string.Empty,
                    observedAt))
                .ToList();
            return await CaptureRemoteRecordsAsync(query, "i5a6", records, [], cancellationToken);
        }

        public Task<int> CaptureRemoteEvidenceAsync(
            string query,
            HsCodeRemoteSearchBundle bundle,
            CancellationToken cancellationToken = default)
        {
            if (bundle == null) return Task.FromResult(0);
            return CaptureRemoteRecordsAsync(
                query,
                bundle.Source,
                bundle.Records.Where(record => record.Kind == HsCodeRemoteRecordKind.DeclarationExample),
                bundle.ReplacementEvidence,
                cancellationToken);
        }

        public Task<int> CaptureRemoteDetailEvidenceAsync(
            string query,
            HsCodeRemoteDetailBundle bundle,
            CancellationToken cancellationToken = default)
        {
            if (bundle == null) return Task.FromResult(0);
            IReadOnlyList<HsCodeRemoteReplacementEvidence> replacementEvidence =
                bundle.RecommendedKeywords.Count == 0
                    ? []
                    : [new HsCodeRemoteReplacementEvidence(
                        HsCodeTextHelper.NormalizeCode(bundle.Item?.Code),
                        bundle.RecommendedKeywords,
                        bundle.EvidenceUrl,
                        bundle.ObservedAt)];
            return CaptureRemoteRecordsAsync(
                query,
                "i5a6",
                bundle.DeclarationExamples,
                replacementEvidence,
                cancellationToken);
        }

        private async Task<int> CaptureRemoteRecordsAsync(
            string query,
            string source,
            IEnumerable<HsCodeRemoteSearchRecord> remoteRecords,
            IReadOnlyList<HsCodeRemoteReplacementEvidence> replacementEvidence,
            CancellationToken cancellationToken)
        {
            var examples = (remoteRecords ?? Enumerable.Empty<HsCodeRemoteSearchRecord>())
                .Where(record => record?.Kind == HsCodeRemoteRecordKind.DeclarationExample &&
                    record.Item != null && !string.IsNullOrWhiteSpace(record.Item.Code) &&
                    !string.IsNullOrWhiteSpace(record.Item.Name))
                .ToList();
            if (examples.Count == 0) return 0;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rawCodes = examples
                .Select(record => HsCodeTextHelper.NormalizeCode(record.Item.Code))
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
            var recommendedPrefixes = (replacementEvidence ?? [])
                .SelectMany(item => item.RecommendedKeywords ?? [])
                .Select(HsCodeTextHelper.NormalizeCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            codes.AddRange((await LoadTrustedHsCodesByPrefixesAsync(context, recommendedPrefixes, cancellationToken))
                .Where(candidate => codes.All(code => code.Id != candidate.Id)));
            var codeMap = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode)).GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var fingerprints = examples
                .Select(record => BuildFingerprint(
                    HsCodeTextHelper.NormalizeCode(record.Item.Code),
                    record.Item.Name,
                    record.Item.Description))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existingCandidates = await LoadRemoteCandidatesByFingerprintsAsync(context, fingerprints, cancellationToken);
            var candidatesByFingerprint = existingCandidates.ToDictionary(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;
            int added = 0;
            foreach (var record in examples)
            {
                var item = record.Item;
                string code = HsCodeTextHelper.NormalizeCode(item.Code);
                string fingerprint = BuildFingerprint(code, item.Name, item.Description);
                if (!candidatesByFingerprint.TryGetValue(fingerprint, out var existing))
                {
                    existing = new HsCodeRemoteCandidate
                    {
                        Fingerprint = fingerprint, QueryText = (query ?? string.Empty).Trim(), RawReportedHsCode = code,
                        ProductName = item.Name.Trim(), Specification = (item.Description ?? string.Empty).Trim(),
                        Source = string.IsNullOrWhiteSpace(source) ? "i5a6" : source.Trim(),
                        SourceUrl = string.IsNullOrWhiteSpace(record.EvidenceUrl) ? item.DetailUrl : record.EvidenceUrl,
                        ReviewStatus = "Pending", FirstSeenAt = now
                    };
                    await context.HsCodeRemoteCandidates.AddAsync(existing, cancellationToken);
                    candidatesByFingerprint[fingerprint] = existing;
                    added++;
                }
                else existing.SeenCount++;
                var resolution = ResolveCurrentCode(new HsCodeDeclarationExample { RawReportedHsCode = code }, codeMap, relations);
                if (string.IsNullOrWhiteSpace(resolution.CurrentCode) || !resolution.CanUse)
                {
                    var webResolution = ResolveRecommendedCurrentCode(code, replacementEvidence, codeMap);
                    if (!string.IsNullOrWhiteSpace(webResolution.CurrentCode) || webResolution.Replacements.Count > 0)
                        resolution = webResolution;
                }
                existing.QueryText = (query ?? string.Empty).Trim(); existing.LastSeenAt = now;
                existing.SuggestedCurrentHsCode = resolution.CurrentCode; existing.ResolutionStatus = resolution.Status;
            }
            await context.SaveChangesAsync(cancellationToken);
            return added;
        }

        public async Task<HsCodeRemoteCandidatePage> ListRemoteCandidatesAsync(
            string reviewStatus,
            string keyword,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            string status = string.IsNullOrWhiteSpace(reviewStatus) ? "Pending" : reviewStatus.Trim();
            string filter = (keyword ?? string.Empty).Trim();
            int page = Math.Max(pageNumber, 1);
            int size = Math.Clamp(pageSize, 1, 200);
            var query = context.HsCodeRemoteCandidates.AsNoTracking().Where(item => item.ReviewStatus == status);
            if (!string.IsNullOrWhiteSpace(filter))
            {
                string normalizedCodePrefix = HsCodeTextHelper.NormalizeCodeSearchKeyword(filter);
                bool isCodePrefix = !string.IsNullOrWhiteSpace(normalizedCodePrefix) && normalizedCodePrefix.All(char.IsDigit);
                query = isCodePrefix
                    ? query.Where(item => item.RawReportedHsCode.StartsWith(normalizedCodePrefix) ||
                        (item.SuggestedCurrentHsCode != null && item.SuggestedCurrentHsCode.StartsWith(normalizedCodePrefix)))
                    : query.Where(item => item.ProductName.Contains(filter) ||
                        (item.Specification != null && item.Specification.Contains(filter)) ||
                        item.QueryText.Contains(filter));
            }
            int totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.LastSeenAt)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync(cancellationToken);
            return new HsCodeRemoteCandidatePage(items, totalCount, page, size, status);
        }

        public async Task<bool> ReviewRemoteCandidateAsync(HsCodeRemoteCandidateReviewInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await ReviewRemoteCandidateInContextAsync(context, input, cancellationToken);
        }

        public async Task<int> ReviewRemoteCandidatesAsync(
            IReadOnlyList<HsCodeRemoteCandidateReviewInput> inputs,
            CancellationToken cancellationToken = default)
        {
            var normalized = (inputs ?? Array.Empty<HsCodeRemoteCandidateReviewInput>())
                .Where(item => item != null && item.Id > 0)
                .GroupBy(item => item.Id)
                .Select(group => group.Last())
                .Take(200)
                .ToList();
            if (normalized.Count == 0) return 0;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            int reviewed = 0;
            foreach (var input in normalized)
                if (await ReviewRemoteCandidateInContextAsync(context, input, cancellationToken, saveChanges: false)) reviewed++;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reviewed;
        }

        public async Task<int> ResetRemoteCandidatesAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default)
        {
            var normalizedIds = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().Take(500).ToList();
            if (normalizedIds.Count == 0) return 0;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var candidates = await context.HsCodeRemoteCandidates
                .Where(item => normalizedIds.Contains(item.Id) && item.ReviewStatus != "Pending")
                .ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
            {
                if (candidate.ReviewStatus == "Confirmed")
                {
                    string fingerprint = BuildFingerprint(candidate.RawReportedHsCode, candidate.ProductName, candidate.Specification);
                    var learnedExample = await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(
                        item => item.Fingerprint == fingerprint && item.Source.StartsWith("RemoteConfirmed:"),
                        cancellationToken);
                    if (learnedExample != null) context.HsCodeDeclarationExamples.Remove(learnedExample);
                }
                candidate.ReviewStatus = "Pending";
                candidate.ReviewedAt = null;
            }
            await context.SaveChangesAsync(cancellationToken);
            return candidates.Count;
        }

        private static async Task<bool> ReviewRemoteCandidateInContextAsync(
            AppDbContext context,
            HsCodeRemoteCandidateReviewInput input,
            CancellationToken cancellationToken,
            bool saveChanges = true)
        {
            var candidate = await context.HsCodeRemoteCandidates.FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken);
            if (candidate == null || candidate.ReviewStatus != "Pending") return false;
            DateTime now = DateTime.UtcNow;
            if (!input.Confirmed)
            {
                candidate.ReviewStatus = "Ignored";
                candidate.ReviewedAt = now;
                if (saveChanges) await context.SaveChangesAsync(cancellationToken);
                return true;
            }

            string currentCode = HsCodeTextHelper.NormalizeCode(input.CurrentCode);
            if (!await HasTrustedActiveCodeAsync(context, currentCode, cancellationToken))
                throw new InvalidOperationException("确认前必须选择已验证年度税则中的当前有效 HS 编码。");
            await UpsertExampleInContextAsync(context, new HsCodeExampleInput(
                0,
                candidate.RawReportedHsCode,
                currentCode,
                candidate.ProductName,
                candidate.Specification,
                $"RemoteConfirmed:{candidate.Source}",
                null,
                "ManuallyVerified",
                true), now, cancellationToken);
            candidate.SuggestedCurrentHsCode = currentCode;
            candidate.ReviewStatus = "Confirmed";
            candidate.ReviewedAt = now;
            if (saveChanges) await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static Task<bool> HasTrustedActiveCodeAsync(
            AppDbContext context,
            string normalizedCode,
            CancellationToken cancellationToken)
        {
            return context.HsCodes.AsNoTracking().AnyAsync(item =>
                item.NormalizedCode == normalizedCode &&
                item.Status == HsCodeValidityPolicy.ActiveStatus &&
                item.SourceName != null && item.SourceName != "" &&
                item.EffectiveYear >= 2000 && item.EffectiveYear <= 2100 &&
                item.LastVerifiedAt != null,
                cancellationToken);
        }

        public async Task RefreshReplacementRelationsAsync(HsCodeImportPreview preview, CancellationToken cancellationToken = default)
        {
            if (preview == null) return;
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            DateTime now = DateTime.UtcNow;
            foreach (var item in preview.Items.Where(item => item.ChangeType == "SuspectedObsolete"))
            {
                string oldCode = HsCodeTextHelper.NormalizeCode(item.Item?.Code);
                for (int index = 0; index < item.ReplacementCandidates.Count; index++)
                {
                    string newCode = HsCodeTextHelper.NormalizeCode(item.ReplacementCandidates[index]);
                    if (string.IsNullOrWhiteSpace(oldCode) || string.IsNullOrWhiteSpace(newCode)) continue;
                    bool exists = await context.HsCodeReplacementRelations.AnyAsync(row =>
                        row.OldCode == oldCode && row.NewCode == newCode && row.EffectiveYear == preview.EffectiveYear, cancellationToken);
                    if (!exists)
                    {
                        await context.HsCodeReplacementRelations.AddAsync(new HsCodeReplacementRelation
                        {
                            OldCode = oldCode, NewCode = newCode, EffectiveYear = preview.EffectiveYear,
                            Source = preview.SourceName, Confidence = Math.Max(50, 80 - index * 10),
                            IsManuallyVerified = false, CreatedAt = now, UpdatedAt = now
                        }, cancellationToken);
                    }
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await ResolveExamplesAsync(context, cancellationToken);
        }

        public async Task<byte[]> ExportPackageAsync(DateTimeOffset? since = null, CancellationToken cancellationToken = default)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            DateTime? sinceUtc = since?.UtcDateTime;

            using var output = new MemoryStream();
            await using (var boundedOutput = new MaximumLengthWriteStream(output, MaximumPackageBytes))
            {
                using var archive = new ZipArchive(boundedOutput, ZipArchiveMode.Create, leaveOpen: true);
                long expandedBytes = 0;
                var checksums = new Dictionary<string, string>(StringComparer.Ordinal);

                async Task WriteJsonEntryAsync<T>(string name, IQueryable<T> query)
                {
                    var zipEntry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    await using var entryStream = zipEntry.Open();
                    using var hashingStream = new HashingQuotaWriteStream(
                        entryStream,
                        MaximumKnowledgeEntryBytes,
                        MaximumKnowledgeExpandedBytes,
                        () => expandedBytes);
                    await JsonSerializer.SerializeAsync(
                        hashingStream,
                        query.AsAsyncEnumerable(),
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await hashingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    expandedBytes += hashingStream.BytesWritten;
                    checksums[name] = hashingStream.GetHashHex();
                }

                await WriteJsonEntryAsync(
                    "hs-codes.json",
                    context.HsCodes.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdateTime >= sinceUtc));
                await WriteJsonEntryAsync(
                    "declaration-examples.json",
                    context.HsCodeDeclarationExamples.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));
                await WriteJsonEntryAsync(
                    "replacement-relations.json",
                    context.HsCodeReplacementRelations.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));
                await WriteJsonEntryAsync(
                    "search-feedback.json",
                    context.HsCodeSearchFeedback.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc));

                var manifest = new KnowledgeManifest(
                    PackageSchemaVersion,
                    DateTimeOffset.UtcNow,
                    since,
                    checksums);
                byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await manifestStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
            }

            return output.ToArray();
        }

        public async Task<HsCodeKnowledgePackagePreview> PreviewPackageAsync(string packagePath, CancellationToken cancellationToken = default)
        {
            var info = new FileInfo(packagePath ?? string.Empty);
            if (!info.Exists) throw new FileNotFoundException("HS知识库文件不存在。", packagePath);
            if (info.Length <= 0 || info.Length > MaximumPackageBytes) throw new InvalidDataException("HS知识库文件为空或超过100MB限制。");
            using var archive = ZipFile.OpenRead(info.FullName);
            var knownNames = new HashSet<string>(["manifest.json", "hs-codes.json", "declaration-examples.json", "replacement-relations.json", "search-feedback.json"], StringComparer.OrdinalIgnoreCase);
            var packageEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();
            bool hasDuplicateEntry = packageEntries
                .GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (packageEntries.Count != knownNames.Count ||
                hasDuplicateEntry ||
                packageEntries.Any(entry => !knownNames.Contains(entry.FullName) || entry.Length > MaximumKnowledgeEntryBytes) ||
                packageEntries.Sum(entry => entry.Length) > MaximumKnowledgeExpandedBytes)
                throw new InvalidDataException("HS知识库包含未知或过大的文件。");
            byte[] manifestBytes = await ReadEntryAsync(archive, "manifest.json", cancellationToken);
            var manifest = JsonSerializer.Deserialize<KnowledgeManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("HS知识库清单无效。");
            if (!string.Equals(manifest.SchemaVersion, PackageSchemaVersion, StringComparison.Ordinal))
                throw new InvalidDataException($"不支持的HS知识库版本：{manifest.SchemaVersion}。");
            async Task<T> ReadAndVerifyAsync<T>(string name)
            {
                byte[] bytes = await ReadEntryAsync(archive, name, cancellationToken);
                if (!manifest.Checksums.TryGetValue(name, out string expected) || !string.Equals(expected, Sha256(bytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"HS知识库文件校验失败：{name}。");
                return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException($"HS知识库内容无效：{name}。");
            }
            var codes = await ReadAndVerifyAsync<List<HsCode>>("hs-codes.json");
            var examples = await ReadAndVerifyAsync<List<HsCodeDeclarationExample>>("declaration-examples.json");
            var replacements = await ReadAndVerifyAsync<List<HsCodeReplacementRelation>>("replacement-relations.json");
            var feedback = await ReadAndVerifyAsync<List<HsCodeSearchFeedback>>("search-feedback.json");
            ValidatePackageContent(codes, examples, replacements, feedback);
            return new HsCodeKnowledgePackagePreview(info.Name, manifest.SchemaVersion, manifest.ExportedAt,
                codes.Count, examples.Count, replacements.Count, feedback.Count,
                codes, examples, replacements, feedback,
                ["导入只合并HS知识库，不包含发票、客户、付款、账号、授权或其他业务数据。"]);
        }

        public async Task<HsCodeKnowledgeImportResult> ImportPackageAsync(
            HsCodeKnowledgePackagePreview preview, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            int addedCodes = 0, updatedCodes = 0, addedExamples = 0, updatedExamples = 0, addedRelations = 0, addedFeedback = 0;

            var preparedCodes = preview.HsCodes
                .Select(source => (Source: source, Code: HsCodeTextHelper.NormalizeCode(source.Code)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .ToList();
            var existingCodes = new Dictionary<string, HsCode>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in preparedCodes.Select(item => item.Code)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Chunk(DatabaseInClauseBatchSize))
            {
                string[] keys = batch.ToArray();
                var rows = await context.HsCodes
                    .Where(item => keys.Contains(item.NormalizedCode))
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    existingCodes.TryAdd(row.NormalizedCode, row);
                }
            }

            foreach (var (source, code) in preparedCodes)
            {
                source.Code = code;
                if (existingCodes.TryGetValue(code, out var target))
                {
                    MergeHsCode(source, target);
                    updatedCodes++;
                }
                else
                {
                    source.Id = 0;
                    source.RowVersion = null;
                    await context.HsCodes.AddAsync(source, cancellationToken);
                    existingCodes[code] = source;
                    addedCodes++;
                }
            }

            var preparedExamples = new List<(HsCodeDeclarationExample Source, string Fingerprint)>(preview.Examples.Count);
            foreach (var source in preview.Examples)
            {
                source.RawReportedHsCode = HsCodeTextHelper.NormalizeCode(source.RawReportedHsCode);
                source.ResolvedCurrentHsCode = HsCodeTextHelper.NormalizeCode(source.ResolvedCurrentHsCode);
                source.ProductName = source.ProductName.Trim();
                source.Specification = (source.Specification ?? string.Empty).Trim();
                source.SearchText = NormalizeSearchText($"{source.ProductName} {source.Specification}");
                source.Fingerprint = BuildFingerprint(source.RawReportedHsCode, source.ProductName, source.Specification);
                source.Source = string.IsNullOrWhiteSpace(source.Source) ? "KnowledgePackage" : source.Source.Trim();
                source.ResolutionStatus = string.IsNullOrWhiteSpace(source.ResolutionStatus)
                    ? "Unresolved"
                    : source.ResolutionStatus.Trim();
                preparedExamples.Add((source, source.Fingerprint));
            }

            var existingExamples = new Dictionary<string, HsCodeDeclarationExample>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in preparedExamples.Select(item => item.Fingerprint)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Chunk(DatabaseInClauseBatchSize))
            {
                string[] keys = batch.ToArray();
                var rows = await context.HsCodeDeclarationExamples
                    .Where(item => keys.Contains(item.Fingerprint))
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    existingExamples.TryAdd(row.Fingerprint, row);
                }
            }

            foreach (var (source, fingerprint) in preparedExamples)
            {
                if (existingExamples.TryGetValue(fingerprint, out var target))
                {
                    MergeExample(source, target);
                    updatedExamples++;
                }
                else
                {
                    source.Id = 0;
                    await context.HsCodeDeclarationExamples.AddAsync(source, cancellationToken);
                    existingExamples[fingerprint] = source;
                    addedExamples++;
                }
            }

            var preparedRelations = new List<(HsCodeReplacementRelation Source, ReplacementRelationKey Key)>(preview.Replacements.Count);
            foreach (var source in preview.Replacements)
            {
                source.OldCode = HsCodeTextHelper.NormalizeCode(source.OldCode);
                source.NewCode = HsCodeTextHelper.NormalizeCode(source.NewCode);
                source.Source = string.IsNullOrWhiteSpace(source.Source) ? "KnowledgePackage" : source.Source.Trim();
                preparedRelations.Add((source, new ReplacementRelationKey(
                    source.OldCode,
                    source.NewCode,
                    source.EffectiveYear)));
            }

            var existingRelations = new HashSet<ReplacementRelationKey>();
            foreach (var batch in preparedRelations.Select(item => item.Key.OldCode)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Chunk(DatabaseInClauseBatchSize))
            {
                string[] oldCodes = batch.ToArray();
                var rows = await context.HsCodeReplacementRelations
                    .AsNoTracking()
                    .Where(item => oldCodes.Contains(item.OldCode))
                    .Select(item => new { item.OldCode, item.NewCode, item.EffectiveYear })
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    existingRelations.Add(new ReplacementRelationKey(
                        row.OldCode,
                        row.NewCode,
                        row.EffectiveYear));
                }
            }

            foreach (var (source, key) in preparedRelations)
            {
                if (existingRelations.Add(key))
                {
                    source.Id = 0;
                    await context.HsCodeReplacementRelations.AddAsync(source, cancellationToken);
                    addedRelations++;
                }
            }

            var preparedFeedback = new List<(HsCodeSearchFeedback Source, string Fingerprint)>(preview.Feedback.Count);
            foreach (var source in preview.Feedback)
            {
                source.QueryText = (source.QueryText ?? string.Empty).Trim();
                source.ProductName = string.IsNullOrWhiteSpace(source.ProductName) ? null : source.ProductName.Trim();
                source.Specification = string.IsNullOrWhiteSpace(source.Specification) ? null : source.Specification.Trim();
                source.CandidateCode = HsCodeTextHelper.NormalizeCode(source.CandidateCode);
                source.Fingerprint = BuildFingerprint(
                    NormalizeSearchText(source.QueryText),
                    source.CandidateCode,
                    source.ProductName,
                    source.Specification);
                preparedFeedback.Add((source, source.Fingerprint));
            }

            var existingFeedback = new Dictionary<string, HsCodeSearchFeedback>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in preparedFeedback.Select(item => item.Fingerprint)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Chunk(DatabaseInClauseBatchSize))
            {
                string[] keys = batch.ToArray();
                var rows = await context.HsCodeSearchFeedback
                    .Where(item => keys.Contains(item.Fingerprint))
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    existingFeedback.TryAdd(row.Fingerprint, row);
                }
            }

            foreach (var (source, fingerprint) in preparedFeedback)
            {
                if (existingFeedback.TryGetValue(fingerprint, out var target))
                {
                    target.AcceptedCount = Math.Max(target.AcceptedCount, source.AcceptedCount);
                    target.RejectedCount = Math.Max(target.RejectedCount, source.RejectedCount);
                    target.LastConfirmedAt = Max(target.LastConfirmedAt, source.LastConfirmedAt);
                    target.UpdatedAt = Max(target.UpdatedAt, source.UpdatedAt);
                }
                else
                {
                    source.Id = 0;
                    await context.HsCodeSearchFeedback.AddAsync(source, cancellationToken);
                    existingFeedback[fingerprint] = source;
                    addedFeedback++;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HsCodeKnowledgeImportResult(addedCodes, updatedCodes, addedExamples, updatedExamples, addedRelations, addedFeedback,
                $"HS知识库合并完成：编码新增{addedCodes}/更新{updatedCodes}，实例新增{addedExamples}/更新{updatedExamples}，替代关系新增{addedRelations}，学习记录新增{addedFeedback}。");
        }

        private static IQueryable<HsCodeDeclarationExample> BuildExampleQuery(AppDbContext context, string keyword)
        {
            var query = context.HsCodeDeclarationExamples.AsNoTracking();
            string value = (keyword ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                query = query.Where(item => item.RawReportedHsCode.Contains(value) ||
                    (item.ResolvedCurrentHsCode != null && item.ResolvedCurrentHsCode.Contains(value)) ||
                    item.ProductName.Contains(value) || (item.Specification != null && item.Specification.Contains(value)));
            return query;
        }

        private static IQueryable<HsCode> BuildMasterCandidateQuery(
            AppDbContext context,
            string primaryToken,
            string relatedToken)
        {
            var query = context.HsCodes.AsNoTracking().Where(item =>
                item.Status == HsCodeValidityPolicy.ActiveStatus &&
                item.SourceName != null && item.SourceName != "" &&
                item.EffectiveYear != null &&
                item.LastVerifiedAt != null);
            if (string.IsNullOrWhiteSpace(primaryToken) && string.IsNullOrWhiteSpace(relatedToken))
                return query.OrderByDescending(item => item.EffectiveYear).ThenBy(item => item.NormalizedCode);

            return query.Where(item =>
                    (!string.IsNullOrWhiteSpace(primaryToken) &&
                        (item.Name.Contains(primaryToken) ||
                         (item.Elements != null && item.Elements.Contains(primaryToken)) ||
                         (item.Description != null && item.Description.Contains(primaryToken)))) ||
                    (!string.IsNullOrWhiteSpace(relatedToken) &&
                        (item.Name.Contains(relatedToken) ||
                         (item.Elements != null && item.Elements.Contains(relatedToken)) ||
                         (item.Description != null && item.Description.Contains(relatedToken)))))
                .OrderByDescending(item => item.EffectiveYear)
                .ThenByDescending(item => item.LastVerifiedAt)
                .ThenBy(item => item.NormalizedCode);
        }

        private static async Task<List<HsCode>> LoadHsCodesByNormalizedCodesAsync(
            AppDbContext context,
            IReadOnlyCollection<string> codes,
            CancellationToken cancellationToken)
        {
            var result = new List<HsCode>();
            foreach (var batch in codes.Chunk(DatabaseInClauseBatchSize))
            {
                string[] values = batch.ToArray();
                result.AddRange(await context.HsCodes.AsNoTracking()
                    .Where(item => values.Contains(item.NormalizedCode))
                    .ToListAsync(cancellationToken));
            }
            return result;
        }

        private static async Task<List<HsCode>> LoadTrustedHsCodesByPrefixesAsync(
            AppDbContext context,
            IEnumerable<string> prefixes,
            CancellationToken cancellationToken)
        {
            var result = new List<HsCode>();
            foreach (string prefix in (prefixes ?? Enumerable.Empty<string>())
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(50))
            {
                var matches = await context.HsCodes.AsNoTracking()
                    .Where(item => item.Status == HsCodeValidityPolicy.ActiveStatus &&
                        item.SourceName != null && item.SourceName != "" &&
                        item.EffectiveYear != null && item.LastVerifiedAt != null &&
                        item.NormalizedCode.StartsWith(prefix))
                    .OrderByDescending(item => item.EffectiveYear)
                    .ThenByDescending(item => item.LastVerifiedAt)
                    .ThenBy(item => item.NormalizedCode)
                    .Take(200)
                    .ToListAsync(cancellationToken);
                result.AddRange(matches.Where(candidate => result.All(item => item.Id != candidate.Id)));
            }
            return result;
        }

        private static async Task<HashSet<string>> LoadKnownFingerprintsAsync(
            AppDbContext context,
            IEnumerable<string> fingerprints,
            CancellationToken cancellationToken)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var valuesToFind = (fingerprints ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var batch in valuesToFind.Chunk(DatabaseInClauseBatchSize))
            {
                string[] values = batch.ToArray();
                var found = await context.HsCodeDeclarationExamples.AsNoTracking()
                    .Where(item => values.Contains(item.Fingerprint))
                    .Select(item => item.Fingerprint)
                    .ToListAsync(cancellationToken);
                result.UnionWith(found);
            }
            return result;
        }

        private static async Task<List<HsCodeReplacementRelation>> LoadReplacementRelationsAsync(
            AppDbContext context,
            IReadOnlyCollection<string> oldCodes,
            CancellationToken cancellationToken)
        {
            var result = new List<HsCodeReplacementRelation>();
            foreach (var batch in oldCodes.Chunk(DatabaseInClauseBatchSize))
            {
                string[] values = batch.ToArray();
                result.AddRange(await context.HsCodeReplacementRelations.AsNoTracking()
                    .Where(item => values.Contains(item.OldCode))
                    .ToListAsync(cancellationToken));
            }
            return result;
        }

        private static async Task<List<HsCodeSearchFeedback>> LoadFeedbackByCandidateCodesAsync(
            AppDbContext context,
            IReadOnlyCollection<string> candidateCodes,
            CancellationToken cancellationToken)
        {
            var result = new List<HsCodeSearchFeedback>();
            foreach (var batch in candidateCodes.Chunk(DatabaseInClauseBatchSize))
            {
                string[] values = batch.ToArray();
                result.AddRange(await context.HsCodeSearchFeedback.AsNoTracking()
                    .Where(item => values.Contains(item.CandidateCode))
                    .ToListAsync(cancellationToken));
            }
            return result;
        }

        private static async Task<List<HsCodeRemoteCandidate>> LoadRemoteCandidatesByFingerprintsAsync(
            AppDbContext context,
            IReadOnlyCollection<string> fingerprints,
            CancellationToken cancellationToken)
        {
            var result = new List<HsCodeRemoteCandidate>();
            foreach (var batch in fingerprints.Chunk(DatabaseInClauseBatchSize))
            {
                string[] values = batch.ToArray();
                result.AddRange(await context.HsCodeRemoteCandidates
                    .Where(item => values.Contains(item.Fingerprint))
                    .ToListAsync(cancellationToken));
            }
            return result;
        }

        private static CurrentCodeResolution ResolveCurrentCode(
            HsCodeDeclarationExample example,
            IReadOnlyDictionary<string, HsCode> codes,
            IReadOnlyList<HsCodeReplacementRelation> relations)
        {
            string resolved = HsCodeTextHelper.NormalizeCode(example.ResolvedCurrentHsCode);
            if (!string.IsNullOrWhiteSpace(resolved) && codes.TryGetValue(resolved, out var resolvedCode) && HsCodeValidityPolicy.IsTrustedActive(resolvedCode))
                return new CurrentCodeResolution(resolved, example.IsManuallyVerified ? "ManuallyVerified" : "SuggestedReplacement", [], example.IsManuallyVerified);
            string raw = HsCodeTextHelper.NormalizeCode(example.RawReportedHsCode);
            if (codes.TryGetValue(raw, out var rawCode) && HsCodeValidityPolicy.IsTrustedActive(rawCode))
                return new CurrentCodeResolution(raw, "Active", [], true);
            var replacements = relations.Where(item => item.OldCode == raw)
                .OrderByDescending(item => item.IsManuallyVerified).ThenByDescending(item => item.Confidence)
                .Select(item => item.NewCode).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(code => codes.TryGetValue(code, out var candidate) && HsCodeValidityPolicy.IsTrustedActive(candidate))
                .ToList();
            var verifiedReplacements = relations.Where(item => item.OldCode == raw && item.IsManuallyVerified)
                .Select(item => item.NewCode).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(code => codes.TryGetValue(code, out var candidate) && HsCodeValidityPolicy.IsTrustedActive(candidate))
                .ToList();
            if (verifiedReplacements.Count == 1) return new CurrentCodeResolution(verifiedReplacements[0], "ObsoleteMapped", verifiedReplacements, true);
            if (replacements.Count == 1) return new CurrentCodeResolution(replacements[0], "SuggestedReplacement", replacements, false);
            return new CurrentCodeResolution(null, replacements.Count > 1 ? "Ambiguous" : "ObsoleteUnresolved", replacements, false);
        }

        private static CurrentCodeResolution ResolveRecommendedCurrentCode(
            string rawCode,
            IReadOnlyList<HsCodeRemoteReplacementEvidence> evidence,
            IReadOnlyDictionary<string, HsCode> codes)
        {
            var matching = (evidence ?? [])
                .Where(item => string.Equals(HsCodeTextHelper.NormalizeCode(item.OldCode), HsCodeTextHelper.NormalizeCode(rawCode), StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.RecommendedKeywords ?? [])
                .Select(HsCodeTextHelper.NormalizeCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var candidates = codes.Values
                .Where(HsCodeValidityPolicy.IsTrustedActive)
                .Where(item => matching.Any(recommended =>
                    string.Equals(item.NormalizedCode, recommended, StringComparison.OrdinalIgnoreCase) ||
                    item.NormalizedCode.StartsWith(recommended, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.NormalizedCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 1) return new CurrentCodeResolution(candidates[0], "WebRecommended", candidates, false);
            return new CurrentCodeResolution(null, candidates.Count > 1 ? "Ambiguous" : "ObsoleteUnresolved", candidates, false);
        }

        private static int ScoreText(string query, string candidate)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate)) return 0;
            if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase)) return 90;
            int score = candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ? 72 : query.Contains(candidate, StringComparison.OrdinalIgnoreCase) ? 62 : 0;
            var queryGrams = BuildNgrams(query);
            var candidateGrams = BuildNgrams(candidate);
            int intersection = queryGrams.Intersect(candidateGrams).Count();
            int denominator = queryGrams.Count + candidateGrams.Count;
            int dice = denominator == 0 ? 0 : (int)Math.Round(intersection * 120d / denominator);
            int relatedBoost = RelatedTerms.Any(pair => query.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) && candidate.Contains(pair.Value, StringComparison.OrdinalIgnoreCase)) ? 12 : 0;
            return Math.Clamp(Math.Max(score, dice) + relatedBoost, 0, 90);
        }

        private static AttributeAssessment AssessAttributes(string query, string candidate)
        {
            var reasons = new List<string>();
            var warnings = new List<string>();
            int penalty = 0;

            CompareExclusiveAttribute(query, candidate, "性别", 38, reasons, warnings,
                ["男式", "男童", "男士"], ["女式", "女童", "女士"]);
            CompareExclusiveAttribute(query, candidate, "织造方式", 24, reasons, warnings,
                ["针织", "钩编"], ["梭织", "机织"]);
            CompareCompatibleSetAttribute(query, candidate, "材质", 28, reasons, warnings,
                ["涤纶", "聚酯", "化纤", "粘胶", "氨纶", "锦纶"], ["棉", "全棉", "纯棉"],
                ["丝", "真丝"], ["毛", "羊毛"], ["麻"]);
            CompareExclusiveAttribute(query, candidate, "品类", 32, reasons, warnings,
                ["T恤衫", "T恤"], ["睡衣", "睡衣裤", "睡裙"], ["衬衫"], ["连衣裙"], ["夹克"], ["长裤", "短裤", "西裤", "裤子"]);

            if (reasons.Count == 0 && warnings.Count == 0) reasons.Add("未检测到明显属性冲突");
            return new AttributeAssessment(Math.Min(penalty, 90), reasons, warnings);

            void CompareExclusiveAttribute(
                string left,
                string right,
                string label,
                int conflictPenalty,
                List<string> matched,
                List<string> conflicts,
                params string[][] groups)
            {
                int leftGroup = FindGroup(left, groups);
                int rightGroup = FindGroup(right, groups);
                if (leftGroup < 0) return;
                if (rightGroup < 0)
                {
                    matched.Add($"{label}：查询为{groups[leftGroup][0]}，候选未明确限定");
                    return;
                }
                if (leftGroup == rightGroup)
                    matched.Add($"{label}：{groups[leftGroup][0]}一致");
                else
                {
                    penalty += conflictPenalty;
                    conflicts.Add($"{label}冲突：查询为{groups[leftGroup][0]}，候选为{groups[rightGroup][0]}");
                }
            }

            void CompareCompatibleSetAttribute(
                string left,
                string right,
                string label,
                int conflictPenalty,
                List<string> matched,
                List<string> conflicts,
                params string[][] groups)
            {
                var leftGroups = FindGroups(NormalizeMaterial(left), groups);
                var rightGroups = FindGroups(NormalizeMaterial(right), groups);
                if (leftGroups.Count == 0) return;
                if (rightGroups.Count == 0)
                {
                    matched.Add($"{label}：查询为{groups[leftGroups[0]][0]}，候选未明确限定");
                    return;
                }
                int common = leftGroups.Intersect(rightGroups).FirstOrDefault(-1);
                if (common >= 0)
                    matched.Add($"{label}：包含{groups[common][0]}");
                else
                {
                    penalty += conflictPenalty;
                    conflicts.Add($"{label}冲突：查询为{groups[leftGroups[0]][0]}，候选为{groups[rightGroups[0]][0]}");
                }
            }

            static int FindGroup(string value, string[][] groups)
            {
                for (int index = 0; index < groups.Length; index++)
                    if (groups[index].Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase))) return index;
                return -1;
            }

            static List<int> FindGroups(string value, string[][] groups) => groups
                .Select((tokens, index) => new { tokens, index })
                .Where(item => item.tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.index)
                .ToList();

            static string NormalizeMaterial(string value) =>
                (value ?? string.Empty).Replace("人棉", "粘胶", StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeSearchText(string value)
        {
            string normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
            foreach (var synonym in Synonyms.OrderByDescending(item => item.Key.Length))
                normalized = normalized.Replace(synonym.Key.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), synonym.Value.ToUpperInvariant(), StringComparison.Ordinal);
            return new string(normalized.Where(character => char.IsLetterOrDigit(character) || character >= 0x4e00 && character <= 0x9fff || character == '%').ToArray());
        }

        private static HashSet<string> BuildNgrams(string value)
        {
            string normalized = NormalizeSearchText(value);
            var grams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (normalized.Length <= 2) { if (normalized.Length > 0) grams.Add(normalized); return grams; }
            for (int index = 0; index < normalized.Length - 1; index++) grams.Add(normalized.Substring(index, 2));
            for (int index = 0; index < normalized.Length - 2; index++) grams.Add(normalized.Substring(index, 3));
            return grams;
        }

        private static string BuildFingerprint(params string[] values) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values.Select(value => (value ?? string.Empty).Trim().ToUpperInvariant())))));

        private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        private static void ValidatePackageContent(
            IReadOnlyList<HsCode> codes,
            IReadOnlyList<HsCodeDeclarationExample> examples,
            IReadOnlyList<HsCodeReplacementRelation> replacements,
            IReadOnlyList<HsCodeSearchFeedback> feedback)
        {
            if (codes.Count > 500_000 || examples.Count > 1_000_000 || replacements.Count > 1_000_000 || feedback.Count > 1_000_000)
                throw new InvalidDataException("HS知识库记录数量超过安全限制。");
            if (codes.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.Code)) ||
                                  HsCodeTextHelper.NormalizeCode(item.Code).Length > 20 ||
                                  string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 ||
                                  (item.SourceName?.Length ?? 0) > 200 ||
                                  (item.Description?.Length ?? 0) > 500 ||
                                  (item.Elements?.Length ?? 0) > 500 ||
                                  (item.Notes?.Length ?? 0) > 1000))
                throw new InvalidDataException("HS知识库包含无效或过长的编码字段。");
            if (codes.Any(item => string.Equals(item.Status, HsCodeValidityPolicy.ActiveStatus, StringComparison.OrdinalIgnoreCase) &&
                                  !HsCodeValidityPolicy.IsTrustedActive(item)))
                throw new InvalidDataException("HS知识库包含缺少来源、适用年度或验证时间的有效编码。");
            if (examples.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode)) ||
                                      string.IsNullOrWhiteSpace(item.ProductName) || item.ProductName.Length > 300 ||
                                      (item.Specification?.Length ?? 0) > 1500 ||
                                      (item.Source?.Length ?? 0) > 100 ||
                                      (item.ResolutionStatus?.Length ?? 0) > 30))
                throw new InvalidDataException("HS知识库包含无效或过长的申报实例字段。");
            if (replacements.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.OldCode)) ||
                                          string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.NewCode)) ||
                                          HsCodeTextHelper.NormalizeCode(item.OldCode).Length > 20 ||
                                          HsCodeTextHelper.NormalizeCode(item.NewCode).Length > 20 ||
                                          (item.Source?.Length ?? 0) > 100))
                throw new InvalidDataException("HS知识库包含无效的编码替代关系。");
            if (feedback.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.CandidateCode)) ||
                                      HsCodeTextHelper.NormalizeCode(item.CandidateCode).Length > 20 ||
                                      (item.QueryText?.Length ?? 0) > 500 ||
                                      (item.ProductName?.Length ?? 0) > 300 ||
                                      (item.Specification?.Length ?? 0) > 1500 ||
                                      item.AcceptedCount < 0 || item.RejectedCount < 0))
                throw new InvalidDataException("HS知识库包含无效的学习记录。");

            bool hasDuplicateCodes = codes
                .Select(item => HsCodeTextHelper.NormalizeCode(item.Code))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            bool hasDuplicateExamples = examples
                .Select(item => BuildFingerprint(
                    HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode),
                    (item.ProductName ?? string.Empty).Trim(),
                    (item.Specification ?? string.Empty).Trim()))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            bool hasDuplicateRelations = replacements
                .Select(item => new ReplacementRelationKey(
                    HsCodeTextHelper.NormalizeCode(item.OldCode),
                    HsCodeTextHelper.NormalizeCode(item.NewCode),
                    item.EffectiveYear))
                .GroupBy(value => value)
                .Any(group => group.Count() > 1);
            bool hasDuplicateFeedback = feedback
                .Select(item => BuildFingerprint(
                    NormalizeSearchText(item.QueryText),
                    HsCodeTextHelper.NormalizeCode(item.CandidateCode),
                    item.ProductName,
                    item.Specification))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (hasDuplicateCodes || hasDuplicateExamples || hasDuplicateRelations || hasDuplicateFeedback)
                throw new InvalidDataException("HS知识库包含重复的业务记录。");
        }

        private static string NormalizeResolutionStatus(string status, string currentCode, string rawCode)
        {
            if (string.Equals(status, "ManuallyVerified", StringComparison.OrdinalIgnoreCase)) return "ManuallyVerified";
            if (!string.IsNullOrWhiteSpace(currentCode)) return string.Equals(currentCode, rawCode, StringComparison.OrdinalIgnoreCase) ? "Active" : "ObsoleteMapped";
            return "Unresolved";
        }

        private static async Task UpsertExampleInContextAsync(
            AppDbContext context,
            HsCodeExampleInput input,
            DateTime now,
            CancellationToken cancellationToken,
            bool incrementUseCount = true)
        {
            string code = HsCodeTextHelper.NormalizeCode(input.RawReportedHsCode);
            string name = (input.ProductName ?? string.Empty).Trim();
            string fingerprint = BuildFingerprint(code, name, input.Specification);
            var example = await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            if (example == null)
            {
                example = new HsCodeDeclarationExample { Fingerprint = fingerprint, CreatedAt = now };
                await context.HsCodeDeclarationExamples.AddAsync(example, cancellationToken);
            }
            example.RawReportedHsCode = code; example.ResolvedCurrentHsCode = HsCodeTextHelper.NormalizeCode(input.ResolvedCurrentHsCode); example.ProductName = name;
            example.Specification = (input.Specification ?? string.Empty).Trim(); example.SearchText = NormalizeSearchText($"{name} {input.Specification}");
            example.Source = string.IsNullOrWhiteSpace(input.Source) ? "UserConfirmed" : input.Source.Trim();
            example.SourceYear = input.SourceYear;
            example.ResolutionStatus = input.IsManuallyVerified ? "ManuallyVerified" : NormalizeResolutionStatus(input.ResolutionStatus, example.ResolvedCurrentHsCode, code);
            example.IsManuallyVerified = true;
            if (incrementUseCount) example.UseCount++;
            example.LastUsedAt = now; example.UpdatedAt = now;
        }

        private async Task ResolveExamplesAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            int lastId = 0;
            while (true)
            {
                var examples = await context.HsCodeDeclarationExamples
                    .Where(item => item.Id > lastId)
                    .OrderBy(item => item.Id)
                    .Take(KnowledgeResolutionBatchSize)
                    .ToListAsync(cancellationToken);
                if (examples.Count == 0)
                {
                    break;
                }

                var rawCodes = examples
                    .Select(item => HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var relations = new List<HsCodeReplacementRelation>();
                foreach (var batch in rawCodes.Chunk(DatabaseInClauseBatchSize))
                {
                    string[] batchCodes = batch.ToArray();
                    relations.AddRange(await context.HsCodeReplacementRelations
                        .AsNoTracking()
                        .Where(item => batchCodes.Contains(item.OldCode))
                        .ToListAsync(cancellationToken));
                }

                var lookupCodes = examples
                    .SelectMany(item => new[] { item.RawReportedHsCode, item.ResolvedCurrentHsCode })
                    .Concat(relations.Select(item => item.NewCode))
                    .Select(HsCodeTextHelper.NormalizeCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var map = new Dictionary<string, HsCode>(StringComparer.OrdinalIgnoreCase);
                foreach (var batch in lookupCodes.Chunk(DatabaseInClauseBatchSize))
                {
                    var rows = await context.HsCodes
                        .AsNoTracking()
                        .Where(item => batch.Contains(item.NormalizedCode))
                        .ToListAsync(cancellationToken);
                    foreach (var row in rows)
                    {
                        map[row.NormalizedCode] = row;
                    }
                }

                foreach (var example in examples)
                {
                    var resolution = ResolveCurrentCode(example, map, relations);
                    example.ResolvedCurrentHsCode = resolution.CurrentCode;
                    example.ResolutionStatus = resolution.Status;
                    example.UpdatedAt = DateTime.UtcNow;
                }

                await context.SaveChangesAsync(cancellationToken);
                lastId = examples[^1].Id;
                context.ChangeTracker.Clear();
            }
        }

        private static void MergeHsCode(HsCode source, HsCode target)
        {
            target.Name = Prefer(source.Name, target.Name); target.Unit = Prefer(source.Unit, target.Unit);
            target.Description = Prefer(source.Description, target.Description); target.Elements = Prefer(source.Elements, target.Elements);
            target.SupervisionConditions = Prefer(source.SupervisionConditions, target.SupervisionConditions);
            target.InspectionCategory = Prefer(source.InspectionCategory, target.InspectionCategory); target.RebateRate = Prefer(source.RebateRate, target.RebateRate);
            target.NormalTariffRate = Prefer(source.NormalTariffRate, target.NormalTariffRate); target.PreferentialTariffRate = Prefer(source.PreferentialTariffRate, target.PreferentialTariffRate);
            target.ExportTariffRate = Prefer(source.ExportTariffRate, target.ExportTariffRate); target.ConsumptionTaxRate = Prefer(source.ConsumptionTaxRate, target.ConsumptionTaxRate);
            target.ValueAddedTaxRate = Prefer(source.ValueAddedTaxRate, target.ValueAddedTaxRate); target.Notes = Prefer(source.Notes, target.Notes);
            target.SourceName = Prefer(source.SourceName, target.SourceName); target.EffectiveYear = Max(source.EffectiveYear, target.EffectiveYear);
            target.LastVerifiedAt = Max(source.LastVerifiedAt, target.LastVerifiedAt); target.UpdateTime = Max(source.UpdateTime, target.UpdateTime);
            if (HsCodeValidityPolicy.IsTrustedActive(source)) target.Status = "Active";
        }

        private static void MergeExample(HsCodeDeclarationExample source, HsCodeDeclarationExample target)
        {
            target.ResolvedCurrentHsCode = Prefer(source.ResolvedCurrentHsCode, target.ResolvedCurrentHsCode);
            target.ProductName = Prefer(source.ProductName, target.ProductName); target.Specification = Prefer(source.Specification, target.Specification);
            target.SearchText = Prefer(source.SearchText, target.SearchText); target.Source = Prefer(source.Source, target.Source);
            target.SourceYear = Max(source.SourceYear, target.SourceYear); target.IsManuallyVerified |= source.IsManuallyVerified;
            target.UseCount = Math.Max(target.UseCount, source.UseCount); target.RejectedCount = Math.Max(target.RejectedCount, source.RejectedCount);
            target.LastUsedAt = Max(source.LastUsedAt, target.LastUsedAt); target.UpdatedAt = Max(source.UpdatedAt, target.UpdatedAt);
            if (source.IsManuallyVerified) target.ResolutionStatus = source.ResolutionStatus;
        }

        private static string Prefer(string primary, string fallback) => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();

        private static string ValidateTextLength(string value, int maximumLength, string fieldName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > maximumLength)
                throw new ArgumentException($"{fieldName}不能超过 {maximumLength} 个字符。", fieldName);
            return normalized;
        }

        private static string NormalizeHistoryProductName(string value)
        {
            string name = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
            int separator = name.LastIndexOf('-');
            if (separator > 0 && name[(separator + 1)..].All(char.IsDigit))
                return name[..separator].TrimEnd();
            return name;
        }

        private static string JoinHistorySpecification(params string[] values) => string.Join(" · ",
            values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));

        private static int? Max(int? left, int? right) => !left.HasValue ? right : !right.HasValue ? left : Math.Max(left.Value, right.Value);
        private static DateTime? Max(DateTime? left, DateTime? right) => !left.HasValue ? right : !right.HasValue ? left : left > right ? left : right;
        private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;

        private static async Task<byte[]> ReadEntryAsync(ZipArchive archive, string name, CancellationToken cancellationToken)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"HS知识库缺少文件：{name}。");
            if (entry.Length > MaximumKnowledgeEntryBytes)
            {
                throw new InvalidDataException($"HS知识库文件过大：{name}。");
            }
            await using var stream = entry.Open();
            using var output = new MemoryStream();
            await BoundedStreamHelper.CopyToAsync(
                stream,
                output,
                MaximumKnowledgeEntryBytes,
                cancellationToken);
            return output.ToArray();
        }

        private sealed class HashingQuotaWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            private readonly long _maximumEntryBytes;
            private readonly long _maximumTotalBytes;
            private readonly Func<long> _totalBytesProvider;
            private bool _hashFinalized;

            public HashingQuotaWriteStream(
                Stream inner,
                long maximumEntryBytes,
                long maximumTotalBytes,
                Func<long> totalBytesProvider)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _maximumEntryBytes = maximumEntryBytes;
                _maximumTotalBytes = maximumTotalBytes;
                _totalBytesProvider = totalBytesProvider ?? throw new ArgumentNullException(nameof(totalBytesProvider));
            }

            public long BytesWritten { get; private set; }

            public string GetHashHex()
            {
                if (_hashFinalized)
                {
                    throw new InvalidOperationException("HS知识库导出校验和已读取。");
                }

                _hashFinalized = true;
                return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position
            {
                get => BytesWritten;
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) =>
                _inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                ValidateWrite(count);
                _hash.AppendData(buffer, offset, count);
                _inner.Write(buffer, offset, count);
                BytesWritten += count;
            }

            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ValidateWrite(buffer.Length);
                _hash.AppendData(buffer.Span);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesWritten += buffer.Length;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _hash.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ValidateWrite(int count)
            {
                if (count < 0 || BytesWritten > _maximumEntryBytes - count)
                {
                    throw new PayloadLimitExceededException(_maximumEntryBytes);
                }

                long totalBytes = _totalBytesProvider();
                if (totalBytes > _maximumTotalBytes - BytesWritten - count)
                {
                    throw new PayloadLimitExceededException(_maximumTotalBytes);
                }
            }
        }

        private sealed class MaximumLengthWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _maximumBytes;

            public MaximumLengthWriteStream(Stream inner, long maximumBytes)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _maximumBytes = maximumBytes;
            }

            private long BytesWritten { get; set; }
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                ValidateWrite(count);
                _inner.Write(buffer, offset, count);
                BytesWritten += count;
            }

            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ValidateWrite(buffer.Length);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesWritten += buffer.Length;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ValidateWrite(int count)
            {
                if (count < 0 || BytesWritten > _maximumBytes - count)
                {
                    throw new PayloadLimitExceededException(_maximumBytes);
                }
            }
        }

        private sealed record KnowledgeManifest(string SchemaVersion, DateTimeOffset ExportedAt, DateTimeOffset? Since, Dictionary<string, string> Checksums);
        private readonly record struct ReplacementRelationKey(string OldCode, string NewCode, int? EffectiveYear);
        private sealed record CurrentCodeResolution(string CurrentCode, string Status, IReadOnlyList<string> Replacements, bool CanUse);
        private sealed record KnowledgeCandidate(
            HsCodeDeclarationExample Example,
            CurrentCodeResolution Resolution,
            int Score,
            IReadOnlyList<string> MatchReasons,
            IReadOnlyList<string> ConflictWarnings);
        private sealed record AttributeAssessment(int Penalty, IReadOnlyList<string> MatchReasons, IReadOnlyList<string> ConflictWarnings);
        private sealed record HistorySourceRow(string Code, string Name, string Specification, string Source, string Variant);
        private sealed record HistorySourceProjection(
            string Code,
            string NamePrimary,
            string NameFallback,
            string SpecificationOne,
            string SpecificationTwo,
            string SpecificationThree,
            string SpecificationFour,
            string Source,
            string Variant);
        private sealed record HistorySourceReadResult(IReadOnlyList<HistorySourceProjection> Rows, bool HasMore);
        private sealed record HistoryCandidateGroup(
            string Fingerprint,
            string RawCode,
            string ProductName,
            string Specification,
            string Source,
            int SourceCount,
            int VariantCount,
            IReadOnlyList<string> VariantSamples);
    }

}
