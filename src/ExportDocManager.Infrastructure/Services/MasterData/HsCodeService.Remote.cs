using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Serilog;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        private const int MaxRecommendationDepth = 3;
        private const int MaxAutomaticDetailLookups = 12;

        public async Task ProcessRemainingDetailsAsync(
            List<HsCode> items,
            Action<HsCode> onItemUpdated,
            Action<HsCode> onItemRemoved,
            Action<List<HsCode>> onItemsAdded = null,
            CancellationToken cancellationToken = default)
        {
            var pendingItems = (items ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.DetailUrl) && string.IsNullOrWhiteSpace(item.Elements))
                .ToList();

            foreach (var item in pendingItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _detailFetchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await FetchDetailAsync(item, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(item.Elements))
                    {
                        onItemUpdated?.Invoke(item);
                    }
                    else
                    {
                        await TryReplaceItemAsync(item, [], onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (HsCodeRemoteExpiredException ex)
                {
                    bool replaced = await TryReplaceItemAsync(
                        item,
                        ex.RecommendedKeywords,
                        onItemUpdated,
                        onItemRemoved,
                        onItemsAdded,
                        cancellationToken).ConfigureAwait(false);
                    if (!replaced) onItemRemoved?.Invoke(item);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "HS编码详情补充失败，已继续处理其他记录。Code={Code}", item.Code);
                }
                finally
                {
                    _detailFetchSemaphore.Release();
                }
            }
        }

        public async Task<List<HsCode>> SearchRemoteAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var bundle = await SearchRemoteEvidenceAsync(keyword, cancellationToken).ConfigureAwait(false);
            return DeduplicateRemoteExamples(bundle.Records
                .Where(record => !record.IsExpired)
                .Select(record => record.Item));
        }

        public async Task<HsCodeRemoteSearchBundle> SearchRemoteEvidenceAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            if (_remoteProviders.Count == 0)
            {
                Log.Warning("HS编码联网查询未配置任何 Provider。Keyword={Keyword}", keyword);
                return HsCodeRemoteSearchBundle.Empty(keyword, "未配置");
            }

            foreach (var provider in _remoteProviders)
            {
                try
                {
                    var bundle = await provider.SearchEvidenceAsync(keyword, cancellationToken).ConfigureAwait(false);
                    if (bundle.Records.Count > 0)
                    {
                        return await EnrichRemoteEvidenceAsync(provider, bundle, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "HS编码联网 Provider 查询失败。Provider={Provider}", provider.Name);
                }
            }

            return HsCodeRemoteSearchBundle.Empty(
                keyword,
                string.Join(", ", _remoteProviders.Select(provider => provider.Name)));
        }

        private static async Task<HsCodeRemoteSearchBundle> EnrichRemoteEvidenceAsync(
            IHsCodeRemoteProvider provider,
            HsCodeRemoteSearchBundle initialBundle,
            CancellationToken cancellationToken)
        {
            if (HasCurrentStandardCode(initialBundle.Records)) return initialBundle;

            var rootRecords = initialBundle.Records.ToList();
            var discoveredStandards = new List<HsCodeRemoteSearchRecord>();
            var replacementEvidence = initialBundle.ReplacementEvidence.ToList();
            var visitedQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeRemoteLookupIdentity(initialBundle.Query)
            };
            var visitedDetailCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int remainingDetailLookups = MaxAutomaticDetailLookups;

            await ResolveCurrentStandardsAsync(initialBundle, depth: 0).ConfigureAwait(false);
            return MergeEnrichedRemoteEvidence(
                initialBundle,
                rootRecords.Concat(discoveredStandards),
                replacementEvidence);

            async Task ResolveCurrentStandardsAsync(HsCodeRemoteSearchBundle bundle, int depth)
            {
                discoveredStandards.AddRange(bundle.Records.Where(IsStandardRecord));
                replacementEvidence.AddRange(bundle.ReplacementEvidence);
                if (depth > MaxRecommendationDepth || HasCurrentStandardCode(bundle.Records)) return;

                var recommendedKeywords = new HashSet<string>(
                    bundle.ReplacementEvidence.SelectMany(item => item.RecommendedKeywords),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var record in bundle.Records
                             .Where(item => item.Kind == HsCodeRemoteRecordKind.DeclarationExample)
                             .Where(item => item.Item != null && provider.CanHandleDetailUrl(item.Item.DetailUrl))
                             .GroupBy(item => HsCodeTextHelper.NormalizeCode(item.Item.Code), StringComparer.OrdinalIgnoreCase)
                             .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                             .Select(group => group.First()))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string oldCode = HsCodeTextHelper.NormalizeCode(record.Item.Code);
                    if (!visitedDetailCodes.Add(oldCode)) continue;
                    if (remainingDetailLookups-- <= 0)
                    {
                        Log.Information(
                            "HS编码联网自动详情追踪达到上限。Provider={Provider}, Query={Query}, Limit={Limit}",
                            provider.Name,
                            initialBundle.Query,
                            MaxAutomaticDetailLookups);
                        break;
                    }

                    try
                    {
                        var detail = await provider.FetchDetailEvidenceAsync(record, cancellationToken).ConfigureAwait(false);
                        if (!detail.IsExpired)
                        {
                            discoveredStandards.Add(new HsCodeRemoteSearchRecord(
                                detail.Item,
                                HsCodeRemoteRecordKind.StandardCode,
                                false,
                                detail.InstanceCount,
                                record.SummaryUrl,
                                string.IsNullOrWhiteSpace(detail.EvidenceUrl) ? record.EvidenceUrl : detail.EvidenceUrl,
                                detail.ObservedAt));
                        }

                        if (detail.RecommendedKeywords.Count == 0) continue;
                        replacementEvidence.Add(new HsCodeRemoteReplacementEvidence(
                            oldCode,
                            detail.RecommendedKeywords,
                            string.IsNullOrWhiteSpace(detail.EvidenceUrl) ? record.EvidenceUrl : detail.EvidenceUrl,
                            detail.ObservedAt));
                        foreach (string recommendedKeyword in detail.RecommendedKeywords)
                            recommendedKeywords.Add(recommendedKeyword);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            ex,
                            "HS编码联网自动详情追踪失败，已继续处理其他旧编码。Provider={Provider}, Code={Code}",
                            provider.Name,
                            oldCode);
                    }
                }

                if (depth >= MaxRecommendationDepth) return;
                foreach (string recommendedKeyword in recommendedKeywords
                             .Select(HsCodeTextHelper.NormalizeCode)
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visitedQueries.Add(NormalizeRemoteLookupIdentity(recommendedKeyword))) continue;
                    try
                    {
                        var nested = await provider.SearchEvidenceAsync(recommendedKeyword, cancellationToken).ConfigureAwait(false);
                        await ResolveCurrentStandardsAsync(nested, depth + 1).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            ex,
                            "HS编码联网推荐查询失败，已继续处理其他推荐编码。Provider={Provider}, Keyword={Keyword}",
                            provider.Name,
                            recommendedKeyword);
                    }
                }
            }
        }

        private static bool HasCurrentStandardCode(IEnumerable<HsCodeRemoteSearchRecord> records) =>
            (records ?? []).Any(record => IsStandardRecord(record) && !record.IsExpired);

        private static bool IsStandardRecord(HsCodeRemoteSearchRecord record) =>
            record?.Kind == HsCodeRemoteRecordKind.StandardCode && record.Item != null;

        private static string NormalizeRemoteLookupIdentity(string value)
        {
            string normalizedCode = HsCodeTextHelper.NormalizeCodeSearchKeyword(value);
            return string.IsNullOrWhiteSpace(normalizedCode) ? (value ?? string.Empty).Trim() : normalizedCode;
        }

        private static HsCodeRemoteSearchBundle MergeEnrichedRemoteEvidence(
            HsCodeRemoteSearchBundle initialBundle,
            IEnumerable<HsCodeRemoteSearchRecord> records,
            IEnumerable<HsCodeRemoteReplacementEvidence> replacements)
        {
            var deduplicatedRecords = (records ?? [])
                .Where(record => record?.Item != null && !string.IsNullOrWhiteSpace(record.Item.Code))
                .GroupBy(
                    record => string.Join(
                        "|",
                        record.Kind,
                        HsCodeTextHelper.NormalizeCode(record.Item.Code),
                        record.Item.Name?.Trim(),
                        record.Item.Description?.Trim()),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var deduplicatedReplacements = (replacements ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.OldCode))
                .GroupBy(item => HsCodeTextHelper.NormalizeCode(item.OldCode), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    return first with
                    {
                        OldCode = group.Key,
                        RecommendedKeywords = group
                            .SelectMany(item => item.RecommendedKeywords)
                            .Select(HsCodeTextHelper.NormalizeCode)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    };
                })
                .ToList();
            return new HsCodeRemoteSearchBundle(
                initialBundle.Query,
                initialBundle.Source,
                deduplicatedRecords,
                deduplicatedReplacements);
        }

        public async Task<HsCode> FetchDetailAsync(
            HsCode hsCode,
            CancellationToken cancellationToken = default)
        {
            var provider = _remoteProviders.FirstOrDefault(item => item.CanHandleDetailUrl(hsCode?.DetailUrl));
            if (provider == null)
                throw new ArgumentException("没有可处理该HS编码详情地址的联网 Provider。", nameof(hsCode));
            return await provider.FetchDetailAsync(hsCode, cancellationToken).ConfigureAwait(false);
        }

        public async Task<HsCodeRemoteDetailBundle> FetchRemoteDetailEvidenceAsync(
            HsCodeRemoteSearchRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            var provider = _remoteProviders.FirstOrDefault(item => item.CanHandleDetailUrl(record.Item?.DetailUrl));
            if (provider == null)
                throw new ArgumentException("没有可处理该HS编码详情地址的联网 Provider。", nameof(record));
            return await provider.FetchDetailEvidenceAsync(record, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> TryReplaceItemAsync(
            HsCode item,
            IEnumerable<string> recommendedKeywords,
            Action<HsCode> onItemUpdated,
            Action<HsCode> onItemRemoved,
            Action<List<HsCode>> onItemsAdded,
            CancellationToken cancellationToken)
        {
            var replacements = await ResolveReplacementResultsAsync(item, recommendedKeywords, cancellationToken)
                .ConfigureAwait(false);
            if (replacements.Count == 0) return false;
            onItemsAdded?.Invoke(replacements);
            onItemRemoved?.Invoke(item);
            await PopulateReplacementDetailsAsync(
                replacements,
                onItemUpdated,
                onItemRemoved,
                onItemsAdded,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<List<HsCode>> ResolveReplacementResultsAsync(
            HsCode originalItem,
            IEnumerable<string> recommendedKeywords,
            CancellationToken cancellationToken)
        {
            var searchKeywords = (recommendedKeywords ?? [])
                .Append(originalItem?.Code)
                .Select(HsCodeTextHelper.NormalizeCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string keyword in searchKeywords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expanded = await SearchRemoteAsync(keyword, cancellationToken).ConfigureAwait(false);
                var replacements = FilterReplacementResults(originalItem, expanded);
                if (replacements.Count > 0) return replacements;
            }
            return [];
        }

        private async Task PopulateReplacementDetailsAsync(
            IEnumerable<HsCode> items,
            Action<HsCode> onItemUpdated,
            Action<HsCode> onItemRemoved,
            Action<List<HsCode>> onItemsAdded,
            CancellationToken cancellationToken)
        {
            foreach (var item in items ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await FetchDetailAsync(item, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(item.Elements)) onItemUpdated?.Invoke(item);
                }
                catch (HsCodeRemoteExpiredException ex)
                {
                    bool replaced = await TryReplaceItemAsync(
                        item,
                        ex.RecommendedKeywords,
                        onItemUpdated,
                        onItemRemoved,
                        onItemsAdded,
                        cancellationToken).ConfigureAwait(false);
                    if (!replaced) onItemRemoved?.Invoke(item);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "替代HS编码详情补充失败。Code={Code}", item.Code);
                }
            }
        }

        private static List<HsCode> DeduplicateRemoteExamples(IEnumerable<HsCode> items) =>
            (items ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(
                    item => string.Join(
                        "|",
                        HsCodeTextHelper.NormalizeCode(item.Code),
                        (item.Name ?? string.Empty).Trim(),
                        (item.Description ?? string.Empty).Trim()),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
    }
}
