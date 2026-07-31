using System.Diagnostics;

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
        Assert.Contains("initialize-windows.ps1", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("initialize-linux.sh", serverWorkflow, StringComparison.Ordinal);
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
        Assert.Contains("sidecarExcludedFileNames", desktopScript, StringComparison.Ordinal);
        Assert.Contains("libcoreclrtraceptprovider.so", desktopScript, StringComparison.Ordinal);
        Assert.Contains("sidecarExcludedFileNames.has(entry.name.toLowerCase())", desktopScript, StringComparison.Ordinal);
        Assert.Contains("unavailable liblttng-ust.so.0", verifier, StringComparison.Ordinal);
        Assert.Contains("Expected exactly one shared", verifier, StringComparison.Ordinal);
        Assert.Contains("onnxruntime_providers_shared", verifier, StringComparison.Ordinal);
        Assert.Contains("Browser payload must contain only", verifier, StringComparison.Ordinal);
        Assert.Contains("forbiddenDeveloperUiPayload", verifier, StringComparison.Ordinal);
        Assert.Contains("dashboard|recorder|traceViewer", verifier, StringComparison.Ordinal);
        Assert.Contains("forbiddenPrivateToolPayload", verifier, StringComparison.Ordinal);
        Assert.Contains("private license key generator", verifier, StringComparison.Ordinal);
        Assert.Contains("pending-data-root-migration.json", verifier, StringComparison.Ordinal);
        Assert.Contains("pending-disaster-recovery.json", verifier, StringComparison.Ordinal);
        Assert.Contains("local-master-key.bin", verifier, StringComparison.Ordinal);
        Assert.Contains("license-reactivation-required.json", verifier, StringComparison.Ordinal);
        Assert.Contains("THIRD_PARTY_NOTICES.md", verifier, StringComparison.Ordinal);
        Assert.Contains("exportdocmanager.spdx.json", verifier, StringComparison.Ordinal);
        Assert.Contains("browser payload is missing its upstream license", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requiredServerEntries", verifier, StringComparison.Ordinal);
        Assert.Contains("initialize-windows.ps1", verifier, StringComparison.Ordinal);
        Assert.Contains("initialize-linux.sh", verifier, StringComparison.Ordinal);
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
        Assert.Contains("wrappingConsistent", comparer, StringComparison.Ordinal);
        Assert.Contains("maximumLineTopSpread", comparer, StringComparison.Ordinal);
        Assert.Contains("at least one platform contains overlapping PDF text", comparer, StringComparison.Ordinal);
        Assert.Contains("equivalent text lines move vertically by more than 2.5pt across platforms", comparer, StringComparison.Ordinal);
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

    [Fact]
    public async Task ReleasePayloadVerifier_ShouldRejectRuntimeDatabaseAndSecretFixtures()
    {
        string root = FindWorkspaceRoot();
        string fixtureRoot = Path.Combine(
            root,
            ".codex-runtime",
            "package-payload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Database"));
        File.WriteAllText(Path.Combine(fixtureRoot, "Database", "runtime.db"), "not-a-release-asset");
        File.WriteAllText(Path.Combine(fixtureRoot, "license.dat"), "not-a-release-secret");

        try
        {
            (int exitCode, string output) = await RunPayloadVerifierAsync(root, fixtureRoot, "Desktop", "win-x64");

            Assert.NotEqual(0, exitCode);
            Assert.Contains(
                "Release payload contains runtime data",
                output,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DesktopPayloadVerifier_ShouldRejectLegacyCoreClrLttngProvider()
    {
        string root = FindWorkspaceRoot();
        string fixtureRoot = Path.Combine(
            root,
            ".codex-runtime",
            "package-payload-tests",
            Guid.NewGuid().ToString("N"));
        string sidecarRoot = Path.Combine(fixtureRoot, "sidecar");
        Directory.CreateDirectory(sidecarRoot);
        File.WriteAllText(Path.Combine(sidecarRoot, "libcoreclrtraceptprovider.so"), "optional-lttng-provider");

        try
        {
            (int exitCode, string output) = await RunPayloadVerifierAsync(root, fixtureRoot, "Desktop", "linux-arm64");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("unavailable liblttng-ust.so.0", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunPayloadVerifierAsync(
        string root,
        string packageRoot,
        string profile,
        string runtimeIdentifier)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(root, "scripts", "verify-package-payload.ps1"));
        process.StartInfo.ArgumentList.Add("-PackageRoot");
        process.StartInfo.ArgumentList.Add(packageRoot);
        process.StartInfo.ArgumentList.Add("-Profile");
        process.StartInfo.ArgumentList.Add(profile);
        process.StartInfo.ArgumentList.Add("-RuntimeIdentifier");
        process.StartInfo.ArgumentList.Add(runtimeIdentifier);

        Assert.True(process.Start());
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Package payload verifier timed out.");
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        return (process.ExitCode, standardOutput + standardError);
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
