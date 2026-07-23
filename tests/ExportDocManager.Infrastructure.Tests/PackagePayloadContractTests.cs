namespace ExportDocManager.Infrastructure.Tests;

public sealed class PackagePayloadContractTests
{
    [Fact]
    public void ReleasePipelines_ShouldUseProfilesAndRejectDuplicateHeavyPayloads()
    {
        string root = FindWorkspaceRoot();
        string apiProject = File.ReadAllText(Path.Combine(root, "src", "ExportDocManager.Api", "ExportDocManager.Api.csproj"));
        string infrastructureProject = File.ReadAllText(Path.Combine(root, "src", "ExportDocManager.Infrastructure", "ExportDocManager.Infrastructure.csproj"));
        string desktopScript = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));
        string serverWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "browser-server-package-reusable.yml"));
        string dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.api"));
        string verifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-package-payload.ps1"));

        Assert.Contains("ExportDocPackageProfile=Desktop", desktopScript, StringComparison.Ordinal);
        Assert.Contains("ExportDocPackageProfile=Server", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("ExportDocPackageProfile=Container", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY Browsers/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("AS report-fonts", dockerfile, StringComparison.Ordinal);
        Assert.Contains("provision-report-fonts.mjs", dockerfile, StringComparison.Ordinal);
        Assert.Contains("verify-font-license-policy.mjs --require-files", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=report-fonts /src/Resources/Fonts/OpenSource/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ExcludeAssets=\"compile;runtime\"", infrastructureProject, StringComparison.Ordinal);
        Assert.Contains("RemoveReleaseNativeDebugSymbols", apiProject, StringComparison.Ordinal);
        Assert.Contains("RemovePlaywrightDeveloperUiPayload", apiProject, StringComparison.Ordinal);
        Assert.Contains(".playwright/package/lib/vite/traceViewer", apiProject, StringComparison.Ordinal);
        Assert.Contains("Expected exactly one shared", verifier, StringComparison.Ordinal);
        Assert.Contains("onnxruntime_providers_shared", verifier, StringComparison.Ordinal);
        Assert.Contains("Browser payload must contain only", verifier, StringComparison.Ordinal);
        Assert.Contains("forbiddenDeveloperUiPayload", verifier, StringComparison.Ordinal);
        Assert.Contains("dashboard|recorder|traceViewer", verifier, StringComparison.Ordinal);
        Assert.Contains("forbiddenPrivateToolPayload", verifier, StringComparison.Ordinal);
        Assert.Contains("private license key generator", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossPlatformTypographyWorkflow_ShouldComparePdfLineWrappingAndRejectTextOverlap()
    {
        string root = FindWorkspaceRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "cross-platform-typography.yml"));
        string extractor = File.ReadAllText(Path.Combine(root, "scripts", "extract-report-pdf-layout.py"));
        string comparer = File.ReadAllText(Path.Combine(root, "scripts", "compare-report-pdf-metrics.mjs"));

        Assert.Contains("actions/setup-python@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("extract-report-pdf-layout.py", workflow, StringComparison.Ordinal);
        Assert.Contains("*.layout.json", workflow, StringComparison.Ordinal);
        Assert.Contains("find_text_overlaps", extractor, StringComparison.Ordinal);
        Assert.Contains("lineHashes", extractor, StringComparison.Ordinal);
        Assert.Contains("line wrapping signatures differ", comparer, StringComparison.Ordinal);
        Assert.Contains("maximumLineTopSpread", comparer, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopBundle_ShouldUseCompatibleLocalRustHostAndExplicitCiTarget()
    {
        string root = FindWorkspaceRoot();
        string bundleScript = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));
        string desktopWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-package-reusable.yml"));

        Assert.Contains("resolveRustTargetTriple(rid)", bundleScript, StringComparison.Ordinal);
        Assert.Contains("resolveLocalBuildPath(\"CARGO_TARGET_DIR\", \"cargo-target-tauri\")", bundleScript, StringComparison.Ordinal);
        Assert.Contains("x86_64-pc-windows-gnu", bundleScript, StringComparison.Ordinal);
        Assert.Contains("x86_64-pc-windows-msvc", bundleScript, StringComparison.Ordinal);
        Assert.Contains("const target = rustTarget;", bundleScript, StringComparison.Ordinal);
        Assert.Contains("const archiveDownload = `${archive}.download`;", bundleScript, StringComparison.Ordinal);
        Assert.Contains("await rm(extracted, { recursive: true, force: true });", bundleScript, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_RUST_TARGET: ${{ inputs.rust_target }}", desktopWorkflow, StringComparison.Ordinal);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("ExportDocManager workspace root was not found.");
    }
}
