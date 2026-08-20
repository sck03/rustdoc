namespace ExportDocManager.Infrastructure.Tests;

public sealed class ChromiumArm64PackagingContractTests
{
    [Fact]
    public void LinuxWorkflows_ShouldBundleClearlyLabelledChromiumArm64AndVerifyPdf()
    {
        string root = FindRepositoryRoot();
        string desktop = File.ReadAllText(Path.Combine(root, ".github", "workflows", "linux-desktop-package.yml"));
        string server = File.ReadAllText(Path.Combine(root, ".github", "workflows", "linux-browser-server-package.yml"));
        string reusableDesktop = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-package-reusable.yml"));
        string reusableServer = File.ReadAllText(Path.Combine(root, ".github", "workflows", "browser-server-package-reusable.yml"));
        string provision = File.ReadAllText(Path.Combine(root, "scripts", "provision-playwright-chromium-arm64.ps1"));
        string bundle = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));

        Assert.Contains("ubuntu-24.04-arm", desktop);
        Assert.Contains("linux-arm64", desktop);
        Assert.Contains("chromium-linux-arm64", desktop);
        Assert.Contains("ubuntu-24.04-arm", server);
        Assert.Contains("Chromium ARM64", provision);
        Assert.Contains("Microsoft.Playwright trusted open-source Chromium build", provision);
        Assert.Contains("Directory.Packages.props", provision);
        Assert.Contains("$playwrightPackageVersion = [string]$playwrightPackageNode.Version", provision, StringComparison.Ordinal);
        Assert.DoesNotContain("$playwrightPackageVersion = \"", provision, StringComparison.Ordinal);
        Assert.Contains("ExportDocManager.Api/ExportDocManager.Api.csproj", provision);
        Assert.Contains("Microsoft.Playwright.dll", provision);
        Assert.Contains("install --with-deps chromium", provision);
        Assert.Contains("Get-ValidatedStagedBrowser", provision);
        Assert.Contains("chromium-arm64.manifest.json", provision);
        Assert.Contains("Assert-ChildPath", provision);
        Assert.Contains("Remove-RepositoryEntry -Path $buildRoot", provision);
        Assert.Contains("Remove-RepositoryEntry -Path $cacheRoot", provision);
        Assert.Contains("Expected exactly one Playwright Chromium ARM64 executable", provision);
        Assert.Contains("Sort-Object FullName", provision);
        Assert.DoesNotContain("Select-Object -First 1", provision);
        Assert.Contains("ChromiumArm64", bundle);
        Assert.Contains("verify-bundled-browser-pdf.ps1", reusableDesktop);
        Assert.Contains("verify-bundled-browser-pdf.ps1", reusableServer);
        Assert.Contains("Validate Linux package on Debian 13", reusableServer);
        Assert.Contains("debian:13-slim", reusableServer);
        Assert.Contains("timeout-minutes: 20", reusableServer);
        Assert.Contains("run_probe", reusableServer);
        Assert.Contains("--no-sandbox", reusableServer);
        Assert.Contains("--user-data-dir=/tmp/exportdoc-browser-profile", reusableServer);
    }

    [Fact]
    public void LinuxDesktopPackaging_ShouldAvoidLegacyLinuxdeployStripAndKeepVerboseDiagnostics()
    {
        string root = FindRepositoryRoot();
        string desktop = File.ReadAllText(Path.Combine(root, ".github", "workflows", "linux-desktop-package.yml"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-package-reusable.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        const string compatibilityStep = """
      - name: Configure Linux AppImage compatibility
        if: inputs.platform == 'linux' && contains(inputs.bundle_targets, 'appimage')
        shell: bash
        run: echo "NO_STRIP=1" >> "$GITHUB_ENV"
""";

        int compatibilityStepIndex = workflow.IndexOf(compatibilityStep, StringComparison.Ordinal);
        int desktopBuildIndex = workflow.IndexOf("      - name: Build unsigned desktop package", StringComparison.Ordinal);
        int verboseBuildCount = workflow.Split(
            "npm --prefix apps/export-doc-tauri run build -- --verbose",
            StringSplitOptions.None).Length - 1;

        Assert.Contains("bundle_targets: deb,appimage", desktop);
        Assert.Contains("sudo apt-get install -y file xdg-utils", workflow);
        Assert.DoesNotContain("libfuse2", workflow);
        Assert.True(compatibilityStepIndex >= 0, "Linux AppImage packaging must disable linuxdeploy's legacy strip pass.");
        Assert.True(
            compatibilityStepIndex < desktopBuildIndex,
            "Linux AppImage compatibility must be configured before Tauri starts bundling.");
        Assert.Equal(2, verboseBuildCount);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
