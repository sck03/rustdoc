param(
    [string]$DestinationRoot = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $DestinationRoot = Join-Path $repoRoot "Browsers" }
$destination = Join-Path ([System.IO.Path]::GetFullPath($DestinationRoot)) "ChromiumArm64"
if (-not $destination.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Chromium ARM64 destination must stay inside the repository workspace."
}
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux) -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64) {
    throw "Chromium ARM64 provisioning must run on a Linux ARM64 runner."
}
if ((Test-Path $destination) -and -not $Force) { Write-Host "Chromium ARM64 already exists: $destination"; exit 0 }

$buildRoot = Join-Path $repoRoot "artifacts/playwright-arm64-build"
$cacheRoot = Join-Path $repoRoot "artifacts/playwright-arm64-cache"
dotnet restore (Join-Path $repoRoot "src/ExportDocManager.Infrastructure/ExportDocManager.Infrastructure.csproj") -r linux-arm64 --configfile (Join-Path $repoRoot "NuGet.Config")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build (Join-Path $repoRoot "src/ExportDocManager.Infrastructure/ExportDocManager.Infrastructure.csproj") -c Release -r linux-arm64 --no-restore -o $buildRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$installer = Join-Path $buildRoot "playwright.ps1"
if (-not (Test-Path $installer -PathType Leaf)) { throw "Microsoft.Playwright installer was not produced: $installer" }
$env:PLAYWRIGHT_BROWSERS_PATH = $cacheRoot
& $installer install chromium
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$browser = Get-ChildItem $cacheRoot -File -Recurse -Filter chrome |
    Where-Object { $_.FullName -match 'chromium-[^\\/]+[\\/]chrome-linux[\\/]chrome$' } |
    Select-Object -First 1
if ($null -eq $browser) { throw "Playwright Chromium ARM64 executable was not found under $cacheRoot" }
$packageRoot = $browser.Directory.Parent.FullName
if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $packageRoot "*") $destination -Recurse -Force
$stagedBrowser = Get-ChildItem $destination -File -Recurse -Filter chrome | Select-Object -First 1
& chmod +x -- $stagedBrowser.FullName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $stagedBrowser.FullName --version
if ($LASTEXITCODE -ne 0) { throw "Bundled Chromium ARM64 failed to start." }

[ordered]@{
    product = "Chromium ARM64"
    architecture = "linux-arm64"
    source = "Microsoft.Playwright trusted open-source Chromium build"
    playwrightPackageVersion = "1.61.0"
    executablePath = [System.IO.Path]::GetRelativePath($destination, $stagedBrowser.FullName)
    storagePolicy = "Bundled below program-root Browsers/ChromiumArm64; never installed into system directories."
} | ConvertTo-Json | Set-Content (Join-Path $destination "chromium-arm64.manifest.json") -Encoding UTF8

@"
Chromium ARM64

This directory contains the open-source Chromium build downloaded by Microsoft.Playwright 1.61.0
on a native Linux ARM64 runner. It is not Google Chrome Headless Shell and is labelled accordingly.
Chromium source and license information: https://www.chromium.org/Home/
"@ | Set-Content (Join-Path $destination "README.txt") -Encoding UTF8
