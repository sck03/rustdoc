using System.Net;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests
{
    public class HsCodeRemoteSearchParserTests
    {
        [Fact]
        public async Task SearchI5a6DirectAsync_ShouldReadCurrentExampleTableWhenStandardResultIsEmpty()
        {
            const string html = """
                <html><body>
                <div id="resultfind">您查询的相关hs编码 0 条</div>
                <table>
                  <tr><td>HS编码</td><td>品名</td><td>实例汇总</td></tr>
                </table>
                <div id="hssbsl">申报实例查询结果</div>
                <div id="hscasefind"><table>
                  <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
                  <tr><td><a href="//www.i5a6.com/hscode/detail/6109100010"><b>61091000.10</b></a></td><td>棉制男T恤</td><td>针织|男式|100%棉</td></tr>
                  <tr><td><a href="//www.i5a6.com/hscode/detail/6109100021"><b>61091000.21</b></a></td><td>全棉针织男T恤</td><td>针织|T恤衫|男式</td></tr>
                </table></div>
                </body></html>
                """;

            using var httpClient = new HttpClient(new StaticHtmlHandler(html));
            using var service = new HsCodeService(null, null, httpClient);

            var results = await service.SearchI5a6DirectAsync("男T恤");

            Assert.Equal(2, results.Count);
            Assert.Equal("6109100010", results[0].Code);
            Assert.Equal("棉制男T恤", results[0].Name);
            Assert.Equal("针织|男式|100%棉", results[0].Description);
            Assert.Equal("https://www.i5a6.com/hscode/detail/6109100010", results[0].DetailUrl);
        }

        [Fact]
        public async Task SearchI5a6DirectAsync_ShouldReadCurrentMobileDealCards()
        {
            const string html = """
                <html><body>
                <div id="hssbsl">申报实例查询结果</div>
                <dl><dd><a class="react" href="//www.i5a6.com/hscode/detail/6109100010">
                  <div class="dealcard react">
                    <div class="dealcard-brand single-line"><b>61091000.10</b></div>
                    <div class="title text-block">棉制男T恤</div>
                    <div class="title text-block">针织|男式|100%棉</div>
                  </div>
                </a></dd></dl>
                </body></html>
                """;

            using var httpClient = new HttpClient(new StaticHtmlHandler(html));
            using var service = new HsCodeService(null, null, httpClient);

            var item = Assert.Single(await service.SearchI5a6DirectAsync("男T恤"));

            Assert.Equal("6109100010", item.Code);
            Assert.Equal("棉制男T恤", item.Name);
            Assert.Equal("针织|男式|100%棉", item.Description);
            Assert.Equal("https://www.i5a6.com/hscode/detail/6109100010", item.DetailUrl);
        }

        [Fact]
        public async Task SearchI5a6DirectAsync_ShouldKeepExamplesFromExpiredSearchPageWhenRecommendationsHaveResults()
        {
            const string expiredSearchHtml = """
                <html><body>
                <div id="resultfind">您查询的相关hs编码 3 条</div>
                <table id="expired-results">
                  <tr><td>HS编码</td><td>品名</td></tr>
                  <tr><td>61091000.22（已作废）<br/><a href="/hscode/key/61091000">推荐查询: 61091000</a></td><td>其他针织女式T恤衫</td></tr>
                </table>
                <div id="hssbsl">申报实例查询结果</div>
                <div id="hscasefind"><table>
                  <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
                  <tr><td><a href="/hscode/detail/6109100010">61091000.10</a></td><td>女式T恤衫</td><td>针织|女式|100%棉</td></tr>
                </table></div>
                </body></html>
                """;
            const string recommendedHtml = """
                <html><body><table>
                  <tr><td>HS编码</td><td>品名</td></tr>
                  <tr><td><a href="/hscode/detail/6109100000">61091000.00</a></td><td>针织棉制T恤衫</td></tr>
                </table></body></html>
                """;

            using var httpClient = new HttpClient(new RoutingStaticHtmlHandler(expiredSearchHtml, recommendedHtml));
            using var service = new HsCodeService(null, null, httpClient);

            var results = await service.SearchI5a6DirectAsync("女式T恤衫");

            Assert.Contains(results, item => item.Code == "6109100010" && item.Name == "女式T恤衫" && item.Description == "针织|女式|100%棉");
            Assert.Contains(results, item => item.Code == "6109100000" && item.Name == "针织棉制T恤衫");
        }

        [Fact]
        public async Task SearchRemoteAsync_ShouldPreserveDistinctDeclarationExamplesForSameCode()
        {
            var provider = new StubRemoteProvider(
            [
                new HsCode { Code = "6109100010", Name = "男T恤", Description = "针织|男式|100%棉" },
                new HsCode { Code = "6109100010", Name = "棉制针织男T恤衫", Description = "针织|男式|62%棉38%涤" },
                new HsCode { Code = "6109100010", Name = "男T恤", Description = "针织|男式|100%棉" }
            ]);
            using var service = new HsCodeService(null, null, [provider]);

            var results = await service.SearchRemoteAsync("男T恤");

            Assert.Equal(2, results.Count);
            Assert.All(results, item => Assert.Equal("6109100010", item.Code));
            Assert.Contains(results, item => item.Name == "男T恤" && item.Description == "针织|男式|100%棉");
            Assert.Contains(results, item => item.Name == "棉制针织男T恤衫" && item.Description == "针织|男式|62%棉38%涤");
        }

        [Fact]
        public async Task SearchRemoteEvidenceAsync_ShouldResolveCurrentCodesWithoutChangingOriginalCandidateExamples()
        {
            string[] oldCodes = ["6109100010", "6109100021", "6109909052"];
            var examples = oldCodes
                .SelectMany((code, codeIndex) => Enumerable.Range(1, 5).Select(exampleIndex =>
                    CreateRecord(
                        code,
                        $"男T恤-{codeIndex + 1}-{exampleIndex}",
                        $"针织|男式|规格{exampleIndex}",
                        HsCodeRemoteRecordKind.DeclarationExample)))
                .ToList();
            var provider = new EvidenceTrackingProvider(
                new HsCodeRemoteSearchBundle("男T恤", "stub", examples, []),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["6109100010"] = "61091000",
                    ["6109100021"] = "61091000",
                    ["6109909052"] = "61099090"
                },
                new Dictionary<string, HsCodeRemoteSearchBundle>(StringComparer.OrdinalIgnoreCase)
                {
                    ["61091000"] = new HsCodeRemoteSearchBundle(
                        "61091000",
                        "stub",
                        [CreateRecord("6109100000", "棉制针织或钩编的T恤衫", string.Empty, HsCodeRemoteRecordKind.StandardCode)],
                        []),
                    ["61099090"] = new HsCodeRemoteSearchBundle(
                        "61099090",
                        "stub",
                        [CreateRecord("6109909000", "其他纺材制针织或钩编的T恤衫", string.Empty, HsCodeRemoteRecordKind.StandardCode)],
                        [])
                });
            using var service = new HsCodeService(null, null, [provider]);

            var bundle = await service.SearchRemoteEvidenceAsync("男T恤");

            var retainedExamples = bundle.Records
                .Where(item => item.Kind == HsCodeRemoteRecordKind.DeclarationExample)
                .ToList();
            Assert.Equal(15, retainedExamples.Count);
            Assert.Equal(15, retainedExamples
                .Select(item => $"{item.Item.Code}|{item.Item.Name}|{item.Item.Description}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
            Assert.Equal(
                ["6109100000", "6109909000"],
                bundle.Records
                    .Where(item => item.Kind == HsCodeRemoteRecordKind.StandardCode && !item.IsExpired)
                    .Select(item => item.Item.Code)
                    .OrderBy(code => code)
                    .ToArray());
            Assert.All(oldCodes, code => Assert.Equal(1, provider.DetailFetchCounts.GetValueOrDefault(code)));
            Assert.Contains(bundle.ReplacementEvidence, item =>
                item.OldCode == "6109100010" && item.RecommendedKeywords.Contains("61091000"));
            Assert.Contains(bundle.ReplacementEvidence, item =>
                item.OldCode == "6109100021" && item.RecommendedKeywords.Contains("61091000"));
            Assert.Contains(bundle.ReplacementEvidence, item =>
                item.OldCode == "6109909052" && item.RecommendedKeywords.Contains("61099090"));
        }

        [Fact]
        public void ParseSearchPage_ShouldReadSleepwearStandardRowsAndInstanceSummary()
        {
            const string html = """
                <div id="resultfind">您查询的相关hs编码 15 条</div>
                <table>
                  <tr><td>HS编码</td><td>品名</td><td>实例汇总</td><td>申报要素·退税</td><td>编码对比</td></tr>
                  <tr>
                    <td><b>61083200.00</b></td>
                    <td><span class="showdesc">化纤制针织或钩编女睡衣及睡衣裤</span><br/><span>[Knitted women's pyjamas of man-made fibres]</span></td>
                    <td><a href="//www.i5a6.com/hscode/detail/6108320000#sbsl">534条</a></td>
                    <td><a href="//www.i5a6.com/hscode/detail/6108320000">查看详情</a></td>
                    <td>--</td>
                  </tr>
                </table>
                """;

            var bundle = I5a6PageParser.ParseSearchPage(html, "睡衣");

            var record = Assert.Single(bundle.Records);
            Assert.Equal(HsCodeRemoteRecordKind.StandardCode, record.Kind);
            Assert.Equal("6108320000", record.Item.Code);
            Assert.Equal("化纤制针织或钩编女睡衣及睡衣裤", record.Item.Name);
            Assert.Equal("Knitted women's pyjamas of man-made fibres", record.Item.Description);
            Assert.Equal(534, record.InstanceCount);
            Assert.Equal("https://www.i5a6.com/hscode/detail/6108320000#sbsl", record.SummaryUrl);
            Assert.Equal("https://www.i5a6.com/hscode/detail/6108320000", record.EvidenceUrl);
        }

        [Fact]
        public void ParseDetailPage_ShouldReadAllFieldsReferencesAndTwentyDeclarationExamples()
        {
            string exampleRows = string.Concat(Enumerable.Range(1, 20).Select(index =>
                $"<tr><td>61083200.00</td><td>女式睡衣{index}</td><td>针织|女式|100%涤纶</td></tr>"));
            string html = $$"""
                <div id="hscode-detail"><table>
                  <tr><td>商品编码</td><td>61083200.00</td></tr>
                  <tr><td>商品名称</td><td>化纤制针织或钩编女睡衣及睡衣裤</td></tr>
                  <tr><td>申报要素</td><td>1:织造方法;2:类别;3:成分含量</td></tr>
                  <tr><td>法定第一单位</td><td>件</td><td>法定第二单位</td><td>千克</td></tr>
                  <tr><td>最惠国进口税率</td><td>6%</td><td>普通进口税率</td><td>130%</td></tr>
                  <tr><td>消费税率</td><td>0%</td><td>增值税率</td><td>13%</td></tr>
                  <tr><td>出口关税率</td><td>0%</td><td>出口退税率</td><td>13%</td></tr>
                  <tr><td>海关监管条件</td><td>A</td><td>检验检疫类别</td><td>M/</td></tr>
                  <tr><td>英文名称</td><td>Knitted women's pyjamas</td></tr>
                </table></div>
                <div class="detail-hd"><span>个人行邮税号 「04019900」</span></div>
                <div class="detail-hd">10位HS编码+3位CIQ代码(中国海关申报13位海关编码)</div>
                <table><tr><td class="tdtoth">10位HS编码+3位CIQ代码</td><td class="tdtoth">商品信息</td></tr>
                  <tr><td>6108320000.101</td><td>儿童服装</td></tr></table>
                <div class="detail-hd">所属分类及章节、品目</div>
                <table><tr><td class="tdtoth">类目</td><td>第十一类 纺织原料及纺织制品</td></tr>
                  <tr><td class="tdtoth">章节</td><td>第六十一章 针织或钩编的服装</td></tr></table>
                <div class="detail-hd" id="sbsl">申报实例汇总</div>
                <table><tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>{{exampleRows}}</table>
                """;
            var seed = new HsCode
            {
                Code = "6108320000",
                DetailUrl = "https://www.i5a6.com/hscode/detail/6108320000"
            };

            var bundle = I5a6PageParser.ParseDetailPage(
                html, seed, 534,
                "https://www.i5a6.com/hscode/detail/6108320000#sbsl");

            Assert.False(bundle.IsExpired);
            Assert.Equal("13%", bundle.Item.RebateRate);
            Assert.Equal("130%", bundle.Item.NormalTariffRate);
            Assert.Equal("6%", bundle.Item.PreferentialTariffRate);
            Assert.Equal("0%", bundle.Item.ExportTariffRate);
            Assert.Equal("M/", bundle.Item.InspectionCategory);
            Assert.Equal("04019900", bundle.PersonalPostalTaxCode);
            Assert.Single(bundle.CiqEntries);
            Assert.Equal(2, bundle.ClassificationEntries.Count);
            Assert.Equal(20, bundle.DeclarationExamples.Count);
            Assert.All(bundle.DeclarationExamples, item => Assert.Equal(HsCodeRemoteRecordKind.DeclarationExample, item.Kind));
        }

        [Fact]
        public void ParseSearchPage_ShouldKeepObsoleteFemaleCodesRecommendationsAndDirectExamples()
        {
            const string html = """
                <div id="resultfind">您查询的相关hs编码 2 条</div>
                <table><tr><td>HS编码</td><td>品名</td><td>实例汇总</td><td>申报要素·退税</td></tr>
                  <tr><td>61091000.22 <span>(已作废)</span><a href="/hscode/key/61091000">推荐查询: 61091000</a></td>
                    <td><span class="showdesc">棉制针织女式T恤衫</span></td><td><a href="/hscode/detail/6109100022#sbsl">814条</a></td><td><a href="/hscode/detail/6109100022">查看详情</a></td></tr>
                  <tr><td>61099090.52 <span>(已作废)</span><a href="/hscode/key/61099090">推荐查询: 61099090</a></td>
                    <td><span class="showdesc">其他纺材制女式T恤衫</span></td><td><a href="/hscode/detail/6109909052#sbsl">432条</a></td><td><a href="/hscode/detail/6109909052">查看详情</a></td></tr>
                </table>
                <div id="hssbsl">申报实例查询结果</div><div id="hscasefind"><table>
                  <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
                  <tr><td>61091000.22</td><td>全棉女式T恤衫</td><td>针织|女式|100%棉</td></tr>
                </table></div>
                """;

            var bundle = I5a6PageParser.ParseSearchPage(html, "女式T恤衫");

            Assert.Equal(2, bundle.Records.Count(item => item.Kind == HsCodeRemoteRecordKind.StandardCode && item.IsExpired));
            Assert.Single(bundle.Records, item => item.Kind == HsCodeRemoteRecordKind.DeclarationExample);
            Assert.Contains(bundle.ReplacementEvidence, item => item.OldCode == "6109100022" && item.RecommendedKeywords.Contains("61091000"));
            Assert.Contains(bundle.ReplacementEvidence, item => item.OldCode == "6109909052" && item.RecommendedKeywords.Contains("61099090"));
        }

        [Fact]
        public void ParseDetailPage_ShouldReadObsoleteRecommendationAndKeepHistoricalExamples()
        {
            const string html = """
                <div id="hscode-detail"><table>
                  <tr><td>商品编码</td><td>61091000.10 <span>(已作废)</span> <a href="/hscode/key/61091000">推荐查询: 61091000</a></td></tr>
                  <tr><td>商品名称</td><td>棉制针织女式T恤衫</td></tr>
                </table></div>
                <div class="detail-hd" id="sbsl">申报实例汇总</div><table>
                  <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
                  <tr><td>61091000.10</td><td>全棉女式T恤衫</td><td>针织|女式|100%棉</td></tr>
                </table>
                """;

            var bundle = I5a6PageParser.ParseDetailPage(html, new HsCode
            {
                Code = "6109100010",
                DetailUrl = "https://www.i5a6.com/hscode/detail/6109100010"
            });

            Assert.True(bundle.IsExpired);
            Assert.Contains("61091000", bundle.RecommendedKeywords);
            Assert.Single(bundle.DeclarationExamples);
            Assert.Equal("6109100010", bundle.DeclarationExamples[0].Item.Code);
        }

        [Fact]
        public void ParseSearchPage_ShouldSurviveWrapperHeaderRowAndColumnOrderChanges()
        {
            const string html = """
                <section class="search-output-v2">
                  <h3>税则查询结果</h3>
                  <table class="result-grid-v2">
                    <tr><td colspan="5">查询结果仅供参考</td></tr>
                    <tr><th>货品名称</th><th>案例数量</th><th>税则号列</th><th>税率详情</th><th>备注</th></tr>
                    <tr><td><span class="showdesc">化纤制针织女睡衣</span></td><td><a href="/hscode/detail/6108320000#sbsl">534条</a></td><td>61083200.00</td><td><a href="/hscode/detail/6108320000">详情</a></td><td>-</td></tr>
                  </table>
                  <h3>申报案例</h3>
                  <div><table class="case-grid-v2">
                    <tr><th>规格型号</th><th>海关编码</th><th>货物名称</th></tr>
                    <tr><td>针织|女式|100%涤纶</td><td>61083200.00</td><td>针织女式睡衣裤</td></tr>
                  </table></div>
                </section>
                """;

            var bundle = I5a6PageParser.ParseSearchPage(html, "睡衣");

            var standard = Assert.Single(bundle.Records, item => item.Kind == HsCodeRemoteRecordKind.StandardCode);
            Assert.Equal("6108320000", standard.Item.Code);
            Assert.Equal(534, standard.InstanceCount);
            var example = Assert.Single(bundle.Records, item => item.Kind == HsCodeRemoteRecordKind.DeclarationExample);
            Assert.Equal("针织女式睡衣裤", example.Item.Name);
            Assert.Equal("针织|女式|100%涤纶", example.Item.Description);
        }

        [Fact]
        public void ParseDetailPage_ShouldSurviveChangedIdsAndHeadingClasses()
        {
            const string html = """
                <main>
                  <table class="tariff-facts-v2">
                    <tr><td>商品编码</td><td>61083200.00</td></tr>
                    <tr><td>商品名称</td><td>化纤制针织女睡衣</td></tr>
                    <tr><td>申报要素</td><td>织造方法;类别;成分含量</td></tr>
                  </table>
                  <h2>10位HS编码+3位CIQ代码</h2>
                  <table><tr><th>编码</th><th>商品信息</th></tr><tr><td>6108320000.999</td><td>其他女睡衣</td></tr></table>
                  <header>所属分类及章节、品目</header>
                  <table><tr><td>章节</td><td>第六十一章 针织服装</td></tr></table>
                  <h2>申报实例汇总</h2>
                  <table class="examples-v2"><tr><th>规格型号</th><th>货品名称</th><th>海关编码</th></tr>
                    <tr><td>针织|女式|100%涤纶</td><td>女式睡衣裤</td><td>61083200.00</td></tr></table>
                </main>
                """;

            var bundle = I5a6PageParser.ParseDetailPage(html, new HsCode
            {
                Code = "6108320000",
                DetailUrl = "https://www.i5a6.com/hscode/detail/6108320000"
            });

            Assert.Equal("化纤制针织女睡衣", bundle.Item.Name);
            Assert.Single(bundle.CiqEntries);
            Assert.Single(bundle.ClassificationEntries);
            Assert.Single(bundle.DeclarationExamples);
            Assert.Equal("女式睡衣裤", bundle.DeclarationExamples[0].Item.Name);
        }

        [Fact]
        public void ParseSearchPage_ShouldUseGenericMobileCardFallbackWhenCssClassesChange()
        {
            const string html = """
                <div class="mobile-results-v3">
                  <a href="/hscode/detail/6109100000">
                    <article><strong>61091000.00</strong><h3>棉制针织男式T恤衫</h3><p>针织|男式|100%棉</p></article>
                  </a>
                </div>
                """;

            var item = Assert.Single(I5a6PageParser.ParseSearchPage(html, "男T恤").Records);

            Assert.Equal(HsCodeRemoteRecordKind.DeclarationExample, item.Kind);
            Assert.Equal("6109100000", item.Item.Code);
            Assert.Equal("棉制针织男式T恤衫", item.Item.Name);
            Assert.Equal("针织|男式|100%棉", item.Item.Description);
        }

        private sealed class StaticHtmlHandler(string html) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html),
                    RequestMessage = request
                });
            }
        }

        private sealed class RoutingStaticHtmlHandler(string expiredHtml, string recommendedHtml) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var content = request.RequestUri?.AbsolutePath.Contains("/61091000", StringComparison.OrdinalIgnoreCase) == true
                    ? recommendedHtml
                    : expiredHtml;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content),
                    RequestMessage = request
                });
            }
        }

        private sealed class StubRemoteProvider(IReadOnlyList<HsCode> rows) : IHsCodeRemoteProvider
        {
            public string Name => "stub";
            public int Priority => 1;
            public bool CanHandleDetailUrl(string detailUrl) => false;
            public Task<IReadOnlyList<HsCode>> SearchAsync(string keyword, CancellationToken cancellationToken = default) => Task.FromResult(rows);
            public Task<HsCode> FetchDetailAsync(HsCode item, CancellationToken cancellationToken = default) => Task.FromResult(item);
            public Task<HsCodeRemoteSourceHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new HsCodeRemoteSourceHealth(Name, true, DateTimeOffset.UtcNow, "ok"));
        }

        private static HsCodeRemoteSearchRecord CreateRecord(
            string code,
            string name,
            string specification,
            HsCodeRemoteRecordKind kind)
        {
            string detailUrl = $"https://www.i5a6.com/hscode/detail/{code}";
            return new HsCodeRemoteSearchRecord(
                new HsCode
                {
                    Code = code,
                    Name = name,
                    Description = specification,
                    DetailUrl = detailUrl,
                    Status = kind == HsCodeRemoteRecordKind.StandardCode ? "Active" : "ReferenceOnly"
                },
                kind,
                false,
                null,
                string.Empty,
                detailUrl,
                DateTimeOffset.UtcNow);
        }

        private sealed class EvidenceTrackingProvider(
            HsCodeRemoteSearchBundle initial,
            IReadOnlyDictionary<string, string> recommendations,
            IReadOnlyDictionary<string, HsCodeRemoteSearchBundle> recommendedResults) : IHsCodeRemoteProvider
        {
            public Dictionary<string, int> DetailFetchCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string Name => "stub";
            public int Priority => 1;
            public bool CanHandleDetailUrl(string detailUrl) => !string.IsNullOrWhiteSpace(detailUrl);
            public Task<IReadOnlyList<HsCode>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<HsCode>>([]);
            public Task<HsCode> FetchDetailAsync(HsCode item, CancellationToken cancellationToken = default) =>
                Task.FromResult(item);
            public Task<HsCodeRemoteSourceHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new HsCodeRemoteSourceHealth(Name, true, DateTimeOffset.UtcNow, "ok"));

            public Task<HsCodeRemoteSearchBundle> SearchEvidenceAsync(
                string keyword,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(recommendedResults.TryGetValue(keyword, out var result) ? result : initial);
            }

            public Task<HsCodeRemoteDetailBundle> FetchDetailEvidenceAsync(
                HsCodeRemoteSearchRecord record,
                CancellationToken cancellationToken = default)
            {
                string code = HsCodeTextHelper.NormalizeCode(record.Item.Code);
                DetailFetchCounts[code] = DetailFetchCounts.GetValueOrDefault(code) + 1;
                string recommendation = recommendations[code];
                return Task.FromResult(new HsCodeRemoteDetailBundle(
                    record.Item,
                    true,
                    record.InstanceCount,
                    [recommendation],
                    [],
                    string.Empty,
                    [],
                    [],
                    record.EvidenceUrl,
                    DateTimeOffset.UtcNow));
            }
        }
    }
}
