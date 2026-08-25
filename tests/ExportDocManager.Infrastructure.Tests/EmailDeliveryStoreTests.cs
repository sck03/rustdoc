using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class EmailDeliveryStoreTests
{
    [Fact]
    public async Task DeliveryKey_ShouldPreventDuplicateSendAndPreserveHistory()
    {
        using var factory = new TestDbContextFactory();
        var accessScope = new BusinessDataAccessScope(
            new DatabaseConnectionSettings(),
            new FixedCurrentUserContext(new User { Id = 7, Username = "sender" }));
        var store = new EmailDeliveryStore(factory, accessScope);
        string fingerprint = EmailDeliveryFingerprint.Create(["buyer@example.com", "Invoice"]);

        var first = await store.BeginAsync("delivery-123", fingerprint, "job-1", "EmailTool", "buyer@example.com", "Invoice", 2);
        Assert.True(first.ShouldSend);
        await store.MarkSentAsync("delivery-123");

        var duplicate = await store.BeginAsync("delivery-123", fingerprint, "job-2", "EmailTool", "buyer@example.com", "Invoice", 2);
        Assert.False(duplicate.ShouldSend);
        Assert.True(duplicate.AlreadySent);

        var row = Assert.Single(await store.ListRecentAsync());
        Assert.Equal("Sent", row.Status);
        await using var context = factory.CreateDbContext();
        Assert.Equal("sender", (await context.EmailDeliveryRecords.SingleAsync()).RequestedBy);
    }

    [Fact]
    public async Task UncertainDelivery_ShouldNeverBeAutomaticallyRetried()
    {
        using var factory = new TestDbContextFactory();
        var store = new EmailDeliveryStore(factory, new BusinessDataAccessScope(new DatabaseConnectionSettings()));
        string fingerprint = EmailDeliveryFingerprint.Create(["buyer@example.com", "Docs"]);

        Assert.True((await store.BeginAsync("delivery-uncertain", fingerprint, "job-1", "ReportDocumentEmail", "buyer@example.com", "Docs", 1)).ShouldSend);
        await store.MarkUncertainAsync("delivery-uncertain", "connection closed");

        var retry = await store.BeginAsync("delivery-uncertain", fingerprint, "job-2", "ReportDocumentEmail", "buyer@example.com", "Docs", 1);
        Assert.False(retry.ShouldSend);
        Assert.False(retry.AlreadySent);
        Assert.Contains("避免重复发送", retry.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeliveryKey_ShouldRejectDifferentMessageContent()
    {
        using var factory = new TestDbContextFactory();
        var store = new EmailDeliveryStore(factory, new BusinessDataAccessScope(new DatabaseConnectionSettings()));

        Assert.True((await store.BeginAsync(
            "delivery-content",
            EmailDeliveryFingerprint.Create(["buyer@example.com", "First"]),
            "job-1",
            "EmailTool",
            "buyer@example.com",
            "First",
            0)).ShouldSend);

        var conflict = await store.BeginAsync(
            "delivery-content",
            EmailDeliveryFingerprint.Create(["buyer@example.com", "Changed"]),
            "job-2",
            "EmailTool",
            "buyer@example.com",
            "Changed",
            0);

        Assert.False(conflict.ShouldSend);
        Assert.False(conflict.AlreadySent);
        Assert.Contains("另一封邮件", conflict.ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class FixedCurrentUserContext(User user) : ICurrentUserContext
    {
        public User CurrentUser { get; } = user;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        public AppDbContext CreateDbContext() => new(_options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public void Dispose()
        {
            using var context = CreateDbContext();
            context.Database.EnsureDeleted();
        }
    }
}
