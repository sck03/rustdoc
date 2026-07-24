using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
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

        public HsCodeKnowledgeService(IDbContextFactory<AppDbContext> dbContextFactory) =>
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

        public async Task<HsCodeKnowledgeSearchResponse> SearchAsync(
            string query,
            int maxResults = 20,
            CancellationToken cancellationToken = default)
        {
            string rawQuery = (query ?? string.Empty).Trim();
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
            string name = (input.ProductName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawCode) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("申报实例必须填写历史/原始HS编码和商品名称。");
            string fingerprint = BuildFingerprint(rawCode, name, input.Specification);
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
            entity.Specification = (input.Specification ?? string.Empty).Trim();
            entity.SearchText = NormalizeSearchText($"{name} {input.Specification}");
            entity.Source = string.IsNullOrWhiteSpace(input.Source) ? "Manual" : input.Source.Trim();
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
            string code = HsCodeTextHelper.NormalizeCode(input.CandidateCode);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("确认结果必须包含HS编码。");
            string fingerprint = BuildFingerprint(NormalizeSearchText(input.QueryText), code, input.ProductName, input.Specification);
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
            entity.QueryText = (input.QueryText ?? string.Empty).Trim();
            entity.ProductName = (input.ProductName ?? string.Empty).Trim();
            entity.Specification = (input.Specification ?? string.Empty).Trim();
            entity.CandidateCode = code;
            if (input.Accepted) { entity.AcceptedCount++; entity.LastConfirmedAt = now; }
            else entity.RejectedCount++;
            entity.UpdatedAt = now;
            if (input.Accepted)
            {
                var exampleInput = new HsCodeExampleInput(0, code, code,
                    string.IsNullOrWhiteSpace(input.ProductName) ? input.QueryText : input.ProductName,
                    input.Specification, "UserConfirmed", DateTime.Now.Year, "ManuallyVerified", true);
                await UpsertExampleInContextAsync(context, exampleInput, now, cancellationToken, incrementUseCount: false);
            }
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<HsCodeHistoryCandidatePage> DiscoverHistoryCandidatesAsync(
            string keyword, int pageNumber = 1, int pageSize = 30, CancellationToken cancellationToken = default)
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);
            string filter = NormalizeSearchText(keyword);
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var codes = await context.HsCodes.AsNoTracking().ToListAsync(cancellationToken);
            var codeMap = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode))
                .GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var relations = await context.HsCodeReplacementRelations.AsNoTracking().ToListAsync(cancellationToken);
            var known = (await context.HsCodeDeclarationExamples.AsNoTracking().Select(item => item.Fingerprint).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<HistorySourceRow>();

            rows.AddRange((await context.Products.AsNoTracking().Where(item => item.HSCode != null && item.HSCode != "").Take(5000).ToListAsync(cancellationToken))
                .Select(item => new HistorySourceRow(item.HSCode, Prefer(item.NameCN, item.NameEN), $"{item.Material} {item.Brand} {item.Elements} {item.Description}", "商品主数据", string.Empty)));
            rows.AddRange((await context.Items.AsNoTracking().Where(item => item.HSCode != null && item.HSCode != "").Take(10000).ToListAsync(cancellationToken))
                .Select(item => new HistorySourceRow(item.HSCode, NormalizeHistoryProductName(Prefer(item.StyleNameCN, item.StyleName)),
                    JoinHistorySpecification(item.FabricComposition, item.Brand), "历史商业发票", item.StyleNo)));
            rows.AddRange((await context.CustomsCooItems.AsNoTracking().Where(item => item.HSCode != "").Take(5000).ToListAsync(cancellationToken))
                .Select(item => new HistorySourceRow(item.HSCode, Prefer(item.GoodsName, item.GoodsNameE), item.GoodsDesc, "历史报关资料", item.SourceStyleNo)));

            var candidates = rows.Where(item => !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item with { Code = HsCodeTextHelper.NormalizeCode(item.Code), Name = item.Name.Trim(), Specification = (item.Specification ?? string.Empty).Trim() })
                .Where(item => string.IsNullOrWhiteSpace(filter) || NormalizeSearchText($"{item.Name} {item.Specification} {item.Code}").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => new { item.Code, Name = NormalizeSearchText(item.Name), Specification = NormalizeSearchText(item.Specification) })
                .Select(group =>
                {
                    var first = group.First();
                    var variants = group.Select(item => (item.Variant ?? string.Empty).Trim())
                        .Where(item => item.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    string fingerprint = BuildFingerprint(first.Code, first.Name, first.Specification);
                    var resolution = ResolveCurrentCode(new HsCodeDeclarationExample { RawReportedHsCode = first.Code }, codeMap, relations);
                    return new HsCodeHistoryLearningCandidate(fingerprint, first.Code, resolution.CurrentCode ?? string.Empty,
                        first.Name, first.Specification, string.Join("、", group.Select(item => item.Source).Distinct()), group.Count(),
                        variants.Count, variants.Take(5).ToList(), resolution.Status, resolution.Replacements,
                        !known.Contains(fingerprint) && resolution.CanUse);
                })
                .Where(item => !known.Contains(item.Fingerprint))
                .OrderByDescending(item => item.CanConfirm).ThenByDescending(item => item.SourceCount).ThenBy(item => item.ProductName)
                .ToList();
            int totalCount = candidates.Count;
            var items = candidates.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
            return new HsCodeHistoryCandidatePage(items, totalCount, pageNumber, pageSize);
        }

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
            var codes = await context.HsCodes.AsNoTracking().ToListAsync(cancellationToken);
            var codeMap = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode)).GroupBy(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var relations = await context.HsCodeReplacementRelations.AsNoTracking().ToListAsync(cancellationToken);
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
            var codes = await context.HsCodes.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdateTime >= sinceUtc).ToListAsync(cancellationToken);
            var examples = await context.HsCodeDeclarationExamples.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc).ToListAsync(cancellationToken);
            var relations = await context.HsCodeReplacementRelations.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc).ToListAsync(cancellationToken);
            var feedback = await context.HsCodeSearchFeedback.AsNoTracking().Where(item => !sinceUtc.HasValue || item.UpdatedAt >= sinceUtc).ToListAsync(cancellationToken);
            var entries = new Dictionary<string, byte[]>
            {
                ["hs-codes.json"] = JsonSerializer.SerializeToUtf8Bytes(codes, JsonOptions),
                ["declaration-examples.json"] = JsonSerializer.SerializeToUtf8Bytes(examples, JsonOptions),
                ["replacement-relations.json"] = JsonSerializer.SerializeToUtf8Bytes(relations, JsonOptions),
                ["search-feedback.json"] = JsonSerializer.SerializeToUtf8Bytes(feedback, JsonOptions)
            };
            var manifest = new KnowledgeManifest(PackageSchemaVersion, DateTimeOffset.UtcNow, since, entries.ToDictionary(item => item.Key, item => Sha256(item.Value)));
            entries["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in entries)
                {
                    var zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                    await using var stream = zipEntry.Open();
                    await stream.WriteAsync(entry.Value, cancellationToken);
                }
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
            if (archive.Entries.Any(entry => !knownNames.Contains(entry.FullName) || entry.Length > MaximumPackageBytes))
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
            foreach (var source in preview.HsCodes)
            {
                string code = HsCodeTextHelper.NormalizeCode(source.Code);
                if (string.IsNullOrWhiteSpace(code)) continue;
                var target = await context.HsCodes.FirstOrDefaultAsync(item => item.NormalizedCode == code, cancellationToken);
                if (target == null) { source.Id = 0; source.Code = code; await context.HsCodes.AddAsync(source, cancellationToken); addedCodes++; }
                else { MergeHsCode(source, target); updatedCodes++; }
            }
            foreach (var source in preview.Examples)
            {
                source.RawReportedHsCode = HsCodeTextHelper.NormalizeCode(source.RawReportedHsCode);
                source.ResolvedCurrentHsCode = HsCodeTextHelper.NormalizeCode(source.ResolvedCurrentHsCode);
                source.ProductName = source.ProductName.Trim();
                source.Specification = (source.Specification ?? string.Empty).Trim();
                source.SearchText = NormalizeSearchText($"{source.ProductName} {source.Specification}");
                source.Fingerprint = BuildFingerprint(source.RawReportedHsCode, source.ProductName, source.Specification);
                var target = await context.HsCodeDeclarationExamples.FirstOrDefaultAsync(item => item.Fingerprint == source.Fingerprint, cancellationToken);
                if (target == null) { source.Id = 0; await context.HsCodeDeclarationExamples.AddAsync(source, cancellationToken); addedExamples++; }
                else { MergeExample(source, target); updatedExamples++; }
            }
            foreach (var source in preview.Replacements)
            {
                bool exists = await context.HsCodeReplacementRelations.AnyAsync(item => item.OldCode == source.OldCode && item.NewCode == source.NewCode && item.EffectiveYear == source.EffectiveYear, cancellationToken);
                if (!exists) { source.Id = 0; await context.HsCodeReplacementRelations.AddAsync(source, cancellationToken); addedRelations++; }
            }
            foreach (var source in preview.Feedback)
            {
                var target = await context.HsCodeSearchFeedback.FirstOrDefaultAsync(item => item.Fingerprint == source.Fingerprint, cancellationToken);
                if (target == null) { source.Id = 0; await context.HsCodeSearchFeedback.AddAsync(source, cancellationToken); addedFeedback++; }
                else { target.AcceptedCount = Math.Max(target.AcceptedCount, source.AcceptedCount); target.RejectedCount = Math.Max(target.RejectedCount, source.RejectedCount); target.UpdatedAt = Max(target.UpdatedAt, source.UpdatedAt); }
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
            if (codes.Any(item => string.Equals(item.Status, HsCodeValidityPolicy.ActiveStatus, StringComparison.OrdinalIgnoreCase) &&
                                  !HsCodeValidityPolicy.IsTrustedActive(item)))
                throw new InvalidDataException("HS知识库包含缺少来源、适用年度或验证时间的有效编码。");
            if (examples.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode)) ||
                                     string.IsNullOrWhiteSpace(item.ProductName) || item.ProductName.Length > 300 ||
                                     (item.Specification?.Length ?? 0) > 1500))
                throw new InvalidDataException("HS知识库包含无效或过长的申报实例字段。");
            if (replacements.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.OldCode)) ||
                                         string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.NewCode))))
                throw new InvalidDataException("HS知识库包含无效的编码替代关系。");
            if (feedback.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.CandidateCode)) ||
                                     item.AcceptedCount < 0 || item.RejectedCount < 0))
                throw new InvalidDataException("HS知识库包含无效的学习记录。");
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
            var codes = await context.HsCodes.AsNoTracking().ToListAsync(cancellationToken);
            var map = codes.Where(item => !string.IsNullOrWhiteSpace(item.NormalizedCode)).ToDictionary(item => item.NormalizedCode, StringComparer.OrdinalIgnoreCase);
            var relations = await context.HsCodeReplacementRelations.AsNoTracking().ToListAsync(cancellationToken);
            var examples = await context.HsCodeDeclarationExamples.ToListAsync(cancellationToken);
            foreach (var example in examples)
            {
                var resolution = ResolveCurrentCode(example, map, relations);
                example.ResolvedCurrentHsCode = resolution.CurrentCode;
                example.ResolutionStatus = resolution.Status;
                example.UpdatedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync(cancellationToken);
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
            await using var stream = entry.Open();
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }

        private sealed record KnowledgeManifest(string SchemaVersion, DateTimeOffset ExportedAt, DateTimeOffset? Since, Dictionary<string, string> Checksums);
        private sealed record CurrentCodeResolution(string CurrentCode, string Status, IReadOnlyList<string> Replacements, bool CanUse);
        private sealed record KnowledgeCandidate(
            HsCodeDeclarationExample Example,
            CurrentCodeResolution Resolution,
            int Score,
            IReadOnlyList<string> MatchReasons,
            IReadOnlyList<string> ConflictWarnings);
        private sealed record AttributeAssessment(int Penalty, IReadOnlyList<string> MatchReasons, IReadOnlyList<string> ConflictWarnings);
        private sealed record HistorySourceRow(string Code, string Name, string Specification, string Source, string Variant);
    }

}
