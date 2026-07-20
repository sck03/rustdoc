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
        Assert.Contains("ExportDocManager.Api/ExportDocManager.Api.csproj", provision);
        Assert.Contains("Microsoft.Playwright.dll", provision);
        Assert.Contains("install --with-deps chromium", provision);
        Assert.Contains("ChromiumArm64", bundle);
        Assert.Contains("verify-bundled-browser-pdf.ps1", reusableDesktop);
        Assert.Contains("verify-bundled-browser-pdf.ps1", reusableServer);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
