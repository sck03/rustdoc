using Serilog;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        private const int MaxRecommendationDepth = 3;
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
                            await SaveAsync(item);
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
            if (_remoteProviders.Count == 0)
            {
                return await SearchI5a6DirectAsync(keyword).ConfigureAwait(false);
            }

            var merged = new List<HsCode>();
            foreach (var provider in _remoteProviders)
            {
                try
                {
                    var rows = await provider.SearchAsync(keyword, cancellationToken).ConfigureAwait(false);
                    AppendSearchResults(merged, rows);
                    if (merged.Count > 0) break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "HS编码联网 Provider 查询失败。Provider={Provider}", provider.Name);
                }
            }
            return DeduplicateByCode(merged);
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
                        await SaveAsync(expandedItem);
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

                    var table = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'table-responsive')]//table")
                        ?? doc.DocumentNode.SelectSingleNode("//table[contains(., 'HS编码')]");
                    var recommendedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (table != null)
                    {
                        var rows = table.SelectNodes(".//tr");
                        if (rows != null)
                        {
                            int codeColumnIndex = 0;
                            int nameColumnIndex = 1;
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
                                var description = string.Empty;
                                var englishMatch = EnglishDescriptionRegex.Match(name);
                                if (englishMatch.Success)
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

                    foreach (var recommendedCode in recommendedCodes)
                    {
                        var normalizedRecommendedCode = HsCodeTextHelper.NormalizeCode(recommendedCode);
                        if (string.IsNullOrWhiteSpace(normalizedRecommendedCode) ||
                            string.Equals(normalizedRecommendedCode, keywordIdentity, StringComparison.OrdinalIgnoreCase) ||
                            visitedKeywords.Contains(normalizedRecommendedCode) ||
                            results.Any(item => string.Equals(HsCodeTextHelper.NormalizeCode(item.Code), normalizedRecommendedCode, StringComparison.OrdinalIgnoreCase)) ||
                            results.Any(item => HsCodeTextHelper.NormalizeCode(item.Code).StartsWith(normalizedRecommendedCode, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        try
                        {
                            if (recommendedCodes.Count > 1)
                            {
                                await Task.Delay(500);
                            }

                            AppendSearchResults(
                                results,
                                await SearchRemoteCoreAsync(normalizedRecommendedCode, visitedKeywords, depth + 1));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to fetch recommended code {normalizedRecommendedCode}: {ex.Message}");
                        }
                    }

                    if (results.Count == 0)
                    {
                        var exampleTable = doc.DocumentNode.SelectSingleNode("//div[contains(., '申报实例查询结果')]/following-sibling::div//table")
                            ?? doc.DocumentNode.SelectSingleNode("//table[contains(., '商品规格')]");
                        if (exampleTable != null)
                        {
                            var rows = exampleTable.SelectNodes(".//tr");
                            if (rows != null)
                            {
                                foreach (var row in rows.Skip(1))
                                {
                                    var cells = row.SelectNodes("td");
                                    if (cells == null || cells.Count < 2)
                                    {
                                        continue;
                                    }

                                    var code = cells[0].InnerText.Trim();
                                    var name = cells[1].InnerText.Trim();
                                    var spec = cells.Count > 2 ? cells[2].InnerText.Trim() : string.Empty;
                                    if (code.Contains("HS编码") || HsCodeTextHelper.IsExpiredText(code) || HsCodeTextHelper.IsExpiredText(name))
                                    {
                                        continue;
                                    }

                                    var hsCode = new HsCode
                                    {
                                        Code = HsCodeTextHelper.NormalizeCode(code),
                                        Name = name,
                                        Description = spec,
                                        UpdateTime = DateTime.Now
                                    };

                                    var linkNode = cells[0].SelectSingleNode(".//a");
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

            return DeduplicateByCode(results);
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
