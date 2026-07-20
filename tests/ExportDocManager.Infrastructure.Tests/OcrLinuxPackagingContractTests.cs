namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class OcrLinuxPackagingContractTests
    {
        [Fact]
        public void LinuxX64AndDockerPackages_ShouldCarryAndVerifyPpOcrV6Runtime()
        {
            string root = FindWorkspaceRoot();
            string project = File.ReadAllText(Path.Combine(
                root,
                "src",
                "ExportDocManager.Infrastructure",
                "ExportDocManager.Infrastructure.csproj"));
            string dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.api"));
            string rustManifest = File.ReadAllText(Path.Combine(root, "apps", "exportdoc-ocr-rs", "Cargo.toml"));
            string workflow = File.ReadAllText(Path.Combine(
                root,
                ".github",
                "workflows",
                "browser-server-package-reusable.yml"));
            string startScript = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "start-linux.sh"));
            string notices = Path.Combine(root, "OcrModels", "PaddleOCR", "V6", "THIRD_PARTY_NOTICES.md");

            Assert.DoesNotContain("OpenCvSharp", project, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EXPORTDOCMANAGER_OCR_RUNTIME=enabled", dockerfile, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", dockerfile, StringComparison.Ordinal);
            Assert.Contains("apps/exportdoc-ocr-rs/Cargo.toml", workflow, StringComparison.Ordinal);
            Assert.Contains("sidecar/ocr", workflow, StringComparison.Ordinal);
            Assert.Contains("libonnxruntime.so", workflow, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", workflow, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", startScript, StringComparison.Ordinal);
            Assert.True(File.Exists(notices));
            Assert.Contains("Apache License 2.0", File.ReadAllText(notices), StringComparison.Ordinal);
            Assert.Contains("MIT License", File.ReadAllText(notices), StringComparison.Ordinal);
            Assert.DoesNotContain("opencv", rustManifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FROM rust:", dockerfile, StringComparison.Ordinal);
            Assert.Contains("sidecar/ocr/exportdoc-ocr", dockerfile, StringComparison.Ordinal);
        }

        private static string FindWorkspaceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("ExportDocManager workspace root was not found.");
        }
    }
}
