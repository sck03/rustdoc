[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRestore,
    [switch]$RequireBrowserPdfTests,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

$powerShellExecutable = Resolve-ExportDocPowerShellExecutable
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $scriptRoot "verify-script-suite.ps1")
) -WorkingDirectory $repoRoot

$resultsDirectory = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
$configuredChromium = $env:EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE
$hasConfiguredChromium = -not [string]::IsNullOrWhiteSpace($configuredChromium) -and
    (Test-Path -LiteralPath $configuredChromium -PathType Leaf)
$hasProgramRootChromium = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "Browsers") -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome-headless-shell", "chrome.exe", "chrome", "chromium") }).Count -gt 0
$hasBrowserPdfRenderer = $hasConfiguredChromium -or $hasProgramRootChromium
if ($RequireBrowserPdfTests -and -not $hasBrowserPdfRenderer) {
    throw "Browser PDF tests were required, but Chromium was not found. Run scripts\provision-chrome-for-testing.ps1 or set EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE."
}

$arguments = @(
    "test",
    (Join-Path $repoRoot "ExportDocManager.sln"),
    "-c", $Configuration,
    "--logger", "trx;LogFilePrefix=ExportDocManager",
    "--results-directory", $resultsDirectory,
    "-v", "minimal"
)
if ($NoRestore) {
    $arguments += "--no-restore"
}
if (-not $hasBrowserPdfRenderer) {
    $browserPdfFilter = @(
        "FullyQualifiedName!~RenderBuiltInProgramTemplatesToPdf_ShouldUseProgramRootBrowserAndRuntimeDataRoot",
        "FullyQualifiedName!~RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf_ShouldPreservePaginationAndDomainIsolation"
    ) -join "&"
    $arguments += @("--filter", $browserPdfFilter)
    Write-Warning "Chromium was not found. Two real browser PDF tests will be skipped. Use -RequireBrowserPdfTests for strict release validation."
}

Write-Host "Running ExportDocManager solution tests ($Configuration)..."
Invoke-ExportDocExternal -FilePath "dotnet" -Arguments $arguments -WorkingDirectory $repoRoot
Write-Host "Test results saved under: $resultsDirectory"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
