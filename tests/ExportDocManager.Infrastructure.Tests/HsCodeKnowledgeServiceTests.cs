using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class HsCodeKnowledgeServiceTests
{
    [Fact]
    public async Task Search_ShouldNormalizeFullWidthAndPreferProductName()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.AddRange(
                ActiveCode("6109100090", "棉制针织男式T恤衫"),
                ActiveCode("9999999999", "其它商品"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6109100090", "6109100090", "男士全棉圆领短袖T-SHIRT", "针织", "Manual", 2026, "Active", true));
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "9999999999", "9999999999", "其它商品", "男士全棉圆领短袖T恤衫", "Manual", 2026, "Active", true));

        var result = await service.SearchAsync("ＭＥＮＳ　１００％ＣＯＴＴＯＮ　Ｔ－ＳＨＩＲＴ");

        Assert.Equal("6109100090", result.Items.First().CurrentCode);
    }

    [Fact]
    public async Task Search_ShouldTreatKnittingAndCrochetAsRelatedButKeepOriginalWording()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6110200090", "棉制钩编套头衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6110200090", "6110200090", "棉制钩编男式套头衫", "长袖", "Manual", 2026, "Active", true));

        var result = await service.SearchAsync("棉制针织男式套头衫");

        Assert.Equal("6110200090", result.Items.First().CurrentCode);
        Assert.Contains("钩编", result.Items.First().Name);
    }

    [Fact]
    public async Task HistoryDiscovery_ShouldRequireExplicitConfirmationBeforeLearning()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100090", "棉制T恤衫"));
            context.Products.Add(new Product { ProductCode = "TS01", NameCN = "男士全棉圆领短袖", HSCode = "6109100090", Material = "100%棉", Brand = "自有品牌" });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);

        var firstPage = await service.DiscoverHistoryCandidatesAsync("圆领短袖", 1, 30);
        var candidate = Assert.Single(firstPage.Items);
        Assert.Equal(1, firstPage.TotalCount);
        Assert.Equal(1, firstPage.PageNumber);
        Assert.True(candidate.CanConfirm);
        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));

        await service.SaveExampleAsync(new HsCodeExampleInput(0, candidate.RawCode, candidate.CurrentCode, candidate.ProductName,
            candidate.Specification, "HistoryConfirmed", 2026, "ManuallyVerified", true));
        Assert.Empty((await service.DiscoverHistoryCandidatesAsync("圆领短袖", 1, 30)).Items);
        Assert.Equal(1, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task HistoryDiscovery_ShouldMergeInvoiceVariantsWithoutChangingProductName()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6110300090", "化纤制针织女式非起绒套头衫"));
            context.Invoices.Add(new Invoice
            {
                InvoiceNo = "HISTORY-001",
                Type = "CommercialInvoice",
                Items =
                [
                    new Item { StyleNameCN = "化纤制针织女式非起绒套头衫", FabricComposition = "51%涤44%棉5%氨纶", Brand = "PETROL INDUSTRIES", StyleNo = "YLAW1320", HSCode = "6110300090" },
                    new Item { StyleNameCN = "化纤制针织女式非起绒套头衫-2", FabricComposition = "51%涤44%棉5%氨纶", Brand = "PETROL INDUSTRIES", StyleNo = "YLAW1320-1", HSCode = "6110300090" },
                    new Item { StyleNameCN = "化纤制针织女式非起绒套头衫-3", FabricComposition = "51%涤44%棉5%氨纶", Brand = "PETROL INDUSTRIES", StyleNo = "YLAW1320-2", HSCode = "6110300090" }
                ]
            });
            await context.SaveChangesAsync();
        }

        var candidate = Assert.Single((await new HsCodeKnowledgeService(factory)
            .DiscoverHistoryCandidatesAsync("套头衫", 1, 30)).Items);

        Assert.Equal("化纤制针织女式非起绒套头衫", candidate.ProductName);
        Assert.Equal("51%涤44%棉5%氨纶 · PETROL INDUSTRIES", candidate.Specification);
        Assert.Equal(3, candidate.SourceCount);
        Assert.Equal(3, candidate.VariantCount);
        Assert.Equal(["YLAW1320", "YLAW1320-1", "YLAW1320-2"], candidate.VariantSamples);
        Assert.DoesNotContain("-2", candidate.ProductName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteResults_ShouldStayInCandidatePoolUntilUserConfirms()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100090", "棉制T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);

        await service.CaptureRemoteExamplesAsync("男士全棉短袖", [new HsCode { Code = "6109100090", Name = "男士全棉圆领短袖", Description = "针织，100%棉", DetailUrl = "https://www.i5a6.com/hscode/detail/6109100090" }]);

        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));
        var candidate = Assert.Single((await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).Items);
        Assert.Equal("6109100090", candidate.SuggestedCurrentHsCode);
        Assert.Equal("https://www.i5a6.com/hscode/detail/6109100090", candidate.SourceUrl);
        await service.ReviewRemoteCandidateAsync(new HsCodeRemoteCandidateReviewInput(candidate.Id, "6109100090", true));
        Assert.Empty((await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).Items);
        Assert.Equal(1, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task UntrustedActiveCode_ShouldNotBeConfirmableOrUsable()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(new HsCode
            {
                Code = "6109100090",
                Name = "缺少年度的T恤衫编码",
                Status = "Active",
                SourceName = "测试来源",
                LastVerifiedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(
            0, "6109100090", "", "棉制针织T恤衫", "短袖|圆领", "Legacy", 2024, "Unresolved", false));
        await service.CaptureRemoteExamplesAsync("棉制针织T恤衫",
            [new HsCode { Code = "6109100090", Name = "棉制针织T恤衫", Description = "短袖|圆领" }]);

        var result = Assert.Single((await service.SearchAsync("棉制针织T恤衫")).Items);
        Assert.False(result.CanUse);
        Assert.True(string.IsNullOrWhiteSpace(result.CurrentCode));
        var candidate = Assert.Single((await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).Items);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReviewRemoteCandidateAsync(new HsCodeRemoteCandidateReviewInput(candidate.Id, "6109100090", true)));
    }

    [Fact]
    public async Task RemoteCandidateReview_ShouldSupportPagingBatchHistoryAndReset()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100000", "棉制针织T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.CaptureRemoteExamplesAsync("T恤",
        [
            new HsCode { Code = "6109100010", Name = "男式T恤一", Description = "针织|棉" },
            new HsCode { Code = "6109100020", Name = "男式T恤二", Description = "针织|棉" },
            new HsCode { Code = "6109100030", Name = "女式T恤三", Description = "针织|棉" }
        ]);

        var firstPage = await service.ListRemoteCandidatesAsync("Pending", "T恤", 1, 2);
        var secondPage = await service.ListRemoteCandidatesAsync("Pending", "T恤", 2, 2);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);

        int reviewed = await service.ReviewRemoteCandidatesAsync(
        [
            new HsCodeRemoteCandidateReviewInput(firstPage.Items[0].Id, "6109100000", true),
            new HsCodeRemoteCandidateReviewInput(firstPage.Items[1].Id, "", false)
        ]);
        Assert.Equal(2, reviewed);
        var confirmed = Assert.Single((await service.ListRemoteCandidatesAsync("Confirmed", "", 1, 30)).Items);
        var ignored = Assert.Single((await service.ListRemoteCandidatesAsync("Ignored", "", 1, 30)).Items);
        Assert.NotNull(confirmed.ReviewedAt);
        Assert.NotNull(ignored.ReviewedAt);
        Assert.Equal(1, await service.CountExamplesAsync(string.Empty));

        Assert.Equal(2, await service.ResetRemoteCandidatesAsync([confirmed.Id, ignored.Id]));
        Assert.Equal(3, (await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).TotalCount);
        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task RemoteCandidateCodeFilter_ShouldMatchHsCodePrefixOnly()
    {
        using var factory = new SqliteFactory();
        var service = new HsCodeKnowledgeService(factory);
        await service.CaptureRemoteExamplesAsync("编码前缀",
        [
            new HsCode { Code = "6109100000", Name = "前缀匹配" },
            new HsCode { Code = "2846109010", Name = "中间包含但不应匹配" }
        ]);

        var result = await service.ListRemoteCandidatesAsync("Pending", "6109", 1, 30);

        var item = Assert.Single(result.Items);
        Assert.Equal("6109100000", item.RawReportedHsCode);
    }

    [Fact]
    public async Task Search_ShouldSuggestSingleUnverifiedReplacementWithoutMarkingItUsable()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100090", "棉制针织或钩编的T恤衫"));
            context.HsCodeReplacementRelations.Add(new HsCodeReplacementRelation { OldCode = "6109100021", NewCode = "6109100090", Source = "2026税则", Confidence = 90 });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6109100021", "", "男式T-SHIRT", "针织，100%棉，男式", "i5a6", 2024, "Unresolved", false));

        var result = await service.SearchAsync("棉制针织男式T恤衫");

        var item = Assert.Single(result.Items);
        Assert.Equal("6109100090", item.CurrentCode);
        Assert.Equal("6109100021", item.RawCode);
        Assert.Equal("SuggestedReplacement", item.ResolutionStatus);
        Assert.False(item.CanUse);
    }

    [Fact]
    public async Task Search_ShouldNotChooseAmbiguousReplacement()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.AddRange(
                ActiveCode("6109100011", "棉制T恤衫一"),
                ActiveCode("6109100012", "棉制T恤衫二"));
            context.HsCodeReplacementRelations.AddRange(
                new HsCodeReplacementRelation { OldCode = "6109100010", NewCode = "6109100011", Source = "test", Confidence = 80 },
                new HsCodeReplacementRelation { OldCode = "6109100010", NewCode = "6109100012", Source = "test", Confidence = 80 });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6109100010", "", "棉制男式T恤衫", "针织", "i5a6", 2023, "Unresolved", false));

        var item = Assert.Single((await service.SearchAsync("棉制男式T恤衫")).Items, row => row.RawCode == "6109100010");
        Assert.Equal("Ambiguous", item.ResolutionStatus);
        Assert.False(item.CanUse);
        Assert.Equal(2, item.ReplacementCandidates.Count);
    }

    [Fact]
    public async Task Package_ShouldRoundTripWithoutBusinessDataAndRejectTampering()
    {
        using var sourceFactory = new SqliteFactory();
        await using (var context = sourceFactory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100090", "棉制T恤衫"));
            await context.SaveChangesAsync();
        }
        var source = new HsCodeKnowledgeService(sourceFactory);
        await source.SaveExampleAsync(new HsCodeExampleInput(0, "6109100090", "6109100090", "棉制T恤衫", "针织", "Manual", 2026, "Active", true));
        byte[] package = await source.ExportPackageAsync();
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edmhs");
        await File.WriteAllBytesAsync(path, package);
        try
        {
            using var targetFactory = new SqliteFactory();
            var target = new HsCodeKnowledgeService(targetFactory);
            var preview = await target.PreviewPackageAsync(path);
            Assert.Single(preview.Examples);
            Assert.DoesNotContain("invoice", System.Text.Encoding.UTF8.GetString(package), StringComparison.OrdinalIgnoreCase);
            await target.ImportPackageAsync(preview);
            Assert.Equal(1, await target.CountExamplesAsync("棉制"));

            package[0] ^= 0x5A;
            await File.WriteAllBytesAsync(path, package);
            await Assert.ThrowsAnyAsync<InvalidDataException>(() => target.PreviewPackageAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Package_ShouldRejectActiveCodeWithoutTrustedMetadata()
    {
        using var sourceFactory = new SqliteFactory();
        await using (var context = sourceFactory.CreateDbContext())
        {
            context.HsCodes.Add(new HsCode
            {
                Code = "6109100090",
                Name = "缺少来源的有效编码",
                Status = "Active",
                EffectiveYear = 2026,
                LastVerifiedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }
        byte[] package = await new HsCodeKnowledgeService(sourceFactory).ExportPackageAsync();
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edmhs");
        await File.WriteAllBytesAsync(path, package);
        try
        {
            using var targetFactory = new SqliteFactory();
            var target = new HsCodeKnowledgeService(targetFactory);
            await Assert.ThrowsAsync<InvalidDataException>(() => target.PreviewPackageAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureRemoteEvidence_ShouldOnlyCreateCandidatesFromDeclarationExamples()
    {
        using var factory = new SqliteFactory();
        var service = new HsCodeKnowledgeService(factory);
        var observedAt = DateTimeOffset.UtcNow;
        var bundle = new HsCodeRemoteSearchBundle(
            "睡衣",
            "i5a6",
            [
                new HsCodeRemoteSearchRecord(
                    new HsCode { Code = "6108320000", Name = "化纤制针织女睡衣", Status = "Active" },
                    HsCodeRemoteRecordKind.StandardCode, false, 534,
                    "https://www.i5a6.com/hscode/detail/6108320000#sbsl",
                    "https://www.i5a6.com/hscode/detail/6108320000", observedAt),
                new HsCodeRemoteSearchRecord(
                    new HsCode { Code = "6108320000", Name = "针织女式睡衣裤", Description = "针织|女式|100%涤纶" },
                    HsCodeRemoteRecordKind.DeclarationExample, false, null, string.Empty,
                    "https://www.i5a6.com/hscode/detail/6108320000#sbsl", observedAt)
            ],
            []);

        Assert.Equal(1, await service.CaptureRemoteEvidenceAsync("睡衣", bundle));
        var pending = await service.ListRemoteCandidatesAsync("Pending", "", 1, 30);
        var candidate = Assert.Single(pending.Items);
        Assert.Equal("针织女式睡衣裤", candidate.ProductName);
        Assert.DoesNotContain(pending.Items, item => item.ProductName == "化纤制针织女睡衣");
    }

    [Fact]
    public async Task WebRecommendation_ShouldSuggestOneActiveLocalCodeButStillRequireReview()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100000", "棉制针织T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        var observedAt = DateTimeOffset.UtcNow;
        var bundle = new HsCodeRemoteSearchBundle(
            "女式T恤衫", "i5a6",
            [new HsCodeRemoteSearchRecord(
                new HsCode { Code = "6109100022", Name = "棉制针织女式T恤衫", Description = "针织|女式|棉" },
                HsCodeRemoteRecordKind.DeclarationExample, false, null, string.Empty,
                "https://www.i5a6.com/hscode/detail/6109100022#sbsl", observedAt)],
            [new HsCodeRemoteReplacementEvidence(
                "6109100022", ["61091000"],
                "https://www.i5a6.com/hscode/detail/6109100022", observedAt)]);

        await service.CaptureRemoteEvidenceAsync("女式T恤衫", bundle);

        var candidate = Assert.Single((await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).Items);
        Assert.Equal("6109100000", candidate.SuggestedCurrentHsCode);
        Assert.Equal("WebRecommended", candidate.ResolutionStatus);
        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task ConfirmedRemoteCandidate_ShouldUseUnknownSourceYearAndActiveLocalCode()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100000", "棉制针织T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.CaptureRemoteExamplesAsync("女式T恤衫",
            [new HsCode { Code = "6109100022", Name = "女式T恤衫", Description = "针织|女式|棉" }]);
        var candidate = Assert.Single((await service.ListRemoteCandidatesAsync("Pending", "", 1, 30)).Items);

        await service.ReviewRemoteCandidateAsync(new HsCodeRemoteCandidateReviewInput(candidate.Id, "6109100000", true));

        var example = Assert.Single(await service.ListExamplesAsync(string.Empty, 1, 20));
        Assert.Null(example.SourceYear);
        Assert.Equal("6109100000", example.ResolvedCurrentHsCode);
        Assert.True(example.IsManuallyVerified);
    }

    [Fact]
    public async Task FeedbackBoost_ShouldNotAffectAnotherQuery()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(ActiveCode("6109100000", "棉制针织T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(
            0, "6109100000", "6109100000", "棉制针织T恤衫", "短袖|圆领", "Manual", 2026, "ManuallyVerified", true));
        int before = Assert.Single((await service.SearchAsync("短袖")).Items).Score;

        await service.RecordFeedbackAsync(new HsCodeKnowledgeFeedbackInput(
            "棉制男式T恤衫", "棉制针织T恤衫", "短袖|圆领", "6109100000", true));

        int after = Assert.Single((await service.SearchAsync("短袖")).Items).Score;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Search_ShouldPenalizeConflictingGenderAndExplainMatchedAttributes()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.AddRange(
                ActiveCode("6109100001", "棉制针织男式T恤衫"),
                ActiveCode("6109100002", "棉制针织女式T恤衫"));
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6109100001", "6109100001", "棉制针织男式T恤衫", "针织|男式|100%棉", "Manual", 2026, "Active", true));
        await service.SaveExampleAsync(new HsCodeExampleInput(0, "6109100002", "6109100002", "棉制针织女式T恤衫", "针织|女式|100%棉", "Manual", 2026, "Active", true));

        var result = await service.SearchAsync("棉制针织男式T恤衫");

        var best = result.Items.First();
        Assert.Equal("6109100001", best.CurrentCode);
        Assert.Contains(best.MatchReasons, reason => reason.Contains("性别", StringComparison.Ordinal));
        Assert.Empty(best.ConflictWarnings);
        var female = result.Items.FirstOrDefault(item => item.CurrentCode == "6109100002");
        Assert.True(female == null || female.Score < best.Score);
    }

    private sealed class SqliteFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly DbContextOptions<AppDbContext> _options;
        public SqliteFactory() { _connection.Open(); _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options; using var context = CreateDbContext(); context.Database.EnsureCreated(); }
        public AppDbContext CreateDbContext() => new(_options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public void Dispose() { _connection.Dispose(); }
    }

    private static HsCode ActiveCode(string code, string name) => new()
    {
        Code = code,
        Name = name,
        Status = "Active",
        SourceName = "2026测试税则",
        EffectiveYear = 2026,
        LastVerifiedAt = new DateTime(2026, 1, 1)
    };
}
