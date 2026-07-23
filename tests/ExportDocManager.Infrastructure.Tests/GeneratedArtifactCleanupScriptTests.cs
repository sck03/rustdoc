namespace ExportDocManager.Infrastructure.Tests
{
    public class GeneratedArtifactCleanupScriptTests
    {
        [Fact]
        public void CleanupScript_ShouldUseWorkspaceBoundedLiteralPathDeletion()
        {
            string scriptPath = ResolveWorkspacePath("scripts", "clean-generated-artifacts.ps1");
            string content = File.ReadAllText(scriptPath);

            Assert.Contains("Assert-WorkspaceChildPath", content, StringComparison.Ordinal);
            Assert.Contains("SupportsShouldProcess", content, StringComparison.Ordinal);
            Assert.Contains("Remove-DirectoryWithRetry -Path $target.Path", content, StringComparison.Ordinal);
            Assert.Contains("Remove-Item -LiteralPath $Path -Recurse -Force", content, StringComparison.Ordinal);
            Assert.Contains("$IncludeCodexRuntime -or -not (Test-IsUnderPath", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Remove-Item $", content, StringComparison.Ordinal);
            Assert.DoesNotContain("cmd /c", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CleanupScript_ShouldKeepOptionalBrowserRuntimeAssetsBehindExplicitSwitch()
        {
            string scriptPath = ResolveWorkspacePath("scripts", "clean-generated-artifacts.ps1");
            string content = File.ReadAllText(scriptPath);

            int switchIndex = content.IndexOf("$IncludeLegacyRuntimeAssets", StringComparison.Ordinal);
            int browserIndex = content.IndexOf("Browsers\\ChromeForTesting", StringComparison.Ordinal);

            Assert.True(switchIndex >= 0, "Cleanup script should expose IncludeLegacyRuntimeAssets.");
            Assert.True(browserIndex > switchIndex, "Browser renderer cleanup must stay behind IncludeLegacyRuntimeAssets.");
        }

        [Fact]
        public void CleanupScript_ShouldKeepReleaseOutputsBehindExplicitSwitch()
        {
            string content = File.ReadAllText(ResolveWorkspacePath("scripts", "clean-generated-artifacts.ps1"));
            int switchIndex = content.IndexOf("$IncludeReleaseOutputs", StringComparison.Ordinal);
            int conditionalIndex = content.IndexOf("if ($IncludeReleaseOutputs)", StringComparison.Ordinal);
            int portableOutputIndex = content.IndexOf("windows-desktop-run", conditionalIndex, StringComparison.Ordinal);
            int installerOutputIndex = content.IndexOf("windows-installers", conditionalIndex, StringComparison.Ordinal);

            Assert.True(switchIndex >= 0, "Cleanup script should expose IncludeReleaseOutputs.");
            Assert.True(conditionalIndex > switchIndex, "Release output cleanup must use an explicit condition.");
            Assert.True(portableOutputIndex > conditionalIndex, "Portable release output must stay behind IncludeReleaseOutputs.");
            Assert.True(installerOutputIndex > conditionalIndex, "Installer output must stay behind IncludeReleaseOutputs.");
            Assert.DoesNotContain("Add-Target -Targets $targets -Path $artifactsRoot", content, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeAssetDirectories_ShouldDocumentProgramRootPolicy()
        {
            string browserReadmePath = ResolveWorkspacePath("Browsers", "README.md");

            Assert.Contains("program root", File.ReadAllText(browserReadmePath), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WindowsDesktopRunScripts_ShouldKeepLicenseKeyGeneratorOutsideCustomerPackage()
        {
            string buildScriptPath = ResolveWorkspacePath("scripts", "build-windows-desktop-run.ps1");
            string prepareScriptPath = ResolveWorkspacePath("scripts", "prepare-windows-desktop-run.ps1");
            string buildScript = File.ReadAllText(buildScriptPath);
            string prepareScript = File.ReadAllText(prepareScriptPath);

            Assert.Contains("IncludeLicenseKeygen", buildScript, StringComparison.Ordinal);
            Assert.Contains("IncludeLicenseKeygen", prepareScript, StringComparison.Ordinal);
            Assert.Contains("LicenseOutputDir", buildScript, StringComparison.Ordinal);
            Assert.Contains("LicenseOutputDir", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path (Split-Path -Parent $resolvedOutputDir) \"KEY\"", buildScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path (Split-Path -Parent $resolvedOutputDir) \"KEY\"", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Assert-Outside -Path $resolvedLicenseOutputDir -Root $resolvedOutputDir -Purpose \"License key generator output\"", prepareScript, StringComparison.Ordinal);
            Assert.Contains("must stay outside", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Remove-GeneratedEntry -Path (Join-Path $toolsDir \"ExportDocLicenseKeyGen.exe\")", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Copy-RequiredFile -Source $licenseExe -Destination (Join-Path $resolvedLicenseOutputDir \"ExportDocLicenseKeyGen.exe\")", prepareScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Copy-RequiredFile -Source $licenseExe -Destination (Join-Path $toolsDir \"ExportDocLicenseKeyGen.exe\")", prepareScript, StringComparison.Ordinal);
            Assert.DoesNotContain("[switch]$SkipLicenseBuild", buildScript, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsDesktopRunScripts_ShouldCleanRuntimeDataWithoutASeparateVerifierGate()
        {
            string prepareScript = File.ReadAllText(ResolveWorkspacePath("scripts", "prepare-windows-desktop-run.ps1"));
            string verifyScript = File.ReadAllText(ResolveWorkspacePath("scripts", "verify-windows-editions.ps1"));
            string buildScript = File.ReadAllText(ResolveWorkspacePath("scripts", "build-windows-desktop-run.ps1"));

            Assert.Contains("foreach ($runtimeEntryName in @(\"App_Data\", \"logs\"))", prepareScript, StringComparison.Ordinal);
            Assert.Contains("-Path (Join-Path $resolvedOutputDir $runtimeEntryName)", prepareScript, StringComparison.Ordinal);
            Assert.Contains("-Root $resolvedOutputDir", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Remove-GeneratedEntry", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Stop-OutputOwnedProcesses -OutputRoot $resolvedOutputDir", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Test-PathInsideRoot -Path ([string]$_.ExecutablePath) -Root $OutputRoot", prepareScript, StringComparison.Ordinal);
            Assert.Contains("$process.Kill($true)", prepareScript, StringComparison.Ordinal);
            Assert.Contains("$maximumAttempts = 8", prepareScript, StringComparison.Ordinal);
            Assert.Contains("RuntimeDataCleanup = \"unconditional\"", buildScript, StringComparison.Ordinal);
            Assert.DoesNotContain("ExistingRuntimeEntries", buildScript, StringComparison.Ordinal);
            Assert.DoesNotContain("RuntimeDataWillBeCleaned", buildScript, StringComparison.Ordinal);
            Assert.DoesNotContain("customer package must not contain runtime entry", verifyScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Keeping the existing file and continuing", prepareScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Could not remove $Purpose because it may be in use", prepareScript, StringComparison.Ordinal);
        }

        [Fact]
        public void PackagingScripts_ShouldReplaceStaleVersionedOutputsWithinWorkspaceBoundaries()
        {
            string prepareScript = File.ReadAllText(ResolveWorkspacePath("scripts", "prepare-windows-desktop-run.ps1"));
            string editionsScript = File.ReadAllText(ResolveWorkspacePath("scripts", "build-windows-editions.ps1"));
            string installerScript = File.ReadAllText(ResolveWorkspacePath("scripts", "build-windows-installers.ps1"));
            string desktopScript = File.ReadAllText(ResolveWorkspacePath("scripts", "build-windows-desktop-run.ps1"));
            string browserPdfScript = File.ReadAllText(ResolveWorkspacePath("scripts", "verify-bundled-browser-pdf.ps1"));

            Assert.Contains("Remove-GeneratedEntry -Path $Destination -Root $OutputRoot -Purpose \"stale packaged browser runtime\"", prepareScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Join-Path $Destination \"Browsers\"", prepareScript, StringComparison.Ordinal);
            Assert.Contains("Remove-GeneratedEntry -Path $resolvedLicenseOutputDir -Root $artifactsRoot", prepareScript, StringComparison.Ordinal);

            Assert.Contains("if (-not $IncludeLicenseKeygen)", editionsScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path $outputRoot \"KEY\"", editionsScript, StringComparison.Ordinal);
            Assert.Contains("Remove-Item -LiteralPath $staleLicenseOutputFullPath -Recurse -Force", editionsScript, StringComparison.Ordinal);

            Assert.Contains("Get-ChildItem -LiteralPath $outputRoot -File -Filter \"ExportDocManager-$edition-*-win-x64-setup.exe\"", installerScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path $outputRoot \"product-edition-$edition.json\"", installerScript, StringComparison.Ordinal);
            Assert.Contains("Assert-Inside -Path $staleInstaller.FullName -Root $outputRoot", installerScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path $scriptRoot \"verify-package-payload.ps1\"", installerScript, StringComparison.Ordinal);
            Assert.Contains("Join-Path $artifactsRoot \"tauri-bundle\\resources\"", installerScript, StringComparison.Ordinal);

            Assert.Contains("$payloadVerifier = Join-Path $scriptRoot \"verify-package-payload.ps1\"", desktopScript, StringComparison.Ordinal);
            Assert.Contains("\"-Profile\", \"Desktop\"", desktopScript, StringComparison.Ordinal);
            Assert.Contains("\"-RuntimeIdentifier\", \"win-x64\"", desktopScript, StringComparison.Ordinal);

            Assert.Contains(".codex-runtime/browser-pdf-check", browserPdfScript, StringComparison.Ordinal);
            Assert.Contains("Assert-RepositoryChildPath", browserPdfScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Path]::GetTempPath", browserPdfScript, StringComparison.Ordinal);
        }

        [Fact]
        public void ScriptSuite_ShouldValidateAllScriptFormatsAndNativeExitCodes()
        {
            string verifier = File.ReadAllText(ResolveWorkspacePath("scripts", "verify-script-suite.ps1"));
            string apiClientGenerator = File.ReadAllText(ResolveWorkspacePath("scripts", "generate-api-client.ps1"));
            string testRunner = File.ReadAllText(ResolveWorkspacePath("scripts", "run-tests.ps1"));

            Assert.Contains("Get-ChildItem -LiteralPath $scriptRoot -Recurse -File", verifier, StringComparison.Ordinal);
            Assert.Contains("PowerShellScriptCount", verifier, StringComparison.Ordinal);
            Assert.Contains("CommandScriptCount", verifier, StringComparison.Ordinal);
            Assert.Contains("ModuleScriptCount", verifier, StringComparison.Ordinal);
            Assert.Contains("node", verifier, StringComparison.Ordinal);
            Assert.Contains("--check", verifier, StringComparison.Ordinal);
            Assert.Contains("run-powershell-entry.cmd", verifier, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Invoke-ExportDocExternal -FilePath \"dotnet\"", apiClientGenerator, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet run --project", apiClientGenerator, StringComparison.Ordinal);
            Assert.Contains("verify-script-suite.ps1", testRunner, StringComparison.Ordinal);
            Assert.Contains("RequireBrowserPdfTests", testRunner, StringComparison.Ordinal);
            Assert.Contains("EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE", testRunner, StringComparison.Ordinal);
            Assert.Contains("--filter", testRunner, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsInstallerScripts_ShouldBuildAndVerifyDistinctProductEditions()
        {
            string buildScript = File.ReadAllText(ResolveWorkspacePath("scripts", "build-windows-installers.ps1"));
            string verifyScript = File.ReadAllText(ResolveWorkspacePath("scripts", "verify-windows-installers.ps1"));
            string provisionScript = File.ReadAllText(ResolveWorkspacePath("scripts", "provision-tauri-nsis.ps1"));
            string bundleScript = File.ReadAllText(ResolveWorkspacePath("scripts", "prepare-tauri-bundle.mjs"));

            Assert.Contains("com.exportdocmanager.desktop.document", buildScript, StringComparison.Ordinal);
            Assert.Contains("com.exportdocmanager.desktop.sales", buildScript, StringComparison.Ordinal);
            Assert.Contains("com.exportdocmanager.desktop.full", buildScript, StringComparison.Ordinal);
            Assert.Contains("Assert-Inside -Path $bundleRoot -Root $resolvedCargoTargetDir", buildScript, StringComparison.Ordinal);
            Assert.Contains("Remove-Item -LiteralPath $verifiedBundleRoot -Recurse -Force", buildScript, StringComparison.Ordinal);
            Assert.Contains("installers-manifest.json", buildScript, StringComparison.Ordinal);
            Assert.Contains("$existingResult.Edition -notin $Editions", buildScript, StringComparison.Ordinal);
            Assert.Contains("\"-Config\", $configPath", buildScript, StringComparison.Ordinal);
            Assert.Contains("compression = \"zlib\"", buildScript, StringComparison.Ordinal);
            Assert.Contains("distinct application identifiers", verifyScript, StringComparison.Ordinal);
            Assert.Contains("distinct SHA256 hashes", verifyScript, StringComparison.Ordinal);
            Assert.Contains("product-edition.json", bundleScript, StringComparison.Ordinal);
            Assert.Contains("normalizeProductEdition", bundleScript, StringComparison.Ordinal);
            Assert.Contains("provision-tauri-nsis.ps1", buildScript, StringComparison.Ordinal);
            Assert.Contains("EF7FF767E5CBD9EDD22ADD3A32C9B8F4500BB10D", provisionScript, StringComparison.Ordinal);
            Assert.Contains("75197FEE3C6A814FE035788D1C34EAD39349B860", provisionScript, StringComparison.Ordinal);
            Assert.Contains("Assert-Inside -Path (Join-Path $resolvedCargoTargetDir \".tauri\\NSIS\")", provisionScript, StringComparison.Ordinal);
        }

        [Fact]
        public void WindowsCommandEntryPoints_ShouldUseOneSharedHost()
        {
            string sharedHost = File.ReadAllText(ResolveWorkspacePath("scripts", "lib", "run-powershell-entry.cmd"));
            string powerShellSupport = File.ReadAllText(ResolveWorkspacePath("scripts", "lib", "build-script-support.ps1"));
            Assert.Contains("where pwsh.exe", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("where powershell.exe", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-ExecutionPolicy Bypass", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("set \"EXPORTDOCMANAGER_NO_PAUSE=1\"", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"%%~A\"==\"-NoPause\"", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-File \"%EXPORTDOCMANAGER_PS_SCRIPT%\" %*", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("set \"EXIT_CODE=%ERRORLEVEL%\"", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pause >nul", sharedHost, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$env:ComSpec /d /c \"pause >nul\"", powerShellSupport, StringComparison.OrdinalIgnoreCase);

            (string CommandFile, string PowerShellFile)[] entryPoints =
            {
                ("build-windows-desktop-run.cmd", "build-windows-desktop-run.ps1"),
                ("build-windows-editions.cmd", "build-windows-editions.ps1"),
                ("build-windows-installers.cmd", "build-windows-installers.ps1"),
                ("run-tests.cmd", "run-tests.ps1")
            };

            foreach ((string commandFile, string powerShellFile) in entryPoints)
            {
                string content = File.ReadAllText(ResolveWorkspacePath("scripts", commandFile));
                Assert.Contains(powerShellFile, content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("lib\\run-powershell-entry.cmd", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("where pwsh", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("pause", content, StringComparison.OrdinalIgnoreCase);
                Assert.InRange(File.ReadAllLines(ResolveWorkspacePath("scripts", commandFile)).Length, 1, 6);
            }
        }

        [Fact]
        public void TauriBuildScripts_ShouldDefaultCargoTargetsUnderWorkspaceArtifacts()
        {
            string[] scriptNames =
            {
                "run-tauri-local.ps1",
                "smoke-tauri-desktop.ps1",
                "build-windows-desktop-run.ps1",
                "prepare-windows-desktop-run.ps1"
            };

            foreach (string scriptName in scriptNames)
            {
                string content = File.ReadAllText(ResolveWorkspacePath("scripts", scriptName));

                Assert.Contains("cargo-target-exportdoc", content, StringComparison.Ordinal);
                Assert.True(
                    content.Contains("Join-Path $repoRoot \"artifacts\\cargo-target-exportdoc\"", StringComparison.Ordinal) ||
                    content.Contains("Join-Path $artifactsRoot \"cargo-target-exportdoc\"", StringComparison.Ordinal),
                    $"{scriptName} should resolve the default Cargo target below the workspace artifacts root.");
                Assert.DoesNotContain(@"D:\Rust\cargo-target-exportdoc", content, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ApiProject_ShouldCopyStableResourcesOnlyDuringPublish()
        {
            string projectPath = ResolveWorkspacePath(
                "src",
                "ExportDocManager.Api",
                "ExportDocManager.Api.csproj");
            string content = File.ReadAllText(projectPath);

            Assert.Contains("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", content, StringComparison.Ordinal);
        }

        [Fact]
        public void DotNetBuilds_ShouldDefaultToTheCurrentHostRuntimeIdentifier()
        {
            string propsPath = ResolveWorkspacePath("Directory.Build.props");
            string content = File.ReadAllText(propsPath);

            Assert.Contains("RuntimeInformation", content, StringComparison.Ordinal);
            Assert.Contains("OperatingSystem]::IsLinux", content, StringComparison.Ordinal);
            Assert.Contains("OperatingSystem]::IsMacOS", content, StringComparison.Ordinal);
            Assert.Contains("<SelfContained Condition=", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", content, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveWorkspacePath(params string[] segments)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new FileNotFoundException($"Could not locate {string.Join("/", segments)} from test output.");
        }
    }
}
