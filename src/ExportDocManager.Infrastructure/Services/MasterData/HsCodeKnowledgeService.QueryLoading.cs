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
    }
}
