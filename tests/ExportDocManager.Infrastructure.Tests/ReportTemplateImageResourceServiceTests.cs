using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ReportTemplateImageResourceServiceTests
{
    private static readonly byte[] Png =
    [
        137, 80, 78, 71, 13, 10, 26, 10,
        0, 0, 0, 0
    ];

    [Fact]
    public async Task StoreAsync_ShouldCreateStableSha256ResourceAndTrimReadId()
    {
        string root = CreateTestRoot("image-resource-stable");
        try
        {
            var service = CreateService(root);
            ReportTemplateImageResource first;
            await using (var input = new MemoryStream(Png, writable: false))
            {
                first = await service.StoreAsync(input, "公司 Logo.png", "image/png");
            }

            await using var duplicateInput = new MemoryStream(Png, writable: false);
            ReportTemplateImageResource duplicate = await service.StoreAsync(
                duplicateInput,
                "另一个文件名.png",
                "image/png");
            ReportTemplateImageResourceContent loaded = await service.ReadAsync($"  {first.Id}  ");

            string expectedHash = Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant();
            Assert.Equal($"img-{expectedHash}.png", first.Id);
            Assert.Equal(first.Id, duplicate.Id);
            Assert.Equal(expectedHash, first.Sha256);
            Assert.Equal(Png.LongLength, first.ByteLength);
            Assert.Equal("公司 Logo", first.AltText);
            Assert.Equal(Png, loaded.Content);
            Assert.Equal(first.Id, loaded.Resource.Id);
            Assert.True(File.Exists(Path.Combine(
                root,
                "data",
                "Templates",
                "Resources",
                "V3",
                first.Id)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [MemberData(nameof(SupportedImageFormats))]
    public async Task StoreAsync_ShouldDetectSupportedImageFormats(
        byte[] content,
        string mediaType,
        string extension)
    {
        string root = CreateTestRoot($"image-resource-{extension}");
        try
        {
            var service = CreateService(root);
            await using var input = new MemoryStream(content, writable: false);
            ReportTemplateImageResource result = await service.StoreAsync(
                input,
                $"sample.{extension}",
                mediaType);

            Assert.Equal(mediaType, result.MediaType);
            Assert.EndsWith($".{extension}", result.Id, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    public static IEnumerable<object[]> SupportedImageFormats() =>
    [
        [Png, "image/png", "png"],
        [new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0xFF, 0xD9 }, "image/jpeg", "jpg"],
        [Encoding.ASCII.GetBytes("GIF89a"), "image/gif", "gif"],
        [Encoding.ASCII.GetBytes("RIFF0000WEBP"), "image/webp", "webp"]
    ];

    [Fact]
    public async Task StoreAsync_ShouldRejectMismatchedMediaType()
    {
        string root = CreateTestRoot("image-resource-media-type");
        try
        {
            var service = CreateService(root);
            await using var input = new MemoryStream(Png, writable: false);

            var error = await Assert.ThrowsAsync<ServiceValidationException>(() =>
                service.StoreAsync(input, "image.jpg", "image/jpeg"));

            Assert.Contains("声明类型", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StoreAsync_ShouldRejectPayloadAboveMaximum()
    {
        string root = CreateTestRoot("image-resource-size");
        try
        {
            var service = CreateService(root);
            await using var input = new MemoryStream(
                new byte[ReportTemplateV3ContractCatalog.MaxResourceBytes + 1],
                writable: false);

            await Assert.ThrowsAsync<PayloadLimitExceededException>(() =>
                service.StoreAsync(input, "too-large.png", "image/png"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReadAsync_ShouldRejectInvalidMissingAndTamperedResources()
    {
        string root = CreateTestRoot("image-resource-integrity");
        try
        {
            var service = CreateService(root);
            string validId = $"img-{new string('a', 64)}.png";

            await Assert.ThrowsAsync<ServiceValidationException>(() =>
                service.ReadAsync("../outside.png"));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                service.ReadAsync(validId));

            await using var input = new MemoryStream(Png, writable: false);
            ReportTemplateImageResource stored = await service.StoreAsync(input, "seal.png", "image/png");
            string path = Path.Combine(root, "data", "Templates", "Resources", "V3", stored.Id);
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("tampered"));

            var error = await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() =>
                service.ReadAsync(stored.Id));
            Assert.Contains("校验失败", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StoreAsync_ShouldEnforceResourceCountAcrossConcurrentUploads()
    {
        string root = CreateTestRoot("image-resource-count");
        try
        {
            string resourceRoot = Path.Combine(root, "data", "Templates", "Resources", "V3");
            Directory.CreateDirectory(resourceRoot);
            for (int index = 0; index < ReportTemplateV3ContractCatalog.MaxResources - 1; index++)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(resourceRoot, $"existing-{index:0000}.bin"),
                    [1]);
            }

            var service = CreateService(root);
            Task<Exception?>[] uploads =
            [
                CaptureExceptionAsync(service, Png, "first.png"),
                CaptureExceptionAsync(service, [.. Png, 1], "second.png")
            ];
            Exception?[] errors = await Task.WhenAll(uploads);

            Assert.Single(errors, error => error is null);
            var limitError = Assert.IsType<ServiceValidationException>(
                Assert.Single(errors, error => error is not null));
            Assert.Contains("1000", limitError.Message, StringComparison.Ordinal);
            Assert.Equal(ReportTemplateV3ContractCatalog.MaxResources, Directory.GetFiles(resourceRoot).Length);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HydrateAsync_ShouldReplaceControlledResourceMarkerWithDataUri()
    {
        string root = CreateTestRoot("image-resource-hydrate");
        try
        {
            var service = CreateService(root);
            await using var input = new MemoryStream(Png, writable: false);
            ReportTemplateImageResource resource = await service.StoreAsync(input, "seal.png", "image/png");
            string html = $$"""
                <!doctype html><html><body>
                <!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA { "version": 3, "resources": [{ "id": "{{resource.Id}}", "mediaType": "{{resource.MediaType}}", "byteLength": {{resource.ByteLength}}, "sha256": "{{resource.Sha256}}" }] } -->
                <img data-edm-v3-resource-id="{{resource.Id}}" alt="seal">
                </body></html>
                """;

            string hydrated = await new ReportTemplateV3ImageResourceHydrator(service).HydrateAsync(html);

            Assert.Contains("src=\"data:image/png;base64,", hydrated, StringComparison.Ordinal);
            Assert.DoesNotContain(ReportTemplateV3ImageResourceHydrator.ResourceIdAttribute, hydrated, StringComparison.Ordinal);
            Assert.DoesNotContain("src=\"http", hydrated, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HydrateAsync_ShouldFailWhenReferencedResourceIsMissing()
    {
        string root = CreateTestRoot("image-resource-hydrate-missing");
        try
        {
            string resourceId = $"img-{new string('b', 64)}.png";
            string html = $$"""
                <!doctype html><html><body>
                <!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA { "resources": [{ "id": "{{resourceId}}", "mediaType": "image/png" }] } -->
                <img data-edm-v3-resource-id="{{resourceId}}" alt="missing">
                </body></html>
                """;

            var error = await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() =>
                new ReportTemplateV3ImageResourceHydrator(CreateService(root)).HydrateAsync(html));

            Assert.Contains("不可用", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HydrateAsync_ShouldRejectMarkerOutsideImageAndUnsafeRenderedUrl()
    {
        string root = CreateTestRoot("image-resource-hydrate-policy");
        try
        {
            var service = CreateService(root);
            await using var input = new MemoryStream(Png, writable: false);
            ReportTemplateImageResource resource = await service.StoreAsync(input, "seal.png", "image/png");
            string schemaComment =
                $"<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA {{ \"resources\": [{{ \"id\": \"{resource.Id}\", \"mediaType\": \"image/png\" }}] }} -->";
            var hydrator = new ReportTemplateV3ImageResourceHydrator(service);

            await Assert.ThrowsAsync<ServiceValidationException>(() =>
                hydrator.HydrateAsync($"<html><body>{schemaComment}<div data-edm-v3-resource-id=\"{resource.Id}\"></div></body></html>"));
            await Assert.ThrowsAsync<ServiceValidationException>(() =>
                hydrator.HydrateAsync($"<html><body>{schemaComment}<img data-edm-v3-resource-id=\"{resource.Id}\"><img src=\"https://example.invalid/image.png\"></body></html>"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StoreAsync_ShouldRejectLinkedResourceDirectory()
    {
        string root = CreateTestRoot("image-resource-link");
        string outside = CreateTestRoot("image-resource-link-outside");
        string templateRoot = Path.Combine(root, "data", "Templates");
        string link = Path.Combine(templateRoot, "Resources");
        Directory.CreateDirectory(templateRoot);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            var service = CreateService(root);
            await using var input = new MemoryStream(Png, writable: false);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.StoreAsync(input, "linked.png", "image/png"));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link, recursive: false);
            }
            DeleteDirectory(root);
            DeleteDirectory(outside);
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(
        IReportTemplateImageResourceService service,
        byte[] content,
        string fileName)
    {
        try
        {
            await using var input = new MemoryStream(content, writable: false);
            await service.StoreAsync(input, fileName, "image/png");
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static IReportTemplateImageResourceService CreateService(string root) =>
        new ReportTemplateImageResourceService(
            new RuntimeAppPathProvider(
                Path.Combine(root, "app"),
                Path.Combine(root, "data")));

    private static string CreateTestRoot(string prefix)
    {
        string root = Path.Combine(
            FindRepositoryRoot(),
            ".codex-runtime",
            "ExportDocManager.Infrastructure.Tests",
            $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Templates", "Export", "invoice_template.html")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("未找到包含 Templates 的仓库根目录。");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
