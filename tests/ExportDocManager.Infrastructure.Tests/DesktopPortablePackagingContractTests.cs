namespace ExportDocManager.Infrastructure.Tests;

public sealed class DesktopPortablePackagingContractTests
{
    [Fact]
    public void DesktopWorkflow_ShouldPublishInstallerAndPortableArtifactsFromOneVerifiedBuild()
    {
        string root = FindRepositoryRoot();
        string workflow = Read(root, ".github", "workflows", "desktop-package-reusable.yml")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("Build, launch-smoke and verify portable desktop package", workflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/package-desktop-portable.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-ResourceRoot ./artifacts/tauri-bundle/resources", workflow, StringComparison.Ordinal);
        Assert.Contains("Upload desktop installer artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("Upload portable desktop artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("-installer", workflow, StringComparison.Ordinal);
        Assert.Contains("-portable", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/desktop-portable/packages/*", workflow, StringComparison.Ordinal);
        Assert.Contains("-PortableAssetRoot ./artifacts/desktop-portable/packages", workflow, StringComparison.Ordinal);

        int payloadVerification = workflow.IndexOf("Verify lean desktop payload", StringComparison.Ordinal);
        int portablePackaging = workflow.IndexOf("Build, launch-smoke and verify portable desktop package", StringComparison.Ordinal);
        int artifactUpload = workflow.IndexOf("Upload portable desktop artifact", StringComparison.Ordinal);
        Assert.True(payloadVerification >= 0 && portablePackaging > payloadVerification);
        Assert.True(artifactUpload > portablePackaging);
    }

    [Fact]
    public void PortablePackager_ShouldUseNativePortableFormatsAndPreserveAuditedRuntimePayload()
    {
        string root = FindRepositoryRoot();
        string script = Read(root, "scripts", "package-desktop-portable.ps1");

        Assert.Contains("verify-package-payload.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-Profile Desktop", script, StringComparison.Ordinal);
        Assert.Contains("-Edition $Edition", script, StringComparison.Ordinal);
        Assert.Contains("portable-runtime.json", script, StringComparison.Ordinal);
        Assert.Contains("portable-package.json", script, StringComparison.Ordinal);
        Assert.Contains("version.json", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", script, StringComparison.Ordinal);
        Assert.Contains(".zip", script, StringComparison.Ordinal);
        Assert.Contains(".tar.gz", script, StringComparison.Ordinal);
        Assert.Contains("Compress-Archive", script, StringComparison.Ordinal);
        Assert.Contains("\"tar\"", script, StringComparison.Ordinal);
        Assert.Contains("--appimage-extract", script, StringComparison.Ordinal);
        Assert.Contains("Contents/Resources", script, StringComparison.Ordinal);
        Assert.Contains("\"ditto\"", script, StringComparison.Ordinal);
        Assert.Contains("\"chmod\"", script, StringComparison.Ordinal);
        Assert.Contains("systemSigned = $false", script, StringComparison.Ordinal);
        Assert.Contains("notarized = $false", script, StringComparison.Ordinal);
        Assert.Contains("must not contain App_Data", script, StringComparison.Ordinal);
        Assert.Contains("smoke-tauri-desktop.ps1", script, StringComparison.Ordinal);
        Assert.Contains("UsePortableDataRoot = $true", script, StringComparison.Ordinal);
        Assert.Contains("$smokeArguments.UseDefaultAppRoot = $true", script, StringComparison.Ordinal);
        Assert.Contains("PortableRoot = $portableRoot", script, StringComparison.Ordinal);
        Assert.Contains("$launchExecutablePath = Join-Path (Join-Path $inspectionRoot \"squashfs-root\") \"AppRun\"", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-MacOsBundleExecutable", script, StringComparison.Ordinal);
        Assert.Contains("bundledReportBrowser = [bool]$editionMetadata.resourceProfile.browserRenderer", script, StringComparison.Ordinal);
        Assert.Contains("Portable launch smoke data cleanup", script, StringComparison.Ordinal);
        Assert.DoesNotContain("signtool", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codesign", script, StringComparison.OrdinalIgnoreCase);

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-package-reusable.yml"));
        Assert.Contains("publish:", workflow, StringComparison.Ordinal);
        Assert.Contains("permissions:\n      contents: read", workflow.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("permissions:\n      actions: read\n      contents: write", workflow.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("needs.package.outputs.version", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("permissions:\n  contents: write", workflow.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("notarytool", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortableRuntime_ShouldKeepResourcesInsideNativeBundlesAndDataBesideThePackage()
    {
        string root = FindRepositoryRoot();
        string runtimePaths = Read(
            root,
            "apps",
            "export-doc-tauri",
            "src-tauri",
            "src",
            "runtime_paths.rs");
        string runtimePortable = Read(
            root,
            "apps",
            "export-doc-tauri",
            "src-tauri",
            "src",
            "runtime_portable.rs");

        Assert.Contains("resolve_portable_runtime_root", runtimePaths, StringComparison.Ordinal);
        Assert.Contains("let storage_root = portable_root.as_deref().unwrap_or(&app_root);", runtimePaths, StringComparison.Ordinal);
        Assert.Contains("let data_root = app_root.join(\"App_Data\");", runtimePaths, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_PORTABLE_ROOT", runtimePortable, StringComparison.Ordinal);
        Assert.Contains("env::var_os(\"APPIMAGE\")", runtimePortable, StringComparison.Ordinal);
        Assert.Contains("macos_bundle_parent", runtimePortable, StringComparison.Ordinal);
        Assert.Contains("validate_portable_runtime_marker(&external_root, app_root)?", runtimePortable, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableRuntime_ShouldCheckForUpdatesWithoutLaunchingAnInstaller()
    {
        string root = FindRepositoryRoot();
        string updater = Read(
            root,
            "apps",
            "export-doc-tauri",
            "src-tauri",
            "src",
            "tauri_updater_commands.rs");
        string updatePage = Read(
            root,
            "apps",
            "export-doc-web",
            "src",
            "features",
            "system",
            "UpdateCenterPage.tsx");

        Assert.Contains("ensure_updater_install_supported(app.state::<RuntimePaths>().portable)?", updater, StringComparison.Ordinal);
        Assert.Contains("install_supported: !portable", updater, StringComparison.Ordinal);
        Assert.Contains("绿色便携版不会启动系统安装器", updater, StringComparison.Ordinal);
        Assert.Contains("PORTABLE_UPDATER_STORAGE_POLICY", updater, StringComparison.Ordinal);
        Assert.Contains("Boolean(checkResult?.installSupported)", updatePage, StringComparison.Ordinal);
        Assert.Contains("发现新版便携包", updatePage, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopReleasePublisher_ShouldVerifyAndUploadPortableArchiveWithInstallerAssets()
    {
        string root = FindRepositoryRoot();
        string publisher = Read(root, "scripts", "publish-tauri-updater-manifest.ps1");

        Assert.Contains("[string]$PortableAssetRoot", publisher, StringComparison.Ordinal);
        Assert.Contains("PortableAssetRoot must stay inside", publisher, StringComparison.Ordinal);
        Assert.Contains("$assetBaseName-portable$portableArchiveSuffix", publisher, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $portableArchivePath -Algorithm SHA256", publisher, StringComparison.Ordinal);
        Assert.Contains("Portable release archive SHA-256 mismatch", publisher, StringComparison.Ordinal);
        Assert.Contains("$stagedAssets.Add($stagedPortableAsset)", publisher, StringComparison.Ordinal);
        Assert.Contains("PortableAssets = @($portableAssets.Name)", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPortableMatrix_ShouldKeepMacOsArm64Only()
    {
        string root = FindRepositoryRoot();
        string windows = Read(root, ".github", "workflows", "windows-desktop-package.yml");
        string linux = Read(root, ".github", "workflows", "linux-desktop-package.yml");
        string macos = Read(root, ".github", "workflows", "macos-desktop-package.yml");
        string localBuild = Read(root, "scripts", "run-tauri-local.ps1");

        Assert.Contains("runtime_identifier: win-x64", windows, StringComparison.Ordinal);
        Assert.Contains("rust_target: x86_64-pc-windows-msvc", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("rust_target: x86_64-pc-windows-gnu", windows, StringComparison.Ordinal);
        Assert.Contains("linux-x64", linux, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", linux, StringComparison.Ordinal);
        Assert.Contains("runtime_identifier: osx-arm64", macos, StringComparison.Ordinal);
        Assert.Contains("bundle_targets: app,dmg", macos, StringComparison.Ordinal);
        Assert.DoesNotContain("osx-x64", macos, StringComparison.Ordinal);
        Assert.DoesNotContain("macos-15-intel", macos, StringComparison.Ordinal);
        Assert.Contains("$env:CARGO_BUILD_JOBS = \"1\"", localBuild, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopWorkflowCallers_ShouldGrantTheReusableWorkflowDeclaredPermissions()
    {
        string root = FindRepositoryRoot();
        foreach (string workflowName in new[]
                 {
                     "windows-desktop-package.yml",
                     "linux-desktop-package.yml",
                     "macos-desktop-package.yml"
                 })
        {
            string workflow = Read(root, ".github", "workflows", workflowName)
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            string packageJob = workflow[..workflow.IndexOf("\n  release:", StringComparison.Ordinal)];

            Assert.Contains(
                "permissions:\n      actions: read\n      contents: write",
                packageJob,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesktopLaunchSmoke_ShouldProbeAnEndpointAllowedByTheProductEdition()
    {
        string root = FindRepositoryRoot();
        string smoke = Read(root, "scripts", "smoke-tauri-desktop.ps1");

        Assert.Contains("Invoke-EditionWorkspacePageProbe", smoke, StringComparison.Ordinal);
        Assert.Contains("$CurrentUser.capabilities.productEdition", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/crm/customers/page?pageNumber=1&pageSize=5", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/invoices?pageNumber=1&pageSize=5", smoke, StringComparison.Ordinal);
        Assert.Contains("WorkspaceProbe = $workspaceProbe.Name", smoke, StringComparison.Ordinal);
        Assert.Contains("WorkspacePageNumber = $workspaceProbe.Page.pageNumber", smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopLaunchSmoke_ShouldCleanOwnedWindowsProcessTreeWithoutRequiringCimAccess()
    {
        string root = FindRepositoryRoot();
        string smoke = Read(root, "scripts", "smoke-tauri-desktop.ps1");

        Assert.Contains("$isWindowsPlatform -and -not $Process.HasExited", smoke, StringComparison.Ordinal);
        Assert.Contains("$Process.Kill($true)", smoke, StringComparison.Ordinal);
        Assert.Contains("function Get-WindowsProcessInventory", smoke, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance Win32_Process -ErrorAction Stop", smoke, StringComparison.Ordinal);
        Assert.Contains("continuing with owned process-tree cleanup", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Process msedgewebview2", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop-Process -Name msedgewebview2", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taskkill /im msedgewebview2", smoke, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
