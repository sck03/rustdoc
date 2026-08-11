using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class PackagePayloadContractTests
{
    [Fact]
    public void PlaywrightChromiumPin_ShouldMatchRestoredPackageMetadata()
    {
        string root = FindWorkspaceRoot();
        string packagePropsPath = Path.Combine(root, "Directory.Packages.props");
        var packageProps = XDocument.Load(packagePropsPath);
        string packageVersion = packageProps.Descendants("PackageVersion")
            .Single(element => string.Equals((string)element.Attribute("Include"), "Microsoft.Playwright", StringComparison.Ordinal))
            .Attribute("Version")?.Value ?? string.Empty;
        string expectedBrowserVersion = packageProps.Descendants("PlaywrightChromiumVersion").Single().Value;
        string expectedRevision = packageProps.Descendants("PlaywrightChromiumRevision").Single().Value;

        string assetsPath = Path.Combine(
            root,
            "tests",
            "ExportDocManager.Infrastructure.Tests",
            "obj",
            "project.assets.json");
        using var assetsDocument = JsonDocument.Parse(File.ReadAllText(assetsPath));
        string packageFolder = assetsDocument.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(property => property.Name)
            .First();
        string libraryKey = $"Microsoft.Playwright/{packageVersion}";
        string relativePackagePath = assetsDocument.RootElement.GetProperty("libraries")
            .GetProperty(libraryKey)
            .GetProperty("path")
            .GetString() ?? string.Empty;
        string browserMetadataPath = Path.Combine(
            packageFolder,
            relativePackagePath.Replace('/', Path.DirectorySeparatorChar),
            ".playwright",
            "package",
            "browsers.json");
        using var browserDocument = JsonDocument.Parse(File.ReadAllText(browserMetadataPath));
        JsonElement chromium = browserDocument.RootElement.GetProperty("browsers")
            .EnumerateArray()
            .Single(browser => browser.GetProperty("name").GetString() == "chromium");

        Assert.Equal(expectedBrowserVersion, chromium.GetProperty("browserVersion").GetString());
        Assert.Equal(expectedRevision, chromium.GetProperty("revision").GetString());
    }

    [Fact]
    public void ReleasePipelines_ShouldUseProfilesAndRejectDuplicateHeavyPayloads()
    {
        string root = FindWorkspaceRoot();
        string apiProject = File.ReadAllText(Path.Combine(root, "src", "ExportDocManager.Api", "ExportDocManager.Api.csproj"));
        string infrastructureProject = File.ReadAllText(Path.Combine(root, "src", "ExportDocManager.Infrastructure", "ExportDocManager.Infrastructure.csproj"));
        string desktopScript = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));
        string serverWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "browser-server-package-reusable.yml"));
        string dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.api"));
        string browserDockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.browser"));
        string webDockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.web"));
        string compose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.yml"));
        string ghcrCompose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.ghcr.yml"));
        string containerRuntimeWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "container-runtime-validation.yml"));
        string containerImageWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "container-images.yml"));
        string verifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-package-payload.ps1"));

        Assert.Contains("ExportDocPackageProfile=Desktop", desktopScript, StringComparison.Ordinal);
        Assert.Contains("ExportDocPackageProfile=Server", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("initialize-windows.ps1", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("initialize-linux.sh", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("deploy/browser-server/README.md", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("setup-windows.cmd", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("version.json", serverWorkflow, StringComparison.Ordinal);
        Assert.Contains("ExportDocPackageProfile=Container", dockerfile, StringComparison.Ordinal);
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM debian:trixie-slim AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("        chromium \\", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("        chromium-sandbox \\", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM debian:trixie-slim", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("        chromium-sandbox \\", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("        socat \\", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("test -u /usr/lib/chromium/chrome-sandbox", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("start-browser-runtime.sh", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-port=9223", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("USER 10001:10001", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dpkg -i /tmp/packages-microsoft-prod.deb", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dpkg --purge packages-microsoft-prod", dockerfile, StringComparison.Ordinal);
        Assert.Contains("aspnetcore-runtime-10.0 \\", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet-runtime-10.0 \\", dockerfile, StringComparison.Ordinal);
        Assert.Contains("test \"$aspnet_version\" = \"$netcore_version\"", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("aspnetcore-runtime-10.0=", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-runtime-10.0=", dockerfile, StringComparison.Ordinal);
        Assert.Contains("https://apt.postgresql.org/pub/repos/apt trixie-pgdg main", dockerfile, StringComparison.Ordinal);
        Assert.Contains("postgresql-client-18", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY --from=dotnet-aspnet", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet/sdk:10.0-bookworm-slim", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet/aspnet:10.0-bookworm-slim", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("bookworm-pgdg", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("libicu72", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("        gnupg", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("https://packages.microsoft.com/keys/microsoft.asc", dockerfile, StringComparison.Ordinal);
        Assert.Contains("signed-by=/usr/share/keyrings/postgresql.asc", dockerfile, StringComparison.Ordinal);
        Assert.Contains("node:24-trixie-slim", dockerfile, StringComparison.Ordinal);
        Assert.Contains("rust:1.96-trixie", dockerfile, StringComparison.Ordinal);
        Assert.Contains("node:24-trixie-slim", webDockerfile, StringComparison.Ordinal);
        Assert.Contains("nginx:1.30.4-alpine3.24", webDockerfile, StringComparison.Ordinal);
        Assert.Contains("USER nginx", webDockerfile, StringComparison.Ordinal);
        Assert.Contains("postgres:18.4-trixie", compose, StringComparison.Ordinal);
        Assert.Contains("postgres:18.4-trixie", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("postgres:18.4-trixie", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--remote-debugging-port=0", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("nginx:1.30.4-alpine3.24", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("sha-${GITHUB_SHA}", containerImageWorkflow, StringComparison.Ordinal);
        Assert.Contains("org.opencontainers.image.revision=${{ github.sha }}", containerImageWorkflow, StringComparison.Ordinal);
        Assert.Contains("promote:", containerImageWorkflow, StringComparison.Ordinal);
        Assert.Contains("Create immutable release manifest", containerImageWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("type=raw,value=${{ steps.version.outputs.value }}", containerImageWorkflow, StringComparison.Ordinal);
        Assert.Contains("sudo chown \"$(id -u):101\"", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("deploy/container/secrets/tls/server.key", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("chmod 0640 \\", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("nginx-main.conf", File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.web")), StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);
        Assert.Contains("ReadonlyRootfs", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("{{.Config.User}}", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("/livez", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("Validate isolated Chromium PDF runtime and capability boundary", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("--print-to-pdf", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("sudo test -d \"$installer_root/runtime/api-data/Cache/ReportPdf\"", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("sudo test -d \"$installer_root/runtime/browser\"", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("if [ ! -f .env ]; then", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("--env-file .env", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_BROWSER_CDP_ENDPOINT: http://browser:9222", compose, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_BROWSER_CDP_ENDPOINT: http://browser:9222", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("docker compose exec -T api sh -ec 'test ! -e /usr/bin/chromium'", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("\"global.json\"", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.Contains("\"Resources/**\"", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres:18-bookworm", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres:18-bookworm", ghcrCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres:18-bookworm", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx:1.27-alpine", webDockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx:1.27-alpine", containerRuntimeWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx:1.30.4-trixie", webDockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx:1.30.4-trixie", containerRuntimeWorkflow, StringComparison.Ordinal);
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
        Assert.Contains("setup-windows.cmd", verifier, StringComparison.Ordinal);
        Assert.Contains("README.md", verifier, StringComparison.Ordinal);
        Assert.Contains("version.json", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossPlatformTypographyWorkflow_ShouldComparePdfLineWrappingAndRejectTextOverlap()
    {
        string root = FindWorkspaceRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "cross-platform-typography.yml"));
        string extractor = File.ReadAllText(Path.Combine(root, "scripts", "extract-report-pdf-layout.py"));
        string comparer = File.ReadAllText(Path.Combine(root, "scripts", "compare-report-pdf-metrics.mjs"));

        Assert.Contains(
            "actions/setup-python@ece7cb06caefa5fff74198d8649806c4678c61a1 # v6",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("extract-report-pdf-layout.py", workflow, StringComparison.Ordinal);
        Assert.Contains("*.layout.json", workflow, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_BROWSER_AUTOMATION_RECYCLE_USES", workflow, StringComparison.Ordinal);
        Assert.Contains("--blame-hang-timeout 4m", workflow, StringComparison.Ordinal);
        Assert.Contains("--filter \"FullyQualifiedName=$test\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FullyQualifiedName~RenderBuiltInProgramTemplatesToPdf|FullyQualifiedName~RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("find_text_overlaps", extractor, StringComparison.Ordinal);
        Assert.Contains("lineHashes", extractor, StringComparison.Ordinal);
        Assert.Contains("wrappingConsistent", comparer, StringComparison.Ordinal);
        Assert.Contains("maximumLineTopSpread", comparer, StringComparison.Ordinal);
        Assert.Contains("at least one platform contains overlapping PDF text", comparer, StringComparison.Ordinal);
        Assert.Contains("equivalent text lines move vertically by more than 2.5pt across platforms", comparer, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopBundle_ShouldUseCompatibleRustAndOnnxRuntimeAcrossDeclaredTargets()
    {
        string root = FindWorkspaceRoot();
        string bundleScript = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));
        string desktopWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-package-reusable.yml"));
        string crossPlatformWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "cross-platform-validation.yml"));
        string macOsWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "macos-desktop-package.yml"));
        string typographyWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "cross-platform-typography.yml"));
        string chromeProvisioning = File.ReadAllText(Path.Combine(root, "scripts", "provision-chrome-for-testing.ps1"));
        string packageVersions = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        string ocrManifest = File.ReadAllText(Path.Combine(root, "apps", "exportdoc-ocr-rs", "Cargo.toml"));

        Assert.Contains("resolveRustTargetTriple(rid)", bundleScript, StringComparison.Ordinal);
        Assert.Contains("resolveLocalBuildPath(\"CARGO_TARGET_DIR\", \"cargo-target-tauri\")", bundleScript, StringComparison.Ordinal);
        Assert.Contains("x86_64-pc-windows-gnu", bundleScript, StringComparison.Ordinal);
        Assert.Contains("x86_64-pc-windows-msvc", bundleScript, StringComparison.Ordinal);
        Assert.Contains("const target = rustTarget;", bundleScript, StringComparison.Ordinal);
        Assert.DoesNotContain("ensureMacOsX64OnnxRuntime", bundleScript, StringComparison.Ordinal);
        Assert.DoesNotContain("onnxruntime-osx-x86_64", bundleScript, StringComparison.Ordinal);
        Assert.DoesNotContain("osx-x64", bundleScript, StringComparison.Ordinal);
        Assert.Contains("Microsoft.ML.OnnxRuntime\" Version=\"1.28.0\"", packageVersions, StringComparison.Ordinal);
        Assert.Contains("\"api-23\"", ocrManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"api-24\"", ocrManifest, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_RUST_TARGET: ${{ inputs.rust_target }}", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("rust_target: x86_64-pc-windows-msvc", crossPlatformWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("msys2/setup-msys2", crossPlatformWorkflow, StringComparison.Ordinal);
        Assert.Contains("cargo test --manifest-path apps/export-doc-tauri/src-tauri/Cargo.toml --target ${{ matrix.rust_target }}", crossPlatformWorkflow, StringComparison.Ordinal);
        Assert.Contains("runtime_identifier: osx-arm64", macOsWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("osx-x64", macOsWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("macos-15-intel", macOsWorkflow, StringComparison.Ordinal);
        Assert.Contains("os: macos-15", typographyWorkflow, StringComparison.Ordinal);
        Assert.Contains("chrome_platform: mac-arm64", typographyWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("mac-x64", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("only supports Apple Silicon ARM64", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("PlaywrightChromiumVersion", packageVersions, StringComparison.Ordinal);
        Assert.Contains("Get-PlaywrightRuntimeMetadata", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("PlaywrightCompatible", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("playwrightCompatible", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("Assert-NoRunningBrowserFromRoot", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("Test-ChromeExecutable", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("FileVersionInfo", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("$Product -eq \"ChromeHeadlessShell\" -and", chromeProvisioning, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 10", typographyWorkflow, StringComparison.Ordinal);
        Assert.Contains("--logger \"console;verbosity=normal\"", typographyWorkflow, StringComparison.Ordinal);
        Assert.Contains("scripts/test_report_template_pdf_regression.mjs", typographyWorkflow, StringComparison.Ordinal);
        Assert.Contains("scripts/test_report_template_print_pixel_regression.mjs", typographyWorkflow, StringComparison.Ordinal);
        Assert.Contains("scripts/test_report_template_visual_regression.mjs", typographyWorkflow, StringComparison.Ordinal);
        string reportRegressionCommon = File.ReadAllText(Path.Combine(root, "scripts", "lib", "report-regression-common.mjs"));
        Assert.Contains("NotoSansCJKsc-Regular.otf", reportRegressionCommon, StringComparison.Ordinal);
        Assert.Contains("NotoSerifCJKsc-Regular.otf", reportRegressionCommon, StringComparison.Ordinal);
        Assert.Contains("@font-face", reportRegressionCommon, StringComparison.Ordinal);
        Assert.Contains("font-display: block", reportRegressionCommon, StringComparison.Ordinal);
        string pdfPixelRegression = File.ReadAllText(Path.Combine(root, "scripts", "test_report_template_pdf_pixel_regression.mjs"));
        Assert.Contains("stageReportHtmlWithBundledFonts", pdfPixelRegression, StringComparison.Ordinal);
        Assert.Contains("--allow-file-access-from-files", pdfPixelRegression, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopBundle_ShouldRestoreCompleteSolutionBeforeDependencyGovernance()
    {
        string root = FindWorkspaceRoot();
        string bundleScript = File.ReadAllText(Path.Combine(root, "scripts", "prepare-tauri-bundle.mjs"));
        const string solutionRestore = "run(\"dotnet\", [\"restore\", path.join(repoRoot, \"ExportDocManager.sln\")], buildEnv);";
        const string governanceGenerator = "path.join(repoRoot, \"scripts\", \"generate-dependency-governance.mjs\")";

        int restoreIndex = bundleScript.IndexOf(solutionRestore, StringComparison.Ordinal);
        int governanceIndex = bundleScript.IndexOf(governanceGenerator, StringComparison.Ordinal);

        Assert.True(restoreIndex >= 0, "Desktop bundle preparation must restore the complete solution.");
        Assert.True(
            governanceIndex > restoreIndex,
            "The complete solution restore must run before dependency governance scans every project.");
    }

    [Fact]
    public void TauriBuildWrapper_ShouldLaunchLocalCliWithoutWindowsCommandShim()
    {
        string root = FindWorkspaceRoot();
        string buildWrapper = File.ReadAllText(Path.Combine(root, "scripts", "run-tauri-build.mjs"));

        Assert.Contains(
            "path.join(tauriRoot, \"node_modules\", \"@tauri-apps\", \"cli\", \"tauri.js\")",
            buildWrapper,
            StringComparison.Ordinal);
        Assert.Contains("spawnSync(process.execPath, [tauriCliPath, \"build\"", buildWrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("npm.cmd", buildWrapper, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContainerInstaller_ShouldKeepNginxAsTheOnlyBusinessGateway()
    {
        string root = FindWorkspaceRoot();
        string installer = File.ReadAllText(Path.Combine(root, "deploy", "container", "install-container.sh"));
        string initializer = File.ReadAllText(Path.Combine(root, "deploy", "container", "initialize-container-runtime.ps1"));
        string baseCompose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.ghcr.yml"));
        string acmeCompose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.acme.yml"));
        string nginxConfig = File.ReadAllText(Path.Combine(root, "deploy", "container", "nginx.acme.conf"));

        Assert.Contains("set -Eeuo pipefail", installer, StringComparison.Ordinal);
        Assert.Contains("--revision SHA", installer, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_IMAGE_REVISION", installer, StringComparison.Ordinal);
        Assert.Contains("org.opencontainers.image.revision", installer, StringComparison.Ordinal);
        Assert.Contains("select_available_subnet", installer, StringComparison.Ordinal);
        Assert.Contains("docker-compose.ghcr.yml", installer, StringComparison.Ordinal);
        Assert.Contains("docker-compose.acme.yml", installer, StringComparison.Ordinal);
        Assert.Contains("certbot/certbot:v5.7.0", installer, StringComparison.Ordinal);
        Assert.Contains("--standalone", installer, StringComparison.Ordinal);
        Assert.Contains("-checkend 2592000", installer, StringComparison.Ordinal);
        Assert.Contains("restore_previous_deployment", installer, StringComparison.Ordinal);
        Assert.Contains("restore_deployment_assets", installer, StringComparison.Ordinal);
        Assert.Contains("restore_certificate_state", installer, StringComparison.Ordinal);
        Assert.Contains("up -d --remove-orphans --force-recreate", installer, StringComparison.Ordinal);
        Assert.Contains(".letsencrypt.previous.", installer, StringComparison.Ordinal);
        Assert.Contains(".letsencrypt.failed.XXXXXX", installer, StringComparison.Ordinal);
        Assert.DoesNotContain(".letsencrypt.failed.$$", installer, StringComparison.Ordinal);
        Assert.Contains(".deployment-assets.stage.", installer, StringComparison.Ordinal);
        Assert.Contains("bash -n \"$ASSET_STAGE/install-container.sh\"", installer, StringComparison.Ordinal);
        Assert.Contains("$ASSET_STAGE/.compose-validation.env", installer, StringComparison.Ordinal);
        Assert.Contains("-f \"$ASSET_STAGE/docker-compose.acme.yml\"", installer, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK_REQUIRED", installer, StringComparison.Ordinal);
        Assert.Contains("ENVIRON[\"ENV_VALUE\"]", installer, StringComparison.Ordinal);
        Assert.Contains("assert_safe_directory_path", installer, StringComparison.Ordinal);
        Assert.Contains("INSTALL_DIR=$(cd -- \"$INSTALL_DIR\" && pwd -P)", installer, StringComparison.Ordinal);
        Assert.Contains("RUNTIME_ROOT=$(cd -- \"$RUNTIME_ROOT\" && pwd -P)", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 700 \"$INSTALL_DIR\"", installer, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY true", installer, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY false", installer, StringComparison.Ordinal);

        int assetBackupIndex = installer.IndexOf(
            "cp -p -- \"$INSTALL_DIR/$asset\" \"$ASSET_BACKUP/$asset\"",
            StringComparison.Ordinal);
        int stagedComposeValidationIndex = installer.IndexOf(
            "-f \"$ASSET_STAGE/docker-compose.acme.yml\"",
            StringComparison.Ordinal);
        int environmentBackupIndex = installer.IndexOf(
            "ENVIRONMENT_BACKUP=$(mktemp \"$INSTALL_DIR/.env.previous.XXXXXX\")",
            StringComparison.Ordinal);
        int rollbackArmedIndex = installer.IndexOf("ROLLBACK_REQUIRED=1", StringComparison.Ordinal);
        int activationIndex = installer.IndexOf(
            "ACTIVATION_FILE=$(mktemp \"$INSTALL_DIR/.$asset.activate.XXXXXX\")",
            StringComparison.Ordinal);
        Assert.True(assetBackupIndex >= 0, "Deployment assets must be backed up before activation.");
        Assert.True(stagedComposeValidationIndex >= 0, "Both staged Compose modes must be validated before activation.");
        Assert.True(assetBackupIndex > stagedComposeValidationIndex, "Staged Compose validation must finish before existing assets are backed up or replaced.");
        Assert.True(environmentBackupIndex >= 0, "The previous environment must be backed up before activation.");
        Assert.True(rollbackArmedIndex > assetBackupIndex, "Rollback must be armed after deployment assets are backed up.");
        Assert.True(rollbackArmedIndex > environmentBackupIndex, "Rollback must be armed after the environment is backed up.");
        Assert.True(activationIndex > rollbackArmedIndex, "Rollback must be armed before the first deployment asset is activated.");
        Assert.Contains("ENVIRONMENT_TEMP_FILE=$(mktemp \"$INSTALL_DIR/.env.tmp.XXXXXX\")", installer, StringComparison.Ordinal);
        Assert.Contains("Installer lock must not be a symbolic link", installer, StringComparison.Ordinal);
        Assert.Contains("Activation marker must not be a symbolic link", installer, StringComparison.Ordinal);

        int certificateBackupIndex = installer.IndexOf(
            "CERTIFICATE_BACKUP=$(mktemp -d \"$RUNTIME_ROOT/.letsencrypt.previous.XXXXXX\")",
            StringComparison.Ordinal);
        int certificateRequestIndex = installer.IndexOf(
            "docker run --rm --name exportdocmanager-certbot-bootstrap",
            StringComparison.Ordinal);
        Assert.True(certificateBackupIndex >= 0, "HTTPS certificate state must be backed up before replacement.");
        Assert.True(certificateRequestIndex > certificateBackupIndex, "The certificate backup must complete before Certbot changes the active lineage.");

        Assert.Contains("chown -R 10001:10001 \"$API_DATA_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("chown -R 999:999 \"$POSTGRES_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("chown root:101 \"$LETSENCRYPT_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 0700 \"$POSTGRES_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 0750 \"$API_DATA_ROOT\" \"$CONFIG_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 0750 \"$LETSENCRYPT_ROOT\"", installer, StringComparison.Ordinal);
        Assert.Contains("prepare_nginx_certificate_permissions", installer, StringComparison.Ordinal);
        Assert.Contains("chown root:101 \"$certificate_path\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 0640 \"$certificate_path\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 0600 \"$SETTINGS_FILE\"", installer, StringComparison.Ordinal);
        Assert.Contains("Assert-SafeDirectoryPath", initializer, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("[System.IO.UnixFileMode]::GroupWrite", initializer, StringComparison.Ordinal);
        Assert.DoesNotContain("https://get.docker.com", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("latest tag is not accepted", installer, StringComparison.Ordinal);
        Assert.Contains("logs --no-color --tail=120", installer, StringComparison.Ordinal);
        Assert.Contains("config --quiet", installer, StringComparison.Ordinal);
        Assert.Contains("pull", installer, StringComparison.Ordinal);
        Assert.Contains("up -d --remove-orphans", installer, StringComparison.Ordinal);
        Assert.Contains("/readyz", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("--build", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("docker compose down", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("caddy", installer, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("export-doc-manager-api", baseCompose, StringComparison.Ordinal);
        Assert.Contains("export-doc-manager-web", baseCompose, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY", baseCompose, StringComparison.Ordinal);
        Assert.Contains("expose:", baseCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("5188:5188", baseCompose, StringComparison.Ordinal);
        Assert.Contains("certbot/certbot:v5.7.0", acmeCompose, StringComparison.Ordinal);
        Assert.Contains("renew --webroot", acmeCompose, StringComparison.Ordinal);
        Assert.Contains("grant_web_certificate_access", acmeCompose, StringComparison.Ordinal);
        Assert.Contains("chown 0:101 \"$$certificate_file\"", acmeCompose, StringComparison.Ordinal);
        Assert.Contains("chmod 0640 \"$$certificate_file\"", acmeCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("proxy_pass", acmeCompose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("listen 8443 ssl", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("proxy_pass http://api:5188", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("/.well-known/acme-challenge/", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("Strict-Transport-Security", nginxConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerNginxConfigs_ShouldStreamLargeMigrationUploadsAndJobDownloads()
    {
        string root = FindWorkspaceRoot();
        foreach (string fileName in new[] { "nginx.conf", "nginx.https.conf", "nginx.acme.conf" })
        {
            string config = File.ReadAllText(Path.Combine(root, "deploy", "container", fileName));
            Assert.Contains(
                "server-migration/restore|postgresql-maintenance/backups/(restore|upload-restore)",
                config,
                StringComparison.Ordinal);
            Assert.Contains("client_max_body_size 4224m", config, StringComparison.Ordinal);
            Assert.Contains("proxy_request_buffering off", config, StringComparison.Ordinal);
            Assert.Contains("proxy_read_timeout 3600s", config, StringComparison.Ordinal);
            Assert.Contains("proxy_send_timeout 3600s", config, StringComparison.Ordinal);
            Assert.Contains("location /downloads/jobs/", config, StringComparison.Ordinal);
            Assert.Contains("location /downloads/postgresql-backups/", config, StringComparison.Ordinal);
            Assert.Contains("proxy_buffering off", config, StringComparison.Ordinal);
            Assert.Contains("client_max_body_size 128m", config, StringComparison.Ordinal);
            int apiLocationStart = config.IndexOf("location /api/ {", StringComparison.Ordinal);
            int healthLocationStart = config.IndexOf("location = /healthz", apiLocationStart, StringComparison.Ordinal);
            Assert.True(apiLocationStart >= 0 && healthLocationStart > apiLocationStart);
            Assert.Contains(
                "proxy_request_buffering off",
                config[apiLocationStart..healthLocationStart],
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContainerNginxConfigs_ShouldProxyPublicHealthProbesToApi()
    {
        string root = FindWorkspaceRoot();
        foreach (string fileName in new[] { "nginx.conf", "nginx.https.conf", "nginx.acme.conf" })
        {
            string config = File.ReadAllText(Path.Combine(root, "deploy", "container", fileName));
            Assert.Contains("location = /healthz", config, StringComparison.Ordinal);
            Assert.Contains("proxy_pass http://api:5188/healthz", config, StringComparison.Ordinal);
            Assert.Contains("location = /readyz", config, StringComparison.Ordinal);
            Assert.Contains("proxy_pass http://api:5188/readyz", config, StringComparison.Ordinal);
            Assert.Contains("location = /livez", config, StringComparison.Ordinal);
            Assert.Contains("proxy_pass http://api:5188/livez", config, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContainerNginxConfigs_ShouldPreserveSecurityHeadersInsideLocations()
    {
        string root = FindWorkspaceRoot();
        foreach (string fileName in new[] { "nginx.conf", "nginx.https.conf", "nginx.acme.conf" })
        {
            string config = File.ReadAllText(Path.Combine(root, "deploy", "container", fileName));
            System.Text.RegularExpressions.MatchCollection locations =
                System.Text.RegularExpressions.Regex.Matches(
                    config,
                    @"location\s+[^{]+\{(?<body>[^{}]*)\}",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

            Assert.True(locations.Count > 0, $"No location blocks were found in {fileName}.");
            foreach (System.Text.RegularExpressions.Match location in locations)
            {
                Assert.DoesNotContain(
                    "add_header ",
                    location.Groups["body"].Value,
                    StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains("expires 1y", config, StringComparison.Ordinal);
            Assert.Contains("expires -1", config, StringComparison.Ordinal);
            Assert.DoesNotContain("add_header Cache-Control", config, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BrowserServerScripts_ShouldUseRuntimeDataRootAndSafeInteractiveDefaults()
    {
        string root = FindWorkspaceRoot();
        string windowsInitializer = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "initialize-windows.ps1"));
        string windowsStarter = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "start-windows.ps1"));
        string windowsSetupLauncher = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "setup-windows.cmd"));
        string windowsStartLauncher = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "start-windows.cmd"));
        string linuxInitializer = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "initialize-linux.sh"));
        string linuxStarter = File.ReadAllText(Path.Combine(root, "deploy", "browser-server", "start-linux.sh"));

        Assert.Contains("Read-Host $Prompt -AsSecureString", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("RandomNumberGenerator", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("AllowHttpDisasterRecovery", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("$($currentIdentity):(M)", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("$($currentIdentity):(OI)(CI)(M)", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", windowsStarter, StringComparison.Ordinal);
        Assert.Contains("Directory]::GetParent", windowsInitializer, StringComparison.Ordinal);
        Assert.Contains("Directory]::GetParent", windowsStarter, StringComparison.Ordinal);
        Assert.Contains("PSVersionTable.PSVersion.Major -lt 7", windowsSetupLauncher, StringComparison.Ordinal);
        Assert.Contains("PSVersionTable.PSVersion.Major -lt 7", windowsStartLauncher, StringComparison.Ordinal);
        Assert.Contains("read_secret_from_tty", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("generate_bootstrap_token", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("contains_control_character", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("mktemp \"$CONFIG_FILE.tmp.XXXXXX\"", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("--allow-http-disaster-recovery", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("数据根不能", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("assert_safe_directory_path", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("chmod 700 \"$DATA_ROOT\" \"$SECURITY_ROOT\" \"$CONFIG_ROOT\"", linuxInitializer, StringComparison.Ordinal);
        Assert.Contains("assert_safe_directory_path", linuxStarter, StringComparison.Ordinal);
        Assert.Contains("contains_control_character", linuxStarter, StringComparison.Ordinal);
        Assert.Contains("RuntimeVerification", linuxStarter, StringComparison.Ordinal);
        Assert.Contains("version.json", linuxStarter, StringComparison.Ordinal);
        Assert.Contains("ocr-${PACKAGE_VERSION}-${RUNTIME_ARCH}.ok", linuxStarter, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerBrowser_ShouldIsolateChromiumSandboxFromDatabaseCredentials()
    {
        string root = FindWorkspaceRoot();
        string apiDockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.api"));
        string browserDockerfile = File.ReadAllText(Path.Combine(root, "deploy", "container", "Dockerfile.browser"));
        string browserEntrypoint = File.ReadAllText(Path.Combine(root, "deploy", "container", "start-browser-runtime.sh"));
        string localCompose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.yml"));
        string ghcrCompose = File.ReadAllText(Path.Combine(root, "deploy", "container", "docker-compose.ghcr.yml"));

        Assert.Contains("USER 10001:10001", apiDockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("chromium-sandbox", apiDockerfile, StringComparison.Ordinal);
        Assert.Contains("/app/.playwright/node/linux-*/node", apiDockerfile, StringComparison.Ordinal);
        Assert.Contains("chmod 0755", apiDockerfile, StringComparison.Ordinal);
        Assert.Contains("\"$playwright_node\" --version", apiDockerfile, StringComparison.Ordinal);
        Assert.Contains("USER 10001:10001", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("chromium-sandbox", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("socat", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-address=127.0.0.1", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-port=9223", browserDockerfile, StringComparison.Ordinal);
        Assert.Contains("TCP-LISTEN:9222,bind=0.0.0.0,reuseaddr,fork", browserEntrypoint, StringComparison.Ordinal);
        Assert.Contains("TCP:127.0.0.1:9223", browserEntrypoint, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_BROWSER_CDP_ENDPOINT: http://browser:9222", localCompose, StringComparison.Ordinal);
        Assert.Contains("EXPORTDOCMANAGER_BROWSER_CDP_ENDPOINT: http://browser:9222", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("shm_size: \"512mb\"", localCompose, StringComparison.Ordinal);
        Assert.Contains("shm_size: \"512mb\"", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("cap_drop:\n      - ALL", localCompose.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("cap_drop:\n      - ALL", ghcrCompose.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("cap_add:", localCompose, StringComparison.Ordinal);
        Assert.Contains("- SYS_ADMIN", localCompose, StringComparison.Ordinal);
        Assert.Contains("cap_add:", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("- SYS_ADMIN", ghcrCompose, StringComparison.Ordinal);
        Assert.Contains("api-data/Cache/ReportPdf:/runtime-data/Cache/ReportPdf:ro", localCompose, StringComparison.Ordinal);
        Assert.Contains("networks:\n      - browser", localCompose.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("backend:\n    internal: true", localCompose.Replace("\r\n", "\n"), StringComparison.Ordinal);
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
