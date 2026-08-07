using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
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

        internal static string BuildFingerprint(params string[] values) =>
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

        internal static async Task UpsertExampleInContextAsync(
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

    }
}
