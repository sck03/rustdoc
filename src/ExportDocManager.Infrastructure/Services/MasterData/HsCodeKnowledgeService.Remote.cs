using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
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
                        Fingerprint = fingerprint,
                        QueryText = (query ?? string.Empty).Trim(),
                        RawReportedHsCode = code,
                        ProductName = item.Name.Trim(),
                        Specification = (item.Description ?? string.Empty).Trim(),
                        Source = string.IsNullOrWhiteSpace(source) ? "i5a6" : source.Trim(),
                        SourceUrl = string.IsNullOrWhiteSpace(record.EvidenceUrl) ? item.DetailUrl : record.EvidenceUrl,
                        ReviewStatus = "Pending",
                        FirstSeenAt = now
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
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _dbContextFactory,
                async (context, token) =>
                {
                    int reviewed = 0;
                    foreach (var input in normalized)
                    {
                        if (await ReviewRemoteCandidateInContextAsync(context, input, token, saveChanges: false))
                        {
                            reviewed++;
                        }
                    }

                    await context.SaveChangesAsync(token);
                    return reviewed;
                },
                cancellationToken).ConfigureAwait(false);
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
                        item => item.Fingerprint == fingerprint &&
                            item.Source == BuildRemoteConfirmationSource(candidate),
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
            string fingerprint = BuildFingerprint(candidate.RawReportedHsCode, candidate.ProductName, candidate.Specification);
            var existingExample = await context.HsCodeDeclarationExamples
                .FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken);
            if (existingExample != null)
            {
                string existingCode = HsCodeTextHelper.NormalizeCode(existingExample.ResolvedCurrentHsCode);
                if (!string.IsNullOrWhiteSpace(existingCode) &&
                    !string.Equals(existingCode, currentCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("同一商品已有指向其他 HS 编码的正式实例，请先在实例库中处理冲突。");
                }

                // An existing company/manual instance is not owned by this remote
                // review. Keep it intact so resetting the candidate can never delete
                // or rewrite knowledge created by another workflow.
            }
            else
            {
                await UpsertExampleInContextAsync(context, new HsCodeExampleInput(
                    0,
                    candidate.RawReportedHsCode,
                    currentCode,
                    candidate.ProductName,
                    candidate.Specification,
                    BuildRemoteConfirmationSource(candidate),
                    null,
                    "ManuallyVerified",
                    true), now, cancellationToken);
            }
            candidate.SuggestedCurrentHsCode = currentCode;
            candidate.ReviewStatus = "Confirmed";
            candidate.ReviewedAt = now;
            if (saveChanges) await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static string BuildRemoteConfirmationSource(HsCodeRemoteCandidate candidate)
        {
            string source = string.IsNullOrWhiteSpace(candidate?.Source) ? "remote" : candidate.Source.Trim();
            string suffix = $"RemoteConfirmed:{candidate?.Id ?? 0}:";
            int maximumSourceLength = 100 - suffix.Length;
            return suffix + source[..Math.Min(source.Length, Math.Max(1, maximumSourceLength))];
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
                            OldCode = oldCode,
                            NewCode = newCode,
                            EffectiveYear = preview.EffectiveYear,
                            Source = preview.SourceName,
                            Confidence = Math.Max(50, 80 - index * 10),
                            IsManuallyVerified = false,
                            CreatedAt = now,
                            UpdatedAt = now
                        }, cancellationToken);
                    }
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await ResolveExamplesAsync(context, cancellationToken);
        }

    }
}
