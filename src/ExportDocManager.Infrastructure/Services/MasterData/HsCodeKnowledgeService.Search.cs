using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
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
    }
}
