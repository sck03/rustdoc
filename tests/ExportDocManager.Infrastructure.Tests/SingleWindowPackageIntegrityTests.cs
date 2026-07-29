using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SingleWindowPackageIntegrityTests
    {
        [Fact]
        public async Task ValidateAsync_ShouldRejectFilesMissingFromManifest()
        {
            string root = CreateTempRoot();
            try
            {
                var manifest = await CreateValidSubmitPackageAsync(root);
                string extraPath = Path.Combine(root, "payloads", "undeclared.xml");
                await File.WriteAllTextAsync(extraPath, "<Unexpected />");

                var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    SingleWindowPackageIntegrity.ValidateAsync(
                        root,
                        manifest,
                        SingleWindowPackageType.SubmitPackage,
                        CancellationToken.None));

                Assert.Contains("未在 manifest 声明", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task ValidateAsync_ShouldRejectCaseInsensitiveDuplicatePaths()
        {
            string root = CreateTempRoot();
            try
            {
                var manifest = await CreateValidSubmitPackageAsync(root);
                var original = Assert.Single(manifest.PayloadFiles);
                manifest = CloneWithPayloadFiles(
                    manifest,
                    [
                        original,
                        new SingleWindowPackageFile
                        {
                            RelativePath = $"payloads/{Path.GetFileName(original.RelativePath).ToUpperInvariant()}",
                            MediaType = original.MediaType,
                            Description = original.Description,
                            SizeBytes = original.SizeBytes,
                            Sha256 = original.Sha256
                        }
                    ]);
                manifest.ContentDigest = SingleWindowPackageIntegrity.ComputeContentDigest(manifest);

                var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    SingleWindowPackageIntegrity.ValidateAsync(
                        root,
                        manifest,
                        SingleWindowPackageType.SubmitPackage,
                        CancellationToken.None));

                Assert.Contains("重复文件路径", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task ValidateAsync_ShouldAcceptOnlyDeclaredSchemaThreeFiles()
        {
            string root = CreateTempRoot();
            try
            {
                var manifest = await CreateValidSubmitPackageAsync(root);

                await SingleWindowPackageIntegrity.ValidateAsync(
                    root,
                    manifest,
                    SingleWindowPackageType.SubmitPackage,
                    CancellationToken.None);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static async Task<SingleWindowPackageManifest> CreateValidSubmitPackageAsync(string root)
        {
            string payloadDirectory = Path.Combine(root, "payloads");
            Directory.CreateDirectory(payloadDirectory);
            string payloadPath = Path.Combine(payloadDirectory, "invoice-coo.xml");
            string snapshotPath = Path.Combine(root, "snapshot.json");
            await File.WriteAllTextAsync(payloadPath, "<Certificate />");
            await File.WriteAllTextAsync(snapshotPath, "{}");

            var payload = await SingleWindowPackageIntegrity.DescribeFileAsync(
                payloadPath,
                PathBoundaryHelper.ToProtocolRelativePath("payloads", "invoice-coo.xml"),
                "application/xml",
                "COO",
                CancellationToken.None);
            var manifest = new SingleWindowPackageManifest
            {
                PackageType = SingleWindowPackageType.SubmitPackage,
                BusinessType = SingleWindowBusinessType.CustomsCoo,
                BatchReference = "COO-V001-20260729120000-ABCDEF123456",
                SourceInvoiceId = 1,
                SourceDocumentId = 2,
                SourceDocumentType = "CustomsCooDocument",
                SubmissionVersion = 1,
                DraftRevision = 1,
                SourceBaselineHash = "BASELINE",
                InvoiceNo = "INV-001",
                ContractNo = "CON-001",
                CompanyScope = "测试公司",
                SnapshotSha256 = await SingleWindowPackageIntegrity.ComputeFileSha256Async(
                    snapshotPath,
                    CancellationToken.None),
                PayloadFiles = [payload],
                AttachmentFiles = [],
                Warnings = [],
                CreatedAt = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc),
                CreatedOnMachine = "TEST-STATION"
            };
            manifest.ContentDigest = SingleWindowPackageIntegrity.ComputeContentDigest(manifest);
            await File.WriteAllTextAsync(
                Path.Combine(root, "manifest.json"),
                JsonSerializer.Serialize(manifest));
            return manifest;
        }

        private static SingleWindowPackageManifest CloneWithPayloadFiles(
            SingleWindowPackageManifest source,
            IReadOnlyList<SingleWindowPackageFile> payloadFiles)
        {
            return new SingleWindowPackageManifest
            {
                SchemaVersion = source.SchemaVersion,
                PackageId = source.PackageId,
                PackageType = source.PackageType,
                BusinessType = source.BusinessType,
                BatchReference = source.BatchReference,
                SourceInvoiceId = source.SourceInvoiceId,
                SourceDocumentId = source.SourceDocumentId,
                SourceDocumentType = source.SourceDocumentType,
                SubmissionVersion = source.SubmissionVersion,
                DraftRevision = source.DraftRevision,
                SourceBaselineHash = source.SourceBaselineHash,
                InvoiceNo = source.InvoiceNo,
                ContractNo = source.ContractNo,
                CompanyScope = source.CompanyScope,
                SnapshotSha256 = source.SnapshotSha256,
                SourcePackageDigest = source.SourcePackageDigest,
                StationKey = source.StationKey,
                CardIdentifier = source.CardIdentifier,
                ClientProfileKey = source.ClientProfileKey,
                ClientProfileName = source.ClientProfileName,
                CreatedAt = source.CreatedAt,
                CreatedOnMachine = source.CreatedOnMachine,
                PayloadFiles = payloadFiles,
                AttachmentFiles = source.AttachmentFiles,
                Warnings = source.Warnings
            };
        }

        private static string CreateTempRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), $"edm-swpkg-integrity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
