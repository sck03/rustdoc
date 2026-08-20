namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class OcrLinuxPackagingContractTests
    {
        [Fact]
        public void LinuxX64AndDockerPackages_ShouldCarryAndIndependentlyVerifyPpOcrV6Runtime()
        {
            string root = FindWorkspaceRoot();
            string project = File.ReadAllText(Path.Combine(
                root,
                "src",
                "ExportDocManager.Infrastructure.PdfOcr",
                "ExportDocManager.Infrastructure.PdfOcr.csproj"));
            string apiProject = File.ReadAllText(Path.Combine(
                root,
                "src",
                "ExportDocManager.Api",
                "ExportDocManager.Api.csproj"));
            string buildProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
            string packageProps = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
            string dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.api"));
            string rustManifest = File.ReadAllText(Path.Combine(root, "apps", "exportdoc-ocr-rs", "Cargo.toml"));
            string workflow = File.ReadAllText(Path.Combine(
                root,
                ".github",
                "workflows",
                "browser-server-package-reusable.yml"));
            string containerWorkflow = File.ReadAllText(Path.Combine(
                root,
                ".github",
                "workflows",
                "container-runtime-validation.yml"));
            string containerOcrVerifier = File.ReadAllText(Path.Combine(
                root,
                "deploy",
                "container",
                "verify-api-ocr-runtime.sh"));
            string startScript = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "start-linux.sh"));
            string notices = Path.Combine(root, "OcrModels", "PaddleOCR", "V6", "THIRD_PARTY_NOTICES.md");

            Assert.DoesNotContain("OpenCvSharp", project, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("System.IO.Path]::Combine", project, StringComparison.Ordinal);
            Assert.DoesNotContain("$(PkgMicrosoft_ML_OnnxRuntime)\\runtimes\\", project, StringComparison.Ordinal);
            Assert.Contains("_ExportDocOnnxRuntimeFile", buildProps, StringComparison.Ordinal);
            Assert.Contains("<MicrosoftMlOnnxRuntimeVersion>1.29.0</MicrosoftMlOnnxRuntimeVersion>", packageProps, StringComparison.Ordinal);
            Assert.Contains("Include=\"Microsoft.ML.OnnxRuntime\" Version=\"$(MicrosoftMlOnnxRuntimeVersion)\"", packageProps, StringComparison.Ordinal);
            Assert.Contains("<ExportDocIncludeOcrRuntime Condition=\"'$(ExportDocIncludeOcrRuntime)' == ''\">true", apiProject, StringComparison.Ordinal);
            Assert.Contains("CopyOcrNativeRuntimeToPublish", apiProject, StringComparison.Ordinal);
            Assert.Contains("$(NuGetPackageRoot)", apiProject, StringComparison.Ordinal);
            Assert.Contains("$(_ExportDocOnnxRuntimeSource)", apiProject, StringComparison.Ordinal);
            Assert.Contains("'$(ExportDocIncludePdfOcrModule)' == 'true'", apiProject, StringComparison.Ordinal);
            Assert.Contains("'$(ExportDocIncludePdfOcrModule)' != 'true' or '$(ExportDocIncludeOcrRuntime)' != 'true'", apiProject, StringComparison.Ordinal);
            Assert.DoesNotContain("Path]::Combine('$(TargetDir)', '$(_ExportDocOnnxRuntimeFile)')", apiProject, StringComparison.Ordinal);
            Assert.Contains("EXPORTDOCMANAGER_OCR_RUNTIME=enabled", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("--verify-ocr-runtime", dockerfile, StringComparison.Ordinal);
            Assert.Contains("test -s /app/libonnxruntime.so", dockerfile, StringComparison.Ordinal);
            Assert.Contains("OcrModels/PaddleOCR/V6/det/inference.onnx", dockerfile, StringComparison.Ordinal);
            Assert.Contains("OcrModels/PaddleOCR/V6/rec/inference.onnx", dockerfile, StringComparison.Ordinal);
            Assert.Contains("apps/exportdoc-ocr-rs/Cargo.toml", workflow, StringComparison.Ordinal);
            Assert.Contains("sidecar/ocr", workflow, StringComparison.Ordinal);
            Assert.Contains("libonnxruntime.so", workflow, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", workflow, StringComparison.Ordinal);
            Assert.Contains("exec bash ./verify-api-ocr-runtime.sh", containerWorkflow, StringComparison.Ordinal);
            Assert.Contains("--network none", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("--read-only", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("docker rm --force", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("--urls http://127.0.0.1:5188", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("--network-mode false", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", containerOcrVerifier, StringComparison.Ordinal);
            Assert.Contains("--verify-ocr-runtime", startScript, StringComparison.Ordinal);
            Assert.True(File.Exists(notices));
            Assert.Contains("Apache License 2.0", File.ReadAllText(notices), StringComparison.Ordinal);
            Assert.Contains("MIT License", File.ReadAllText(notices), StringComparison.Ordinal);
            Assert.DoesNotContain("opencv", rustManifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FROM rust:", dockerfile, StringComparison.Ordinal);
            Assert.Contains("sidecar/ocr/exportdoc-ocr", dockerfile, StringComparison.Ordinal);
            Assert.Contains("exportdoc-runtime-id", dockerfile, StringComparison.Ordinal);
            Assert.Contains("amd64) echo linux-x64", dockerfile, StringComparison.Ordinal);
            Assert.Contains("arm64) echo linux-arm64", dockerfile, StringComparison.Ordinal);
            Assert.Contains("--runtime \"$runtime_identifier\"", dockerfile, StringComparison.Ordinal);
            Assert.Contains("tools/excel-analyzer-rs/Cargo.toml", workflow, StringComparison.Ordinal);
            Assert.Contains("Tools/exportdoc-excel-analyzer", dockerfile, StringComparison.Ordinal);
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
