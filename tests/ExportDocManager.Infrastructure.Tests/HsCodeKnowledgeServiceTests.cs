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
                new HsCode { Code = "6109100090", Name = "棉制针织男式T恤衫", Status = "Active" },
                new HsCode { Code = "9999999999", Name = "其它商品", Status = "Active" });
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
            context.HsCodes.Add(new HsCode { Code = "6110200090", Name = "棉制钩编套头衫", Status = "Active" });
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
            context.HsCodes.Add(new HsCode { Code = "6109100090", Name = "棉制T恤衫", Status = "Active" });
            context.Products.Add(new Product { ProductCode = "TS01", NameCN = "男士全棉圆领短袖", HSCode = "6109100090", Material = "100%棉", Brand = "自有品牌" });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);

        var candidate = Assert.Single(await service.DiscoverHistoryCandidatesAsync("圆领短袖"));
        Assert.True(candidate.CanConfirm);
        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));

        await service.SaveExampleAsync(new HsCodeExampleInput(0, candidate.RawCode, candidate.CurrentCode, candidate.ProductName,
            candidate.Specification, "HistoryConfirmed", 2026, "ManuallyVerified", true));
        Assert.Empty(await service.DiscoverHistoryCandidatesAsync("圆领短袖"));
        Assert.Equal(1, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task RemoteResults_ShouldStayInCandidatePoolUntilUserConfirms()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(new HsCode { Code = "6109100090", Name = "棉制T恤衫", Status = "Active" });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);

        await service.CaptureRemoteExamplesAsync("男士全棉短袖", [new HsCode { Code = "6109100090", Name = "男士全棉圆领短袖", Description = "针织，100%棉", DetailUrl = "https://www.i5a6.com/hscode/detail/6109100090" }]);

        Assert.Equal(0, await service.CountExamplesAsync(string.Empty));
        var candidate = Assert.Single(await service.ListRemoteCandidatesAsync("Pending"));
        Assert.Equal("6109100090", candidate.SuggestedCurrentHsCode);
        Assert.Equal("https://www.i5a6.com/hscode/detail/6109100090", candidate.SourceUrl);
        await service.ReviewRemoteCandidateAsync(new HsCodeRemoteCandidateReviewInput(candidate.Id, "6109100090", true));
        Assert.Empty(await service.ListRemoteCandidatesAsync("Pending"));
        Assert.Equal(1, await service.CountExamplesAsync(string.Empty));
    }

    [Fact]
    public async Task Search_ShouldSuggestSingleUnverifiedReplacementWithoutMarkingItUsable()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(new HsCode { Code = "6109100090", Name = "棉制针织或钩编的T恤衫", Status = "Active" });
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
                new HsCode { Code = "6109100011", Name = "棉制T恤衫一", Status = "Active" },
                new HsCode { Code = "6109100012", Name = "棉制T恤衫二", Status = "Active" });
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
            context.HsCodes.Add(new HsCode { Code = "6109100090", Name = "棉制T恤衫", Status = "Active" });
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
        var candidate = Assert.Single(await service.ListRemoteCandidatesAsync("Pending"));
        Assert.Equal("针织女式睡衣裤", candidate.ProductName);
        Assert.DoesNotContain(await service.ListRemoteCandidatesAsync("Pending"), item => item.ProductName == "化纤制针织女睡衣");
    }

    [Fact]
    public async Task WebRecommendation_ShouldSuggestOneActiveLocalCodeButStillRequireReview()
    {
        using var factory = new SqliteFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.HsCodes.Add(new HsCode { Code = "6109100000", Name = "棉制针织T恤衫", Status = "Active" });
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

        var candidate = Assert.Single(await service.ListRemoteCandidatesAsync("Pending"));
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
            context.HsCodes.Add(new HsCode { Code = "6109100000", Name = "棉制针织T恤衫", Status = "Active" });
            await context.SaveChangesAsync();
        }
        var service = new HsCodeKnowledgeService(factory);
        await service.CaptureRemoteExamplesAsync("女式T恤衫",
            [new HsCode { Code = "6109100022", Name = "女式T恤衫", Description = "针织|女式|棉" }]);
        var candidate = Assert.Single(await service.ListRemoteCandidatesAsync("Pending"));

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
            context.HsCodes.Add(new HsCode { Code = "6109100000", Name = "棉制针织T恤衫", Status = "Active" });
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
                new HsCode { Code = "6109100001", Name = "棉制针织男式T恤衫", Status = "Active" },
                new HsCode { Code = "6109100002", Name = "棉制针织女式T恤衫", Status = "Active" });
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
}
