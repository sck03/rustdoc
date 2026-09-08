using System.Security.Cryptography;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BusinessAttachmentServiceTests
{
    [Fact]
    public async Task UploadAndConfirm_ShouldPreserveOriginalBytesAndRequireExplicitVersionSelection()
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        var service = Service(db);
        var request = Upload("cafe\u0301.txt");
        var first = await service.UploadAsync(invoice, request, Bytes("客户原始要求"));
        Assert.Null(first.CurrentRevision);
        var confirmed = await service.UpdateAsync(first.Id, new(first.VersionNumber, 1, false, "客户已确认"));
        var second = await service.UploadAsync(invoice, request with
        { AttachmentId = first.Id, ExpectedVersion = confirmed.VersionNumber, UploadKey = Guid.NewGuid(), FileName = "新要求.txt" }, Bytes("客户修改要求"));
        Assert.Equal(1, second.CurrentRevision);
        Assert.Equal(2, second.LatestRevision);
        Assert.Equal("客户原始要求", System.Text.Encoding.UTF8.GetString((await service.ReadAsync(first.Id, 1)).Content));
        var updated = await service.UpdateAsync(first.Id, new(second.VersionNumber, 2, false, "采用新要求"));
        var details = await service.GetAsync(first.Id);
        Assert.Equal(2, updated.CurrentRevision);
        Assert.Equal(2, details.Revisions.Count);
        Assert.Equal("café.txt", details.Revisions[1].FileName);
        Assert.Equal(2, details.EventCount);
        var search = await service.QueryAsync(new(Keyword: "café"));
        Assert.Single(search.Page.Items);
        Assert.Equal(first.Id, search.Page.Items[0].Id);
        var usage = await service.QueryAsync(new(InvoiceId: invoice));
        Assert.True(usage.CanUpload);
        Assert.Equal("客户原始要求"u8.Length + "客户修改要求"u8.Length, usage.UsedBytes);
    }

    [Fact]
    public async Task UploadRetry_ShouldBeIdempotentAndRejectReusedKeysWithDifferentContent()
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        var service = Service(db);
        var request = Upload();
        var first = await service.UploadAsync(invoice, request, Bytes("first"));
        var repeated = await service.UploadAsync(invoice, request, Bytes("first"));
        Assert.Equal(first.Id, repeated.Id);
        Assert.Single((await service.GetAsync(first.Id)).Revisions);
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.UploadAsync(invoice, request, Bytes("different")));
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => service.UpdateAsync(first.Id, new(500, 1, false, "过期版本")));
    }

    [Fact]
    public async Task Access_ShouldFollowInvoiceViewAndOperationScopesIncludingOldVersions()
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        var item = await Service(db).UploadAsync(invoice, Upload(), Bytes("private"));
        var user = new User
        {
            Id = 8,
            Username = "reader",
            Role = "User",
            CompanyScope = "C1",
            IsActive = true,
            EffectivePermissionGrants = new Dictionary<string, string>
            {
                [PermissionResourceCatalog.CreateGrantKey(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View)] = PermissionDataScope.Company,
                [PermissionResourceCatalog.CreateGrantKey(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate)] = PermissionDataScope.Own
            }
        };
        var reader = Service(db, user);
        Assert.False((await reader.GetAsync(item.Id)).Attachment.CanEdit);
        Assert.False((await reader.QueryAsync(new(InvoiceId: invoice))).CanUpload);
        Assert.Equal("private", System.Text.Encoding.UTF8.GetString((await reader.ReadAsync(item.Id, 1)).Content));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => reader.UpdateAsync(item.Id, new(item.VersionNumber, 1, false, "无操作权限")));
        user.CompanyScope = "C2";
        Assert.Empty((await reader.QueryAsync(new())).Page.Items);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => reader.ReadAsync(item.Id, 1));
    }

    [Fact]
    public async Task ArchiveAndIntegrity_ShouldRetainBytesAndFailClosedOnCorruption()
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        var service = Service(db);
        var item = await service.UploadAsync(invoice, Upload(), Bytes("source"));
        await service.UpdateAsync(item.Id, new(item.VersionNumber, null, true, "资料停用"));
        Assert.Empty((await service.QueryAsync(new())).Page.Items);
        Assert.Single((await service.QueryAsync(new(IncludeArchived: true))).Page.Items);
        Assert.NotEmpty((await service.ReadAsync(item.Id, 1)).Content);
        using (var context = db.CreateDbContext())
        {
            var version = await context.BusinessAttachmentRevisions.SingleAsync();
            version.Content = "tampered"u8.ToArray();
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() => service.ReadAsync(item.Id, 1));
    }

    [Theory]
    [InlineData("../source.txt")]
    [InlineData("CON.txt")]
    [InlineData("source.txt.")]
    [InlineData("source.exe")]
    [InlineData("source.pdf")]
    public async Task Upload_ShouldRejectUnsafeNamesAndMismatchedContent(string name)
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        await Assert.ThrowsAsync<ServiceValidationException>(() => Service(db).UploadAsync(invoice, Upload(name), Bytes("text")));
        using var context = db.CreateDbContext();
        Assert.Empty(context.BusinessAttachments);
    }

    [Fact]
    public async Task Upload_ShouldEnforceFileAndInvoiceBudgetsWithoutPartialRecords()
    {
        using var db = new SqliteTestDatabase();
        int invoice = await SeedAsync(db);
        var service = Service(db);
        await Assert.ThrowsAsync<PayloadLimitExceededException>(() => service.UploadAsync(invoice, Upload(), new MemoryStream(new byte[BusinessAttachmentLimits.FileBytes + 1])));
        var item = await service.UploadAsync(invoice, Upload(), Bytes("ok"));
        using (var context = db.CreateDbContext())
        {
            context.BusinessAttachmentRevisions.Single().Length = (int)BusinessAttachmentLimits.InvoiceBytes;
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.UploadAsync(invoice, Upload(), Bytes("over budget")));
        Assert.Single((await service.QueryAsync(new())).Page.Items);
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var context = db.CreateDbContext();
            context.Invoices.Remove(await context.Invoices.SingleAsync());
            await context.SaveChangesAsync();
        });
    }

    internal static BusinessAttachmentUpload Upload(string name = "source.txt") => new(null, 0, Guid.NewGuid(), "客户资料",
        BusinessAttachmentCategory.Original, "PO-2026", "STYLE-1", name, "初始资料");
    internal static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text), false);
    internal static BusinessAttachmentService Service(IDbContextFactory<AppDbContext> db, User? user = null) =>
        new(db, BusinessFeatureTestRuntime.Scope(user ?? BusinessFeatureTestRuntime.Admin()), BusinessFeatureTestRuntime.Clock, new BusinessFeatureTestRuntime());
    internal static async Task<int> SeedAsync(IDbContextFactory<AppDbContext> db)
    {
        using var context = db.CreateDbContext();
        var invoice = BusinessFeatureTestRuntime.Invoice("ATTACHMENT-1");
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice.Id;
    }
}
