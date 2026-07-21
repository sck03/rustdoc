using Serilog;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        private const int MaxRecommendationDepth = 3;
        private const int MaxAutomaticDetailLookups = 12;
        private static readonly System.Text.RegularExpressions.Regex RecommendedCodeLinkRegex =
            new(@"推荐查询[:：\s]*<a[^>]*>(\d+)</a>", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex RecommendedCodePlainRegex =
            new(@"推荐查询[:：\s]*(\d{4,})", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex AlternativeRecommendedCodePlainRegex =
            new(@"或者[:：\s]*(\d{4,})", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex LeadingHsCodeRegex =
            new(@"^[\d\.]+", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex NormalizeWhitespaceRegex =
            new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex EnglishDescriptionRegex =
            new(@"\[(.*?)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        public async Task ProcessRemainingDetailsAsync(List<HsCode> items, Action<HsCode> onItemUpdated, Action<HsCode> onItemRemoved, Action<List<HsCode>> onItemsAdded = null, CancellationToken cancellationToken = default)
        {
            var pendingItems = items
                .Where(item => !string.IsNullOrEmpty(item.DetailUrl) && string.IsNullOrEmpty(item.Elements))
                .ToList();
            if (pendingItems.Count == 0)
            {
                return;
            }

            try
            {
                foreach (var item in pendingItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await _detailFetchSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested || !string.IsNullOrEmpty(item.Elements))
                        {
                            continue;
                        }

                        await FetchDetailAsync(item, cancellationToken);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (!string.IsNullOrEmpty(item.Elements))
                        {
                            onItemUpdated?.Invoke(item);
                        }
                        else
                        {
                            await TryReplaceItemAsync(item, [], onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken);
                        }
                    }
                    catch (HsCodeRemoteExpiredException ex)
                    {
                        var replaced = await TryReplaceItemAsync(item, ex.RecommendedKeywords, onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken);
                        if (!replaced)
                        {
                            System.Diagnostics.Debug.WriteLine($"Removing expired item during detail fetch: {item.Code}");
                            onItemRemoved?.Invoke(item);
                        }
                    }
                    catch (DetailFetchFailedException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Detail fetch failed for {item.Code}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        if (IsExpiredException(ex))
                        {
                            System.Diagnostics.Debug.WriteLine($"Removing expired item during detail fetch: {item.Code}");
                            onItemRemoved?.Invoke(item);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing item {item.Code}: {ex.Message}");
                        }
                    }
                    finally
                    {
                        _detailFetchSemaphore.Release();
                    }

                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        public async Task<List<HsCode>> SearchRemoteAsync(string keyword, CancellationToken cancellationToken = default)
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
                var rows = await SearchI5a6DirectAsync(keyword).ConfigureAwait(false);
                DateTimeOffset observedAt = DateTimeOffset.UtcNow;
                return new HsCodeRemoteSearchBundle(
                    keyword ?? string.Empty,
                    "i5a6",
                    rows.Select(item => new HsCodeRemoteSearchRecord(
                        item,
                        HsCodeRemoteRecordKind.DeclarationExample,
                        HsCodeTextHelper.IsExpired(item),
                        null,
                        string.Empty,
                        item.DetailUrl ?? string.Empty,
                        observedAt)).ToList(),
                    []);
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
            return HsCodeRemoteSearchBundle.Empty(keyword, string.Join(", ", _remoteProviders.Select(provider => provider.Name)));
        }

        private static async Task<HsCodeRemoteSearchBundle> EnrichRemoteEvidenceAsync(
            IHsCodeRemoteProvider provider,
            HsCodeRemoteSearchBundle initialBundle,
            CancellationToken cancellationToken)
        {
            if (HasCurrentStandardCode(initialBundle.Records))
            {
                return initialBundle;
            }

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
                if (depth > MaxRecommendationDepth || HasCurrentStandardCode(bundle.Records))
                {
                    discoveredStandards.AddRange(bundle.Records.Where(IsStandardRecord));
                    replacementEvidence.AddRange(bundle.ReplacementEvidence);
                    return;
                }

                discoveredStandards.AddRange(bundle.Records.Where(IsStandardRecord));
                replacementEvidence.AddRange(bundle.ReplacementEvidence);
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
                    if (!visitedDetailCodes.Add(oldCode))
                    {
                        continue;
                    }

                    if (remainingDetailLookups <= 0)
                    {
                        Log.Information(
                            "HS编码联网自动详情追踪达到上限。Provider={Provider}, Query={Query}, Limit={Limit}",
                            provider.Name,
                            initialBundle.Query,
                            MaxAutomaticDetailLookups);
                        break;
                    }

                    remainingDetailLookups--;
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

                        if (detail.RecommendedKeywords.Count > 0)
                        {
                            replacementEvidence.Add(new HsCodeRemoteReplacementEvidence(
                                oldCode,
                                detail.RecommendedKeywords,
                                string.IsNullOrWhiteSpace(detail.EvidenceUrl) ? record.EvidenceUrl : detail.EvidenceUrl,
                                detail.ObservedAt));
                            foreach (string recommendedKeyword in detail.RecommendedKeywords)
                            {
                                recommendedKeywords.Add(recommendedKeyword);
                            }
                        }
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

                if (depth >= MaxRecommendationDepth)
                {
                    return;
                }

                foreach (string recommendedKeyword in recommendedKeywords
                             .Select(HsCodeTextHelper.NormalizeCode)
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string identity = NormalizeRemoteLookupIdentity(recommendedKeyword);
                    if (!visitedQueries.Add(identity))
                    {
                        continue;
                    }

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
            (records ?? Enumerable.Empty<HsCodeRemoteSearchRecord>()).Any(record => IsStandardRecord(record) && !record.IsExpired);

        private static bool IsStandardRecord(HsCodeRemoteSearchRecord record) =>
            record?.Kind == HsCodeRemoteRecordKind.StandardCode && record.Item != null;

        private static string NormalizeRemoteLookupIdentity(string value)
        {
            string normalizedCode = HsCodeTextHelper.NormalizeCodeSearchKeyword(value);
            return string.IsNullOrWhiteSpace(normalizedCode)
                ? (value ?? string.Empty).Trim()
                : normalizedCode;
        }

        private static HsCodeRemoteSearchBundle MergeEnrichedRemoteEvidence(
            HsCodeRemoteSearchBundle initialBundle,
            IEnumerable<HsCodeRemoteSearchRecord> records,
            IEnumerable<HsCodeRemoteReplacementEvidence> replacements)
        {
            var deduplicatedRecords = (records ?? Enumerable.Empty<HsCodeRemoteSearchRecord>())
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
            var deduplicatedReplacements = (replacements ?? Enumerable.Empty<HsCodeRemoteReplacementEvidence>())
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

        internal Task<List<HsCode>> SearchI5a6DirectAsync(string keyword) => SearchRemoteCoreAsync(
            keyword, new HashSet<string>(StringComparer.OrdinalIgnoreCase), depth: 0);

        public async Task<HsCode> FetchDetailAsync(HsCode hsCode, CancellationToken cancellationToken = default)
        {
            if (_remoteProviders.Count > 0)
            {
                var provider = _remoteProviders.FirstOrDefault(item => item.CanHandleDetailUrl(hsCode?.DetailUrl));
                if (provider == null) throw new ArgumentException("没有可处理该HS编码详情地址的联网 Provider。", nameof(hsCode));
                return await provider.FetchDetailAsync(hsCode, cancellationToken).ConfigureAwait(false);
            }
            return await FetchI5a6DetailDirectAsync(hsCode).ConfigureAwait(false);
        }

        public async Task<HsCodeRemoteDetailBundle> FetchRemoteDetailEvidenceAsync(
            HsCodeRemoteSearchRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (_remoteProviders.Count > 0)
            {
                var provider = _remoteProviders.FirstOrDefault(item => item.CanHandleDetailUrl(record.Item?.DetailUrl));
                if (provider == null) throw new ArgumentException("没有可处理该HS编码详情地址的联网 Provider。", nameof(record));
                return await provider.FetchDetailEvidenceAsync(record, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var item = await FetchI5a6DetailDirectAsync(record.Item).ConfigureAwait(false);
                return new HsCodeRemoteDetailBundle(item, false, record.InstanceCount, [], [], string.Empty, [], [], record.EvidenceUrl, DateTimeOffset.UtcNow);
            }
            catch (HsCodeRemoteExpiredException ex)
            {
                return new HsCodeRemoteDetailBundle(record.Item, true, record.InstanceCount, ex.RecommendedKeywords, [], string.Empty, [], [], record.EvidenceUrl, DateTimeOffset.UtcNow);
            }
        }

        internal async Task<HsCode> FetchI5a6DetailDirectAsync(HsCode hsCode)
        {
            if (string.IsNullOrEmpty(hsCode?.DetailUrl))
            {
                return hsCode;
            }

            if (!Uri.TryCreate(hsCode.DetailUrl, UriKind.Absolute, out var detailUri) ||
                detailUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(detailUri.Host, "www.i5a6.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("HS编码详情地址不是受信任的联网数据源。", nameof(hsCode));
            }

            try
            {
                await Task.Delay(Random.Shared.Next(500, 1500));

                using var stream = await _httpClient.GetStreamAsync(hsCode.DetailUrl);
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.Load(stream, true);
                var recommendedKeywords = ExtractRecommendedSearchKeywords(doc.DocumentNode.OuterHtml);

                if (doc.DocumentNode.SelectSingleNode("//div[@id='hscode-detail']//span[contains(normalize-space(.), '已作废')]") != null)
                {
                    throw new HsCodeRemoteExpiredException(recommendedKeywords);
                }

                string GetValue(string label)
                {
                    var strategies = new[]
                    {
                        $"//td[normalize-space(text())='{label}']/following-sibling::td[1]",
                        $"//th[normalize-space(text())='{label}']/following-sibling::td[1]",
                        $"//td[contains(text(), '{label}')]/following-sibling::td[1]",
                        $"//th[contains(text(), '{label}')]/following-sibling::td[1]"
                    };

                    foreach (var xpath in strategies)
                    {
                        var node = doc.DocumentNode.SelectSingleNode(xpath);
                        if (node == null)
                        {
                            continue;
                        }

                        var text = System.Net.WebUtility.HtmlDecode(node.InnerText).Trim();
                        if (text == label || text.Replace(":", string.Empty).Trim() == label)
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(text))
                        {
                            return text;
                        }
                    }

                    return null;
                }

                var fetchedCode = GetValue("商品编码");
                if (!string.IsNullOrEmpty(fetchedCode))
                {
                    if (HsCodeTextHelper.IsExpiredText(fetchedCode))
                    {
                        throw new HsCodeRemoteExpiredException(recommendedKeywords);
                    }

                    hsCode.Code = HsCodeTextHelper.NormalizeCode(fetchedCode);
                }

                var fetchedName = GetValue("商品名称");
                if (!string.IsNullOrEmpty(fetchedName))
                {
                    if (HsCodeTextHelper.IsExpiredText(fetchedName))
                    {
                        throw new HsCodeRemoteExpiredException(recommendedKeywords);
                    }

                    hsCode.Name = fetchedName;
                }

                hsCode.Elements = GetValue("申报要素");
                hsCode.Unit = GetValue("法定第一单位");
                hsCode.RebateRate = GetValue("出口退税率");
                hsCode.SupervisionConditions = GetValue("海关监管条件");
                hsCode.InspectionCategory = GetValue("检验检疫类别");

                var fetchedEnglishName = GetValue("英文名称");
                if (!string.IsNullOrEmpty(fetchedEnglishName))
                {
                    hsCode.Description = fetchedEnglishName;
                }

                bool hasAnyParsedField =
                    !string.IsNullOrWhiteSpace(fetchedCode) ||
                    !string.IsNullOrWhiteSpace(fetchedName) ||
                    !string.IsNullOrWhiteSpace(hsCode.Elements) ||
                    !string.IsNullOrWhiteSpace(hsCode.Unit) ||
                    !string.IsNullOrWhiteSpace(hsCode.RebateRate) ||
                    !string.IsNullOrWhiteSpace(hsCode.SupervisionConditions) ||
                    !string.IsNullOrWhiteSpace(hsCode.InspectionCategory) ||
                    !string.IsNullOrWhiteSpace(fetchedEnglishName);

                if (!hasAnyParsedField)
                {
                    throw new DetailFetchFailedException("未能从详情页解析到有效字段。");
                }

                hsCode.UpdateTime = DateTime.Now;
                hsCode.Status = "Active";
                hsCode.SourceName = "i5a6（第三方参考）";
                hsCode.LastVerifiedAt = DateTime.Now;
                return hsCode;
            }
            catch (HsCodeRemoteExpiredException)
            {
                throw;
            }
            catch (DetailFetchFailedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Detail Fetch Failed ({hsCode.DetailUrl}): {ex.Message}");
                throw new DetailFetchFailedException($"详情抓取失败: {ex.Message}", ex);
            }
        }

        internal static List<string> ExtractRecommendedSearchKeywords(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);
            var plainText = NormalizeWhitespaceRegex.Replace(
                System.Net.WebUtility.HtmlDecode(document.DocumentNode.InnerText ?? string.Empty),
                " ");

            var codes = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void AppendCode(List<string> target, HashSet<string> seenCodes, string rawCode)
            {
                var normalizedCode = HsCodeTextHelper.NormalizeCode(rawCode);
                if (!string.IsNullOrWhiteSpace(normalizedCode) && seenCodes.Add(normalizedCode))
                {
                    target.Add(normalizedCode);
                }
            }

            foreach (System.Text.RegularExpressions.Match match in RecommendedCodeLinkRegex.Matches(html))
            {
                AppendCode(codes, seen, match.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match match in RecommendedCodePlainRegex.Matches(plainText))
            {
                AppendCode(codes, seen, match.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match match in AlternativeRecommendedCodePlainRegex.Matches(plainText))
            {
                AppendCode(codes, seen, match.Groups[1].Value);
            }

            return codes;
        }

        private async Task<bool> TryReplaceItemAsync(
            HsCode item,
            IEnumerable<string> recommendedKeywords,
            Action<HsCode> onItemUpdated,
            Action<HsCode> onItemRemoved,
            Action<List<HsCode>> onItemsAdded,
            CancellationToken cancellationToken)
        {
            var replacementResults = await ResolveReplacementResultsAsync(item, recommendedKeywords, cancellationToken);
            if (replacementResults.Count == 0)
            {
                return false;
            }

            onItemsAdded?.Invoke(replacementResults);
            onItemRemoved?.Invoke(item);
            await PopulateReplacementDetailsAsync(replacementResults, onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken);
            return true;
        }

        private async Task<List<HsCode>> ResolveReplacementResultsAsync(
            HsCode originalItem,
            IEnumerable<string> recommendedKeywords,
            CancellationToken cancellationToken)
        {
            var searchKeywords = (recommendedKeywords ?? Enumerable.Empty<string>())
                .Append(originalItem?.Code)
                .Select(HsCodeTextHelper.NormalizeCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var keyword in searchKeywords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var expandedResults = await SearchRemoteAsync(keyword);
                var replacementResults = FilterReplacementResults(originalItem, expandedResults);
                if (replacementResults.Count > 0)
                {
                    return replacementResults;
                }
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
            foreach (var expandedItem in items ?? Enumerable.Empty<HsCode>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(300, cancellationToken);

                try
                {
                    await FetchDetailAsync(expandedItem, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(expandedItem.Elements))
                    {
                        onItemUpdated?.Invoke(expandedItem);
                    }
                    else
                    {
                        await TryReplaceItemAsync(expandedItem, [], onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken);
                    }
                }
                catch (HsCodeRemoteExpiredException ex)
                {
                    var replaced = await TryReplaceItemAsync(expandedItem, ex.RecommendedKeywords, onItemUpdated, onItemRemoved, onItemsAdded, cancellationToken);
                    if (!replaced)
                    {
                        onItemRemoved?.Invoke(expandedItem);
                    }
                }
                catch (DetailFetchFailedException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Replacement detail fetch failed for {expandedItem.Code}: {ex.Message}");
                }
                catch (Exception ex) when (IsExpiredException(ex))
                {
                    onItemRemoved?.Invoke(expandedItem);
                }
            }
        }

        private static bool IsExpiredException(Exception ex)
        {
            return ex != null &&
                   (HsCodeTextHelper.IsExpiredText(ex.Message) ||
                    HsCodeTextHelper.IsExpiredText(ex.InnerException?.Message));
        }

        private async Task<List<HsCode>> SearchRemoteCoreAsync(
            string keyword,
            HashSet<string> visitedKeywords,
            int depth)
        {
            var results = new List<HsCode>();
            var queryKeyword = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();
            if (string.IsNullOrWhiteSpace(queryKeyword))
            {
                return results;
            }

            var normalizedKeyword = HsCodeTextHelper.NormalizeCodeSearchKeyword(queryKeyword);
            var keywordIdentity = string.IsNullOrWhiteSpace(normalizedKeyword)
                ? queryKeyword
                : normalizedKeyword;

            if (depth > MaxRecommendationDepth)
            {
                Log.Warning("HS 编码远程推荐递归超过最大深度，已停止继续展开。Keyword={Keyword}", queryKeyword);
                return results;
            }

            if (!visitedKeywords.Add(keywordIdentity))
            {
                return results;
            }

            try
            {
                var url = $"https://www.i5a6.com/hscode/key/{System.Net.WebUtility.UrlEncode(queryKeyword)}";

                await ExecuteWithRetryAsync(async () =>
                {
                    using var stream = await _httpClient.GetStreamAsync(url);
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.Load(stream, true);

                    var table = doc.DocumentNode.SelectSingleNode("//*[@id='resultfind']/following::table[1]")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'table-responsive')]//table[.//a[contains(@href,'/hscode/detail/')]]")
                        ?? doc.DocumentNode.SelectSingleNode("//table[contains(., 'HS编码') and .//a[contains(@href,'/hscode/detail/')]]");
                    var recommendedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (table != null)
                    {
                        var rows = table.SelectNodes(".//tr");
                        if (rows != null)
                        {
                            int codeColumnIndex = 0;
                            int nameColumnIndex = 1;
                            int descriptionColumnIndex = -1;
                            var headerCells = rows[0].SelectNodes("th|td");
                            if (headerCells != null)
                            {
                                for (int index = 0; index < headerCells.Count; index++)
                                {
                                    var headerText = headerCells[index].InnerText.Trim();
                                    if (headerText.Contains("HS编码") || headerText.Contains("Code"))
                                    {
                                        codeColumnIndex = index;
                                    }
                                    else if (headerText.Contains("商品名称") || headerText.Contains("品名") || headerText.Contains("Name"))
                                    {
                                        nameColumnIndex = index;
                                    }
                                    else if (headerText.Contains("商品规格") || headerText.Contains("规格") || headerText.Contains("Description"))
                                    {
                                        descriptionColumnIndex = index;
                                    }
                                }
                            }

                            foreach (var row in rows.Skip(1))
                            {
                                var cells = row.SelectNodes("td");
                                if (cells == null || cells.Count <= Math.Max(codeColumnIndex, nameColumnIndex))
                                {
                                    continue;
                                }

                                var fullCodeText = cells[codeColumnIndex].InnerText.Trim();
                                var nameRaw = cells[nameColumnIndex].InnerText.Trim();
                                if (fullCodeText.Contains("HS编码"))
                                {
                                    continue;
                                }

                                bool isExpired = HsCodeTextHelper.IsExpiredText(fullCodeText) || HsCodeTextHelper.IsExpiredText(nameRaw);
                                if (isExpired)
                                {
                                    foreach (var recommendationLinkNode in (cells[codeColumnIndex].SelectNodes(".//a") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
                                        .Concat(cells[nameColumnIndex].SelectNodes(".//a") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>()))
                                    {
                                        var hrefMatch = System.Text.RegularExpressions.Regex.Match(
                                            recommendationLinkNode.GetAttributeValue("href", string.Empty),
                                            @"/hscode/key/(\d{4,})",
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (hrefMatch.Success)
                                        {
                                            recommendedCodes.Add(hrefMatch.Groups[1].Value);
                                        }
                                    }

                                    var textToCheck = cells[codeColumnIndex].InnerHtml + " " + cells[nameColumnIndex].InnerHtml;
                                    var match = RecommendedCodeLinkRegex.Match(textToCheck);
                                    if (!match.Success)
                                    {
                                        match = RecommendedCodePlainRegex.Match(textToCheck);
                                    }

                                    if (match.Success)
                                    {
                                        recommendedCodes.Add(match.Groups[1].Value);
                                    }

                                    continue;
                                }

                                var cleanCode = fullCodeText;
                                var codeMatch = LeadingHsCodeRegex.Match(fullCodeText);
                                if (codeMatch.Success)
                                {
                                    cleanCode = codeMatch.Value;
                                }

                                var name = NormalizeWhitespaceRegex.Replace(nameRaw, " ");
                                var description = descriptionColumnIndex >= 0 && cells.Count > descriptionColumnIndex
                                    ? NormalizeWhitespaceRegex.Replace(cells[descriptionColumnIndex].InnerText.Trim(), " ")
                                    : string.Empty;
                                var englishMatch = EnglishDescriptionRegex.Match(name);
                                if (string.IsNullOrWhiteSpace(description) && englishMatch.Success)
                                {
                                    description = englishMatch.Groups[1].Value;
                                }

                                var hsCode = new HsCode
                                {
                                    Code = HsCodeTextHelper.NormalizeCode(cleanCode),
                                    Name = name,
                                    Description = description,
                                    UpdateTime = DateTime.Now
                                };

                                var linkNode = cells[codeColumnIndex].SelectSingleNode(".//a")
                                    ?? cells[nameColumnIndex].SelectSingleNode(".//a")
                                    ?? (cells.Count > 3 ? cells[3].SelectSingleNode(".//a") : null);
                                if (linkNode != null)
                                {
                                    hsCode.DetailUrl = NormalizeDetailUrl(linkNode.GetAttributeValue("href", string.Empty));
                                }

                                results.Add(hsCode);
                            }
                        }
                    }

                    if (results.Count == 0)
                    {
                        var cardLinks = doc.DocumentNode.SelectNodes(
                            "//a[contains(@href,'/hscode/detail/')][.//*[contains(concat(' ',normalize-space(@class),' '),' dealcard ')]]");
                        if (cardLinks != null)
                        {
                            foreach (var link in cardLinks)
                            {
                                var card = link.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' dealcard ')]");
                                var textBlocks = card?.SelectNodes(
                                    ".//*[contains(concat(' ',normalize-space(@class),' '),' title ') and contains(concat(' ',normalize-space(@class),' '),' text-block ')]");
                                string code = card?.SelectSingleNode(
                                    ".//*[contains(concat(' ',normalize-space(@class),' '),' dealcard-brand ')]")?.InnerText?.Trim() ?? string.Empty;
                                string name = textBlocks?.ElementAtOrDefault(0)?.InnerText?.Trim() ?? string.Empty;
                                string spec = textBlocks?.ElementAtOrDefault(1)?.InnerText?.Trim() ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(code)
                                    || string.IsNullOrWhiteSpace(name)
                                    || HsCodeTextHelper.IsExpiredText(code)
                                    || HsCodeTextHelper.IsExpiredText(name))
                                {
                                    continue;
                                }

                                results.Add(new HsCode
                                {
                                    Code = HsCodeTextHelper.NormalizeCode(code),
                                    Name = System.Net.WebUtility.HtmlDecode(name),
                                    Description = System.Net.WebUtility.HtmlDecode(spec),
                                    DetailUrl = NormalizeDetailUrl(link.GetAttributeValue("href", string.Empty)),
                                    UpdateTime = DateTime.Now
                                });
                                if (results.Count >= 50)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    var exampleTable = doc.DocumentNode.SelectSingleNode("//*[@id='hscasefind']//table")
                        ?? doc.DocumentNode.SelectSingleNode("//*[@id='hssbsl']/following-sibling::*[1]//table")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(., '申报实例查询结果')]/following-sibling::div//table")
                        ?? doc.DocumentNode.SelectSingleNode("//table[contains(., '商品规格')]");
                    AppendDeclarationExamples(exampleTable, results);

                    foreach (var recommendedCode in recommendedCodes)
                    {
                        var normalizedRecommendedCode = HsCodeTextHelper.NormalizeCode(recommendedCode);
                        if (string.IsNullOrWhiteSpace(normalizedRecommendedCode) ||
                            string.Equals(normalizedRecommendedCode, keywordIdentity, StringComparison.OrdinalIgnoreCase) ||
                            visitedKeywords.Contains(normalizedRecommendedCode) ||
                            results.Any(item => string.Equals(HsCodeTextHelper.NormalizeCode(item.Code), normalizedRecommendedCode, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        try
                        {
                            if (recommendedCodes.Count > 1)
                            {
                                await Task.Delay(500);
                            }

                            results.AddRange(
                                await SearchRemoteCoreAsync(normalizedRecommendedCode, visitedKeywords, depth + 1));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to fetch recommended code {normalizedRecommendedCode}: {ex.Message}");
                        }
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Remote HS Code Search Failed");
                System.Diagnostics.Debug.WriteLine($"Remote Search Failed: {ex.Message}");
            }

            return DeduplicateRemoteExamples(results);
        }

        private static void AppendDeclarationExamples(HtmlAgilityPack.HtmlNode exampleTable, List<HsCode> results)
        {
            if (exampleTable == null || results.Count >= 50)
            {
                return;
            }

            var rows = exampleTable.SelectNodes(".//tr");
            if (rows == null || rows.Count < 2)
            {
                return;
            }

            int codeColumnIndex = 0;
            int nameColumnIndex = 1;
            int descriptionColumnIndex = 2;
            var headerCells = rows[0].SelectNodes("th|td");
            if (headerCells != null)
            {
                for (int index = 0; index < headerCells.Count; index++)
                {
                    var headerText = NormalizeWhitespaceRegex.Replace(headerCells[index].InnerText.Trim(), " ");
                    if (headerText.Contains("HS编码") || headerText.Contains("商品编码") || headerText.Contains("Code", StringComparison.OrdinalIgnoreCase))
                    {
                        codeColumnIndex = index;
                    }
                    else if (headerText.Contains("商品名称") || headerText.Contains("品名") || headerText.Contains("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        nameColumnIndex = index;
                    }
                    else if (headerText.Contains("商品规格") || headerText.Contains("规格") || headerText.Contains("Description", StringComparison.OrdinalIgnoreCase))
                    {
                        descriptionColumnIndex = index;
                    }
                }
            }

            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count <= Math.Max(codeColumnIndex, nameColumnIndex))
                {
                    continue;
                }

                var code = NormalizeWhitespaceRegex.Replace(cells[codeColumnIndex].InnerText.Trim(), " ");
                var name = NormalizeWhitespaceRegex.Replace(cells[nameColumnIndex].InnerText.Trim(), " ");
                var spec = cells.Count > descriptionColumnIndex
                    ? NormalizeWhitespaceRegex.Replace(cells[descriptionColumnIndex].InnerText.Trim(), " ")
                    : string.Empty;
                if (code.Contains("HS编码") || HsCodeTextHelper.IsExpiredText(code) || HsCodeTextHelper.IsExpiredText(name))
                {
                    continue;
                }

                var normalizedCode = HsCodeTextHelper.NormalizeCode(code);
                if (string.IsNullOrWhiteSpace(normalizedCode) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var hsCode = new HsCode
                {
                    Code = normalizedCode,
                    Name = name,
                    Description = spec,
                    UpdateTime = DateTime.Now
                };

                var linkNode = cells[codeColumnIndex].SelectSingleNode(".//a");
                if (linkNode != null)
                {
                    hsCode.DetailUrl = NormalizeDetailUrl(linkNode.GetAttributeValue("href", string.Empty));
                }

                results.Add(hsCode);
                if (results.Count >= 50)
                {
                    break;
                }
            }
        }

        private static List<HsCode> DeduplicateRemoteExamples(IEnumerable<HsCode> items)
        {
            return (items ?? Enumerable.Empty<HsCode>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(
                    item => string.Join("|",
                        HsCodeTextHelper.NormalizeCode(item.Code),
                        (item.Name ?? string.Empty).Trim(),
                        (item.Description ?? string.Empty).Trim()),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string NormalizeDetailUrl(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            if (href.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + href;
            }

            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            return $"https://www.i5a6.com{href}";
        }

        internal sealed class DetailFetchFailedException : InvalidOperationException
        {
            public DetailFetchFailedException(string message, Exception innerException = null)
                : base(message, innerException)
            {
            }
        }
    }
}
