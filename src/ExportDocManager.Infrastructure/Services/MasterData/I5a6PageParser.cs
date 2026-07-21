using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;
using HtmlAgilityPack;

namespace ExportDocManager.Services.MasterData;

internal static class I5a6PageParser
{
    private const string BaseUrl = "https://www.i5a6.com";
    private static readonly Regex LeadingCodeRegex = new(@"^[\d\.]+", RegexOptions.Compiled);
    private static readonly Regex CountRegex = new(@"([\d,]+)\s*条", RegexOptions.Compiled);
    private static readonly Regex RecommendedCodeRegex = new(@"(?:推荐查询|或者)[:：\s]*(\d{4,})", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EnglishDescriptionRegex = new(@"\[(.*?)\]", RegexOptions.Compiled);

    public static HsCodeRemoteSearchBundle ParseSearchPage(
        string html,
        string query,
        string source = "i5a6",
        DateTimeOffset? observedAt = null)
    {
        var document = Load(html);
        DateTimeOffset timestamp = observedAt ?? DateTimeOffset.UtcNow;
        var records = new List<HsCodeRemoteSearchRecord>();
        var replacements = new List<HsCodeRemoteReplacementEvidence>();

        var standardTable = SelectStandardTable(document);
        ParseStandardTable(standardTable, records, replacements, timestamp, source);
        records.AddRange(ParseDeclarationTable(SelectDeclarationTable(document), timestamp, source: source));

        if (records.Count == 0)
        {
            ParseMobileCards(document, records, timestamp, source);
        }

        return new HsCodeRemoteSearchBundle(
            query ?? string.Empty,
            source ?? string.Empty,
            DeduplicateRecords(records),
            replacements
                .Where(item => !string.IsNullOrWhiteSpace(item.OldCode) && item.RecommendedKeywords.Count > 0)
                .GroupBy(item => item.OldCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList());
    }

    public static HsCodeRemoteDetailBundle ParseDetailPage(
        string html,
        HsCode seed,
        int? instanceCount = null,
        string evidenceUrl = "",
        DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var document = Load(html);
        DateTimeOffset timestamp = observedAt ?? DateTimeOffset.UtcNow;
        var detailRoot = SelectDetailRoot(document);
        var fields = ReadFieldTable(detailRoot);
        string Get(params string[] labels) => labels
            .Select(label => fields.GetValueOrDefault(label))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        string code = Get("商品编码", "HS编码");
        string name = Get("商品名称", "品名");
        string elements = Get("申报要素", "规范申报要素");
        string englishName = Get("英文名称", "英文品名");
        var item = Clone(seed);
        if (!string.IsNullOrWhiteSpace(code))
            item.Code = HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(code).Value);
        if (!string.IsNullOrWhiteSpace(name)) item.Name = name;
        item.Elements = Prefer(elements, item.Elements);
        item.Unit = Prefer(Get("法定第一单位", "第一法定单位"), item.Unit);
        item.RebateRate = Prefer(Get("出口退税率", "退税率"), item.RebateRate);
        item.SupervisionConditions = Prefer(Get("海关监管条件", "监管条件"), item.SupervisionConditions);
        item.InspectionCategory = Prefer(Get("检验检疫类别", "检验检疫"), item.InspectionCategory);
        item.NormalTariffRate = Prefer(Get("普通进口税率", "普通税率"), item.NormalTariffRate);
        item.PreferentialTariffRate = Prefer(Get("最惠国进口税率", "优惠税率", "最惠国税率"), item.PreferentialTariffRate);
        item.ConsumptionTaxRate = Prefer(Get("消费税率"), item.ConsumptionTaxRate);
        item.ValueAddedTaxRate = Prefer(Get("增值税率", "进口增值税率"), item.ValueAddedTaxRate);
        item.ExportTariffRate = Prefer(Get("出口关税率", "出口税率"), item.ExportTariffRate);
        item.Description = Prefer(englishName, item.Description);
        bool isExpired = HsCodeTextHelper.IsExpiredText(detailRoot.InnerText);
        item.Status = isExpired ? "Obsolete" : "Active";
        item.SourceName = "i5a6（第三方参考）";
        item.LastVerifiedAt = timestamp.LocalDateTime;
        item.UpdateTime = timestamp.LocalDateTime;

        var recommendedKeywords = ExtractRecommendedKeywords(document.DocumentNode.OuterHtml);
        string exampleEvidenceUrl = string.IsNullOrWhiteSpace(item.DetailUrl)
            ? evidenceUrl
            : item.DetailUrl.Split('#')[0] + "#sbsl";
        var examples = ParseDeclarationTable(
            SelectFollowingSemanticTable(document, "申报实例汇总", "申报实例", "申报案例"),
            timestamp,
            exampleEvidenceUrl,
            fallbackCode: item.Code,
            source: "i5a6");

        string postalTaxCode = ReadHeadingValue(document, "个人行邮税号");
        var ciqEntries = ParseReferenceEntries(SelectFollowingSemanticTable(document, "10位HS编码+3位CIQ", "CIQ代码"));
        var classificationEntries = ParseReferenceEntries(SelectFollowingSemanticTable(document, "所属分类及章节", "所属分类", "章节、品目"));

        return new HsCodeRemoteDetailBundle(
            item,
            isExpired,
            instanceCount,
            recommendedKeywords,
            examples,
            postalTaxCode,
            ciqEntries,
            classificationEntries,
            string.IsNullOrWhiteSpace(evidenceUrl) ? item.DetailUrl ?? string.Empty : evidenceUrl,
            timestamp);
    }

    internal static IReadOnlyList<string> ExtractRecommendedKeywords(string html)
    {
        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var document = Load(html);
        foreach (var link in document.DocumentNode.SelectNodes("//a[contains(@href, '/hscode/key/')]") ?? Enumerable.Empty<HtmlNode>())
        {
            string href = link.GetAttributeValue("href", string.Empty);
            string raw = Regex.Match(href, @"/hscode/key/(\d{4,})", RegexOptions.IgnoreCase).Groups[1].Value;
            AppendCode(raw);
            foreach (Match match in Regex.Matches(link.InnerText ?? string.Empty, @"\d{4,}")) AppendCode(match.Value);
        }

        string text = NormalizeText(document.DocumentNode.InnerText);
        foreach (Match match in RecommendedCodeRegex.Matches(text)) AppendCode(match.Groups[1].Value);
        return codes;

        void AppendCode(string raw)
        {
            string normalized = HsCodeTextHelper.NormalizeCode(raw);
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized)) codes.Add(normalized);
        }
    }

    internal static IReadOnlyList<HsCodeRemoteSearchRecord> ParseLegacySimpleTable(
        string html,
        string source,
        DateTimeOffset? observedAt = null)
    {
        var document = Load(html);
        var table = document.DocumentNode.SelectNodes("//table")?.FirstOrDefault(candidate =>
        {
            var headers = ReadHeaders(candidate.SelectSingleNode(".//tr"));
            return FindHeader(headers, "HS编码", "商品编码", "Code") >= 0 &&
                FindHeader(headers, "商品名称", "品名", "Name") >= 0;
        });
        return ParseDeclarationTable(
            table,
            observedAt ?? DateTimeOffset.UtcNow,
            source: source);
    }

    private static HtmlDocument Load(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        return document;
    }

    private static HtmlNode SelectStandardTable(HtmlDocument document) =>
        SelectHighestScoringTable(document, ScoreStandardTable, minimumScore: 11);

    private static bool HasStandardHeaders(HtmlNode table)
    {
        if (table == null) return false;
        return ScoreStandardTable(table) >= 11;
    }

    private static HtmlNode SelectDeclarationTable(HtmlDocument document) =>
        SelectHighestScoringTable(document, ScoreDeclarationTable, minimumScore: 11);

    private static HtmlNode SelectHighestScoringTable(
        HtmlDocument document,
        Func<HtmlNode, int> scorer,
        int minimumScore)
    {
        return document.DocumentNode.SelectNodes("//table")?
            .Select(table => new { Table = table, Score = scorer(table) })
            .Where(item => item.Score >= minimumScore)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Table.SelectNodes(".//tr")?.Count ?? 0)
            .Select(item => item.Table)
            .FirstOrDefault();
    }

    private static int ScoreStandardTable(HtmlNode table)
    {
        var headerRow = FindHeaderRow(table, headers => FindCodeHeader(headers) >= 0 && FindNameHeader(headers) >= 0);
        if (headerRow == null) return 0;
        var headers = ReadHeaders(headerRow);
        int score = 8;
        if (FindHeader(headers, "实例汇总", "实例数量", "案例数量", "申报实例") >= 0) score += 7;
        if (FindHeader(headers, "申报要素", "退税", "编码对比", "税率", "详情") >= 0) score += 3;
        if (table.SelectSingleNode(".//a[contains(@href, '#sbsl')]") != null) score += 4;
        string context = ReadTableSemanticContext(table);
        if (context.Contains("相关HS编码", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("税则", StringComparison.OrdinalIgnoreCase)) score += 3;
        if (FindSpecificationHeader(headers) >= 0 &&
            FindHeader(headers, "实例汇总", "实例数量", "案例数量", "申报实例") < 0) score -= 5;
        return score;
    }

    private static int ScoreDeclarationTable(HtmlNode table)
    {
        var headerRow = FindHeaderRow(table, headers => FindCodeHeader(headers) >= 0 && FindNameHeader(headers) >= 0);
        if (headerRow == null) return 0;
        var headers = ReadHeaders(headerRow);
        int score = 8;
        if (FindSpecificationHeader(headers) >= 0) score += 7;
        string context = ReadTableSemanticContext(table);
        if (context.Contains("申报实例", StringComparison.OrdinalIgnoreCase) ||
            context.Contains("申报案例", StringComparison.OrdinalIgnoreCase)) score += 6;
        if (FindHeader(headers, "实例汇总", "实例数量", "案例数量", "编码对比") >= 0) score -= 7;
        return score;
    }

    private static HtmlNode SelectDetailRoot(HtmlDocument document)
    {
        var identified = document.DocumentNode.SelectSingleNode("//*[@id='hscode-detail']");
        if (identified != null) return identified;
        return SelectHighestScoringTable(document, table =>
        {
            string text = NormalizeText(table.InnerText);
            int score = text.Contains("商品编码", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            if (text.Contains("商品名称", StringComparison.OrdinalIgnoreCase)) score += 4;
            if (text.Contains("申报要素", StringComparison.OrdinalIgnoreCase)) score += 4;
            if (text.Contains("法定第一单位", StringComparison.OrdinalIgnoreCase)) score += 2;
            return score;
        }, minimumScore: 9) ?? document.DocumentNode;
    }

    private static HtmlNode SelectFollowingSemanticTable(HtmlDocument document, params string[] labels)
    {
        foreach (var node in document.DocumentNode.SelectNodes("//div|//h1|//h2|//h3|//h4|//caption|//header|//section") ?? Enumerable.Empty<HtmlNode>())
        {
            string text = ReadOwnText(node);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 160 ||
                !labels.Any(label => text.Contains(label, StringComparison.OrdinalIgnoreCase))) continue;
            var table = node.SelectSingleNode("following::table[1]");
            if (table != null) return table;
        }
        return null;
    }

    private static HtmlNode FindHeaderRow(HtmlNode table, Func<IReadOnlyList<string>, bool> predicate)
    {
        return table?.SelectNodes(".//tr")?
            .Take(8)
            .FirstOrDefault(row => predicate(ReadHeaders(row)));
    }

    private static int FindCodeHeader(IReadOnlyList<string> headers) =>
        FindHeader(headers, "HS编码", "海关编码", "商品编码", "税则号列", "税号", "Code");

    private static int FindNameHeader(IReadOnlyList<string> headers) =>
        FindHeader(headers, "商品名称", "货品名称", "货物名称", "品名", "Name");

    private static int FindSpecificationHeader(IReadOnlyList<string> headers) =>
        FindHeader(headers, "商品规格", "规格型号", "规格与申报要素", "申报规格", "规格", "Description");

    private static string ReadTableSemanticContext(HtmlNode table)
    {
        var parts = new List<string>
        {
            table?.Id ?? string.Empty,
            table?.GetAttributeValue("class", string.Empty) ?? string.Empty,
            table?.ParentNode?.Id ?? string.Empty,
            table?.ParentNode?.GetAttributeValue("class", string.Empty) ?? string.Empty
        };
        var sibling = table?.PreviousSibling;
        for (int index = 0; sibling != null && index < 4; sibling = sibling.PreviousSibling, index++)
        {
            string text = NormalizeText(sibling.InnerText);
            if (text.Length <= 240) parts.Add(text);
        }
        return NormalizeText(string.Join(" ", parts));
    }

    private static string ReadOwnText(HtmlNode node) => NormalizeText(string.Join(" ",
        node?.ChildNodes
            .Where(child => child.NodeType == HtmlNodeType.Text || string.Equals(child.Name, "span", StringComparison.OrdinalIgnoreCase))
            .Select(child => child.InnerText) ?? []));

    private static void ParseStandardTable(
        HtmlNode table,
        List<HsCodeRemoteSearchRecord> records,
        List<HsCodeRemoteReplacementEvidence> replacements,
        DateTimeOffset observedAt,
        string source)
    {
        if (table == null) return;
        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2) return;
        var headerRow = FindHeaderRow(table, headers => FindCodeHeader(headers) >= 0 && FindNameHeader(headers) >= 0);
        if (headerRow == null) return;
        var headers = ReadHeaders(headerRow);
        int codeIndex = FindCodeHeader(headers);
        int nameIndex = FindNameHeader(headers);
        int specificationIndex = FindSpecificationHeader(headers);
        int summaryIndex = FindHeader(headers, "实例汇总", "实例数量", "案例数量", "申报实例", "实例", "案例");

        foreach (var row in rows.SkipWhile(row => !ReferenceEquals(row, headerRow)).Skip(1))
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count <= Math.Max(codeIndex, nameIndex)) continue;
            string rawCode = NormalizeText(cells[codeIndex].InnerText);
            string code = HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(rawCode).Value);
            string rawName = NormalizeText(cells[nameIndex].InnerText);
            string name = NormalizeText(cells[nameIndex].SelectSingleNode(
                ".//*[contains(concat(' ', normalize-space(@class), ' '), ' showdesc ')]")?.InnerText);
            if (string.IsNullOrWhiteSpace(name)) name = rawName;
            if (!IsPlausibleHsCode(code) || string.IsNullOrWhiteSpace(name) || rawCode.Contains("HS编码", StringComparison.OrdinalIgnoreCase)) continue;
            bool expired = HsCodeTextHelper.IsExpiredText(rawCode) || HsCodeTextHelper.IsExpiredText(name);
            string description = specificationIndex >= 0 && cells.Count > specificationIndex
                ? NormalizeText(cells[specificationIndex].InnerText)
                : EnglishDescriptionRegex.Match(rawName).Groups[1].Value;
            string detailUrl = FindDetailUrl(cells, summaryOnly: false, out _);
            int? count;
            string summaryUrl = summaryIndex >= 0 && cells.Count > summaryIndex
                ? FindSummaryUrl(cells[summaryIndex], out count)
                : FindSummaryUrl(cells.ElementAtOrDefault(2), out count);
            if (summaryIndex < 0 && string.IsNullOrWhiteSpace(summaryUrl)) count = null;
            var recommendations = ExtractRecommendedKeywords(string.Join(" ",
                cells[codeIndex].InnerHtml,
                cells[nameIndex].InnerHtml));
            var item = new HsCode
            {
                Code = code,
                Name = name,
                Description = description,
                DetailUrl = detailUrl,
                Status = expired ? "Obsolete" : "Active",
                SourceName = BuildSourceName(source, HsCodeRemoteRecordKind.StandardCode),
                LastVerifiedAt = observedAt.LocalDateTime,
                UpdateTime = observedAt.LocalDateTime
            };
            records.Add(new HsCodeRemoteSearchRecord(item, HsCodeRemoteRecordKind.StandardCode, expired, count, summaryUrl, detailUrl, observedAt));
            if (expired && recommendations.Count > 0)
            {
                replacements.Add(new HsCodeRemoteReplacementEvidence(code, recommendations, detailUrl, observedAt));
            }
        }
    }

    private static List<HsCodeRemoteSearchRecord> ParseDeclarationTable(
        HtmlNode table,
        DateTimeOffset observedAt,
        string evidenceUrl = "",
        string fallbackCode = "",
        string source = "i5a6")
    {
        var records = new List<HsCodeRemoteSearchRecord>();
        if (table == null) return records;
        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2) return records;
        var headerRow = FindHeaderRow(table, headers => FindCodeHeader(headers) >= 0 && FindNameHeader(headers) >= 0);
        if (headerRow == null) return records;
        var headers = ReadHeaders(headerRow);
        int codeIndex = FindCodeHeader(headers);
        int nameIndex = FindNameHeader(headers);
        int specificationIndex = FindSpecificationHeader(headers);
        if (codeIndex < 0) codeIndex = 0;
        if (nameIndex < 0) nameIndex = 1;
        if (specificationIndex < 0) specificationIndex = 2;

        foreach (var row in rows.SkipWhile(row => !ReferenceEquals(row, headerRow)).Skip(1))
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count <= Math.Max(codeIndex, nameIndex)) continue;
            string rawCode = NormalizeText(cells[codeIndex].InnerText);
            string code = HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(rawCode).Value);
            if (string.IsNullOrWhiteSpace(code)) code = HsCodeTextHelper.NormalizeCode(fallbackCode);
            string name = NormalizeText(cells[nameIndex].InnerText);
            if (!IsPlausibleHsCode(code) || string.IsNullOrWhiteSpace(name) || IsHeaderText(name)) continue;
            string specification = cells.Count > specificationIndex ? NormalizeText(cells[specificationIndex].InnerText) : string.Empty;
            string detailUrl = FindDetailUrl(cells, summaryOnly: false, out _);
            string url = string.IsNullOrWhiteSpace(detailUrl) ? evidenceUrl : detailUrl;
            records.Add(new HsCodeRemoteSearchRecord(
                new HsCode
                {
                    Code = code,
                    Name = name,
                    Description = specification,
                    DetailUrl = url,
                    Status = "Active",
                    SourceName = BuildSourceName(source, HsCodeRemoteRecordKind.DeclarationExample),
                    LastVerifiedAt = observedAt.LocalDateTime,
                    UpdateTime = observedAt.LocalDateTime
                },
                HsCodeRemoteRecordKind.DeclarationExample,
                false,
                null,
                string.Empty,
                url,
                observedAt));
        }
        return records;
    }

    private static void ParseMobileCards(
        HtmlDocument document,
        List<HsCodeRemoteSearchRecord> records,
        DateTimeOffset observedAt,
        string source)
    {
        foreach (var link in document.DocumentNode.SelectNodes("//a[contains(@href,'/hscode/detail/')][.//*[contains(concat(' ',normalize-space(@class),' '),' dealcard ')]]") ?? Enumerable.Empty<HtmlNode>())
        {
            var card = link.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' dealcard ')]");
            var blocks = card?.SelectNodes(".//*[contains(concat(' ',normalize-space(@class),' '),' title ') and contains(concat(' ',normalize-space(@class),' '),' text-block ')]");
            string code = HsCodeTextHelper.NormalizeCode(card?.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' dealcard-brand ')]")?.InnerText);
            string name = NormalizeText(blocks?.ElementAtOrDefault(0)?.InnerText);
            string specification = NormalizeText(blocks?.ElementAtOrDefault(1)?.InnerText);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) continue;
            records.Add(new HsCodeRemoteSearchRecord(
                new HsCode { Code = code, Name = name, Description = specification, DetailUrl = NormalizeUrl(link.GetAttributeValue("href", string.Empty)), Status = "Active", SourceName = BuildSourceName(source, HsCodeRemoteRecordKind.DeclarationExample), LastVerifiedAt = observedAt.LocalDateTime, UpdateTime = observedAt.LocalDateTime },
                HsCodeRemoteRecordKind.DeclarationExample,
                false,
                null,
                string.Empty,
                NormalizeUrl(link.GetAttributeValue("href", string.Empty)),
                observedAt));
        }

        if (records.Count == 0) ParseGenericDetailCards(document, records, observedAt, source);
    }

    private static void ParseGenericDetailCards(
        HtmlDocument document,
        List<HsCodeRemoteSearchRecord> records,
        DateTimeOffset observedAt,
        string source)
    {
        foreach (var link in document.DocumentNode.SelectNodes("//a[contains(@href, '/hscode/detail/')]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (link.Ancestors("table").Any()) continue;
            string url = NormalizeUrl(link.GetAttributeValue("href", string.Empty));
            if (string.IsNullOrWhiteSpace(url)) continue;
            var leaves = link.DescendantsAndSelf()
                .Where(node => !node.ChildNodes.Any(child => child.NodeType == HtmlNodeType.Element))
                .Select(node => NormalizeText(node.InnerText))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string code = leaves.Select(value => HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(value).Value))
                .FirstOrDefault(IsPlausibleHsCode) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                code = HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(NormalizeText(link.InnerText)).Value);
            }
            if (!IsPlausibleHsCode(code)) continue;
            var textValues = leaves.Where(value => !IsPlausibleHsCode(HsCodeTextHelper.NormalizeCode(LeadingCodeRegex.Match(value).Value)))
                .Where(value => !value.Contains("查看详情", StringComparison.OrdinalIgnoreCase))
                .ToList();
            string name = textValues.ElementAtOrDefault(0) ?? string.Empty;
            string specification = textValues.ElementAtOrDefault(1) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 300) continue;
            records.Add(new HsCodeRemoteSearchRecord(
                new HsCode
                {
                    Code = code,
                    Name = name,
                    Description = specification,
                    DetailUrl = url,
                    Status = "Active",
                    SourceName = BuildSourceName(source, HsCodeRemoteRecordKind.DeclarationExample),
                    LastVerifiedAt = observedAt.LocalDateTime,
                    UpdateTime = observedAt.LocalDateTime
                },
                HsCodeRemoteRecordKind.DeclarationExample,
                false,
                null,
                string.Empty,
                url,
                observedAt));
        }
    }

    private static IReadOnlyList<HsCodeRemoteSearchRecord> DeduplicateRecords(IEnumerable<HsCodeRemoteSearchRecord> records) =>
        (records ?? Enumerable.Empty<HsCodeRemoteSearchRecord>())
            .Where(record => record?.Item != null && IsPlausibleHsCode(HsCodeTextHelper.NormalizeCode(record.Item.Code)) &&
                !string.IsNullOrWhiteSpace(record.Item.Name) && !IsHeaderText(record.Item.Name))
            .GroupBy(record => string.Join("|", record.Kind, HsCodeTextHelper.NormalizeCode(record.Item.Code), record.Item.Name?.Trim(), record.Item.Description?.Trim()), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private static Dictionary<string, string> ReadFieldTable(HtmlNode root)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("th|td");
            if (cells == null || cells.Count < 2) continue;
            for (int index = 0; index + 1 < cells.Count; index += 2)
            {
                string label = NormalizeText(cells[index].InnerText).Trim(':', '：');
                string value = NormalizeText(cells[index + 1].InnerText);
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value) && !fields.ContainsKey(label))
                    fields[label] = value;
            }
        }
        return fields;
    }

    private static IReadOnlyList<HsCodeRemoteReferenceEntry> ParseReferenceEntries(HtmlNode table)
    {
        var result = new List<HsCodeRemoteReferenceEntry>();
        foreach (var row in table?.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("td|th");
            if (cells == null || cells.Count < 2) continue;
            string code = NormalizeText(cells[0].InnerText);
            string name = NormalizeText(cells[1].InnerText);
            bool header = (HasClass(cells[0], "tdtoth") && HasClass(cells[1], "tdtoth")) ||
                (code.Contains("编码", StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("名称", StringComparison.OrdinalIgnoreCase) || name.Contains("信息", StringComparison.OrdinalIgnoreCase)));
            if (header) continue;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name)) result.Add(new HsCodeRemoteReferenceEntry(code, name));
        }
        return result;
    }

    private static string ReadHeadingValue(HtmlDocument document, string label)
    {
        var heading = document.DocumentNode.SelectNodes("//div|//h1|//h2|//h3|//h4|//header|//section")?
            .FirstOrDefault(node =>
            {
                string text = ReadOwnText(node);
                return text.Contains(label, StringComparison.OrdinalIgnoreCase) && text.Length <= 160;
            });
        return heading == null ? string.Empty : ReadOwnText(heading).Replace(label, string.Empty, StringComparison.OrdinalIgnoreCase).Trim(' ', '「', '」', '[', ']');
    }

    private static string FindDetailUrl(IEnumerable<HtmlNode> cells, bool summaryOnly, out int? count)
    {
        count = null;
        foreach (var link in (cells ?? Enumerable.Empty<HtmlNode>()).SelectMany(cell => cell.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>()))
        {
            string href = NormalizeUrl(link.GetAttributeValue("href", string.Empty));
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.Contains("#sbsl", StringComparison.OrdinalIgnoreCase))
            {
                count = ParseCount(link.InnerText);
                if (summaryOnly) return href;
                continue;
            }
            if (href.Contains("/hscode/detail/", StringComparison.OrdinalIgnoreCase)) return href;
        }
        return string.Empty;
    }

    private static string FindSummaryUrl(HtmlNode cell, out int? count)
    {
        count = null;
        var link = cell?.SelectSingleNode(".//a[contains(@href, '/hscode/detail/')]");
        if (link == null) return string.Empty;
        count = ParseCount(link.InnerText);
        string href = NormalizeUrl(link.GetAttributeValue("href", string.Empty));
        return count.HasValue || href.Contains("#sbsl", StringComparison.OrdinalIgnoreCase) ? href : string.Empty;
    }

    private static int? ParseCount(string value)
    {
        string raw = CountRegex.Match(value ?? string.Empty).Groups[1].Value.Replace(",", string.Empty);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : null;
    }

    private static IReadOnlyList<string> ReadHeaders(HtmlNode row) =>
        row?.SelectNodes("th|td")?.Select(cell => NormalizeText(cell.InnerText)).ToList() ?? [];

    private static int FindHeader(IReadOnlyList<string> headers, params string[] names)
    {
        for (int index = 0; index < headers.Count; index++)
        {
            if (names.Any(name => headers[index].Contains(name, StringComparison.OrdinalIgnoreCase))) return index;
        }
        return -1;
    }

    private static bool IsPlausibleHsCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length is >= 8 and <= 13 && code.All(char.IsDigit);

    private static bool IsHeaderText(string value)
    {
        string normalized = NormalizeText(value);
        return normalized.Contains("HS编码", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("品名", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("商品名称", StringComparison.OrdinalIgnoreCase);
    }

    private static HsCode Clone(HsCode source) => new()
    {
        Code = source.Code,
        Name = source.Name,
        Unit = source.Unit,
        Description = source.Description,
        Elements = source.Elements,
        SupervisionConditions = source.SupervisionConditions,
        InspectionCategory = source.InspectionCategory,
        RebateRate = source.RebateRate,
        NormalTariffRate = source.NormalTariffRate,
        PreferentialTariffRate = source.PreferentialTariffRate,
        ExportTariffRate = source.ExportTariffRate,
        ConsumptionTaxRate = source.ConsumptionTaxRate,
        ValueAddedTaxRate = source.ValueAddedTaxRate,
        Notes = source.Notes,
        DetailUrl = source.DetailUrl,
        Status = source.Status,
        SourceName = source.SourceName,
        EffectiveYear = source.EffectiveYear,
        LastVerifiedAt = source.LastVerifiedAt,
        UpdateTime = source.UpdateTime
    };

    private static string Prefer(string primary, string fallback) => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();
    private static string BuildSourceName(string source, HsCodeRemoteRecordKind kind)
    {
        string normalized = (source ?? string.Empty).Trim();
        if (normalized.Contains("浏览器降级", StringComparison.OrdinalIgnoreCase)) return normalized;
        return kind == HsCodeRemoteRecordKind.DeclarationExample
            ? "i5a6（第三方申报实例）"
            : "i5a6（第三方参考）";
    }

    private static bool HasClass(HtmlNode node, string className) =>
        (node?.GetAttributeValue("class", string.Empty) ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeText(string value) => WhitespaceRegex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), " ").Trim();
    private static string NormalizeUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return string.Empty;
        if (href.StartsWith("//", StringComparison.Ordinal)) return "https:" + href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return BaseUrl + (href.StartsWith('/') ? href : "/" + href);
    }
}
