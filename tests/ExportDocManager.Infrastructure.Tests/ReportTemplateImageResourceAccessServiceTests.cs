using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ReportTemplateImageResourceAccessServiceTests
{
    [Fact]
    public void CanonicalTemplate_ShouldRemainAValidV3ResourceReference()
    {
        const string resourceId = "img-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";
        string content = CanonicalTemplate(resourceId);
        int markerStart = content.IndexOf("EXPORTDOC_REPORT_DESIGNER_SCHEMA", StringComparison.Ordinal) +
                          "EXPORTDOC_REPORT_DESIGNER_SCHEMA".Length;
        int markerEnd = content.IndexOf("-->", markerStart, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(content[markerStart..markerEnd]);

        ReportTemplateV3SchemaValidator.Validate(ReportDocumentType.ExportDocument, document.RootElement);
    }

    [Fact]
    public async Task CanReadAsync_ShouldUsePersistedVisibleTemplateRelations()
    {
        using var factory = new InMemoryTestDatabase();
        const string ownResourceId = "img-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";
        const string privateResourceId = "img-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png";
        const string sharedResourceId = "img-cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc.png";
        const string unreferencedResourceId = "img-dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd.png";
        using (var seed = factory.CreateDbContext())
        {
            var own = Template(7, "我的草稿", TemplateLifecycleStatusCatalog.Draft, TemplateShareScopeCatalog.Private);
            var privateTemplate = Template(8, "他人私有模板", TemplateLifecycleStatusCatalog.Published, TemplateShareScopeCatalog.Private);
            var shared = Template(8, "共享模板", TemplateLifecycleStatusCatalog.Published, TemplateShareScopeCatalog.All);
            seed.UserReportTemplates.AddRange(own, privateTemplate, shared);
            foreach (string id in new[] { ownResourceId, privateResourceId, sharedResourceId, unreferencedResourceId })
            {
                seed.ReportTemplateImageResources.Add(Resource(id));
            }
            seed.UserReportTemplateResourceReferences.AddRange(
                Reference(own, ownResourceId, ReportTemplateResourceReferenceKind.Draft),
                Reference(privateTemplate, privateResourceId, ReportTemplateResourceReferenceKind.Published),
                Reference(shared, sharedResourceId, ReportTemplateResourceReferenceKind.Published));
            await seed.SaveChangesAsync();
        }

        var resourceOnlyService = new ReportTemplateImageResourceAccessService(
            factory,
            CreateScope(CreateResourceUser(7, canViewTemplates: false)));
        Assert.False(await resourceOnlyService.CanReadAsync(ownResourceId));
        Assert.False(await resourceOnlyService.CanReadAsync(sharedResourceId));

        var service = new ReportTemplateImageResourceAccessService(
            factory,
            CreateScope(CreateResourceUser(7)));

        Assert.True(await service.CanReadAsync(ownResourceId));
        Assert.False(await service.CanReadAsync(privateResourceId));
        Assert.True(await service.CanReadAsync(sharedResourceId));
        Assert.False(await service.CanReadAsync(unreferencedResourceId));
        Assert.False(await service.CanReadAsync("../outside.png"));
    }

    [Fact]
    public async Task UploadClaim_ShouldAuthorizeOwnerAndRecycleOnlyWhenUnreferenced()
    {
        using var factory = new InMemoryTestDatabase();
        const string resourceId = "img-eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee.png";
        var service = new ReportTemplateImageResourceAccessService(
            factory,
            CreateScope(CreateResourceUser(7)));
        var resource = new ReportTemplateImageResource
        {
            Id = resourceId,
            Sha256 = new string('e', 64),
            MediaType = "image/png",
            ByteLength = 68
        };

        await service.RegisterUploadAsync(resource);
        Assert.True(await service.CanReadAsync(resourceId));

        using (var context = factory.CreateDbContext())
        {
            var template = Template(7, "引用资源", TemplateLifecycleStatusCatalog.Draft, TemplateShareScopeCatalog.Private);
            context.UserReportTemplates.Add(template);
            context.UserReportTemplateResourceReferences.Add(Reference(
                template,
                resourceId,
                ReportTemplateResourceReferenceKind.Draft));
            await context.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ResourceConflictException>(() => service.RecycleAsync(resourceId));

        using (var context = factory.CreateDbContext())
        {
            context.UserReportTemplateResourceReferences.RemoveRange(context.UserReportTemplateResourceReferences);
            await context.SaveChangesAsync();
        }

        Assert.True(await service.RecycleAsync(resourceId));
        Assert.False(await service.CanReadAsync(resourceId));
        using var verify = factory.CreateDbContext();
        Assert.NotNull((await verify.ReportTemplateImageResources.SingleAsync()).RecycledAt);
    }

    [Fact]
    public async Task RollbackRecycleAsync_ShouldRestoreClaimForPhysicalDeleteRetry()
    {
        using var factory = new InMemoryTestDatabase();
        const string resourceId = "img-ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff.png";
        var service = new ReportTemplateImageResourceAccessService(
            factory,
            CreateScope(CreateResourceUser(7)));
        await service.RegisterUploadAsync(new ReportTemplateImageResource
        {
            Id = resourceId,
            Sha256 = new string('f', 64),
            MediaType = "image/png",
            ByteLength = 68
        });

        Assert.True(await service.RecycleAsync(resourceId));
        await service.RollbackRecycleAsync(resourceId);
        Assert.True(await service.CanReadAsync(resourceId));
        Assert.True(await service.RecycleAsync(resourceId));
    }

    private static UserReportTemplate Template(int ownerId, string name, string status, string shareScope) =>
        new()
        {
            OwnerUserId = ownerId,
            ReportType = ReportDocumentType.ExportDocument.ToString(),
            Name = name,
            ContentHtml = "<html></html>",
            Status = status,
            ShareScope = shareScope
        };

    private static ReportTemplateImageResourceEntry Resource(string id) => new()
    {
        Id = id,
        Sha256 = id[4..68],
        MediaType = "image/png",
        ByteLength = 68,
        CreatedByUserId = 7,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static UserReportTemplateResourceReference Reference(
        UserReportTemplate template,
        string resourceId,
        string kind) =>
        new()
        {
            Template = template,
            ResourceId = resourceId,
            ReferenceKind = kind,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static User CreateResourceUser(int id, bool canViewTemplates = true)
    {
        var actions = PermissionResourceCatalog.ByKey[PermissionResourceCatalog.ReportResources].Actions;
        var grants = actions.ToDictionary(
            action => PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.ReportResources, action.Key),
            _ => PermissionDataScope.All,
            StringComparer.OrdinalIgnoreCase);
        if (canViewTemplates)
        {
            grants[PermissionResourceCatalog.CreateGrantKey(
                PermissionResourceCatalog.ReportTemplates, PermissionAction.View)] = PermissionDataScope.All;
        }
        return new User
        {
            Id = id,
            Username = $"resource-{id}",
            Role = UserRoleCatalog.User,
            EffectivePermissionGrants = grants
        };
    }

    private static BusinessDataAccessScope CreateScope(User user) =>
        new(CreatePostgreSqlSettings(), new FixedCurrentUserContext(user));

    private static string CanonicalTemplate(string resourceId)
    {
        string digest = resourceId[4..68];
        return $$"""
            <!doctype html><html><body>
            <!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA
            {
              "version": 3,
              "astKind": "ReportDocument",
              "coordinateUnit": "hundredth-mm",
              "contractVersion": "3.0",
              "reportType": "ExportDocument",
              "page": {
                "size": "A4", "orientation": "Portrait", "widthHundredthMm": 21000, "heightHundredthMm": 29700,
                "marginTopHundredthMm": 800, "marginRightHundredthMm": 800, "marginBottomHundredthMm": 800, "marginLeftHundredthMm": 800,
                "fontFamily": "Arial", "fontSizePt": 9
              },
              "grid": { "enabled": true, "sizeHundredthMm": 500, "snap": true },
              "resources": [{ "id": "{{resourceId}}", "mediaType": "image/png", "byteLength": 68, "sha256": "{{digest}}" }],
              "layers": [{
                "id": "body", "name": "主体", "role": "Body",
                "print": { "repeatOnEveryPage": false, "keepTogether": false, "pinToPageBottom": false, "minHeightHundredthMm": 0 },
                "visible": true, "locked": false,
                "elements": [
                  { "id": "image", "type": "Image", "sourceKind": "Resource", "purpose": "Image", "resourceId": "{{resourceId}}",
                    "xHundredthMm": 1000, "yHundredthMm": 1000, "widthHundredthMm": 1000, "heightHundredthMm": 1000,
                    "rotationDeg": 0, "zIndex": 0, "visible": true, "locked": false, "style": {}, "outputEnabled": true, "hideWhenSourceEmpty": true }
                ]
              }]
            }
            -->
            </body></html>
            """;
    }

    private static DatabaseConnectionSettings CreatePostgreSqlSettings() => new()
    {
        Provider = DatabaseConnectionSettings.PostgreSqlProvider,
        PostgreSqlHost = "127.0.0.1",
        PostgreSqlDatabase = "test",
        PostgreSqlUsername = "test"
    };


}
