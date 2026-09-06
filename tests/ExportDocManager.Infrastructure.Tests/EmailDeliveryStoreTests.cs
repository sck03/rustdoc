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
        using var factory = new InMemoryTestDatabase();
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
    public async Task DeliveryHistory_ShouldApplyDepartmentAndCompanyPermissionScopes()
    {
        using var factory = new InMemoryTestDatabase();
        var databaseSettings = new DatabaseConnectionSettings
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider
        };
        User viewer = CreateScopedUser(7, "viewer", "sales", "acme", PermissionDataScope.Department);
        User departmentPeer = CreateScopedUser(8, "peer", "sales", "acme", PermissionDataScope.Own);
        User companyPeer = CreateScopedUser(9, "finance", "finance", "acme", PermissionDataScope.Own);
        User outsideCompany = CreateScopedUser(10, "outside", "sales", "other", PermissionDataScope.Own);

        foreach (User sender in new[] { viewer, departmentPeer, companyPeer, outsideCompany })
        {
            var senderStore = new EmailDeliveryStore(
                factory,
                new BusinessDataAccessScope(databaseSettings, new FixedCurrentUserContext(sender)));
            Assert.True((await senderStore.BeginAsync(
                $"delivery-{sender.Id}",
                EmailDeliveryFingerprint.Create([sender.Username, "scope-test"]),
                string.Empty,
                "EmailTool",
                $"{sender.Username}@example.com",
                sender.Username,
                0)).ShouldSend);
        }

        var departmentStore = new EmailDeliveryStore(
            factory,
            new BusinessDataAccessScope(databaseSettings, new FixedCurrentUserContext(viewer)));
        var departmentRows = await departmentStore.ListRecentAsync();
        Assert.Equal(new[] { "peer", "viewer" }, departmentRows.Select(row => row.Subject).Order().ToArray());

        viewer.EffectivePermissionGrants = CreateDeliveryHistoryGrant(PermissionDataScope.Company);
        var companyRows = await departmentStore.ListRecentAsync();
        Assert.Equal(new[] { "finance", "peer", "viewer" }, companyRows.Select(row => row.Subject).Order().ToArray());
    }

    [Fact]
    public async Task DeliveryKey_ShouldBeIsolatedPerUser()
    {
        using var factory = new InMemoryTestDatabase();
        var databaseSettings = new DatabaseConnectionSettings();
        User firstUser = CreateScopedUser(7, "first", "sales", "acme", PermissionDataScope.Own);
        User secondUser = CreateScopedUser(8, "second", "sales", "acme", PermissionDataScope.Own);
        var firstStore = new EmailDeliveryStore(
            factory,
            new BusinessDataAccessScope(databaseSettings, new FixedCurrentUserContext(firstUser)));
        var secondStore = new EmailDeliveryStore(
            factory,
            new BusinessDataAccessScope(databaseSettings, new FixedCurrentUserContext(secondUser)));

        Assert.True((await firstStore.BeginAsync(
            "shared-browser-key",
            EmailDeliveryFingerprint.Create(["first"]),
            string.Empty,
            "EmailTool",
            "first@example.com",
            "First",
            0)).ShouldSend);
        Assert.True((await secondStore.BeginAsync(
            "shared-browser-key",
            EmailDeliveryFingerprint.Create(["second"]),
            string.Empty,
            "EmailTool",
            "second@example.com",
            "Second",
            0)).ShouldSend);

        await using var context = factory.CreateDbContext();
        Assert.Equal(2, await context.EmailDeliveryRecords.CountAsync());
    }

    [Fact]
    public async Task UncertainDelivery_ShouldNeverBeAutomaticallyRetried()
    {
        using var factory = new InMemoryTestDatabase();
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
        using var factory = new InMemoryTestDatabase();
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


    private static User CreateScopedUser(
        int id,
        string username,
        string departmentId,
        string companyScope,
        string dataScope) =>
        new()
        {
            Id = id,
            Username = username,
            DepartmentId = departmentId,
            CompanyScope = companyScope,
            PermissionTemplateId = 1,
            EffectivePermissionGrants = CreateDeliveryHistoryGrant(dataScope)
        };

    private static IReadOnlyDictionary<string, string> CreateDeliveryHistoryGrant(string dataScope) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PermissionResourceCatalog.CreateGrantKey(
                PermissionResourceCatalog.EmailDelivery,
                PermissionAction.ViewDelivery)] = dataScope
        };

}
