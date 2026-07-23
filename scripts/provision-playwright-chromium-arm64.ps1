param(
    [string]$DestinationRoot = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "lib/build-script-support.ps1")

$playwrightPackageVersion = "1.61.0"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$isWindowsPlatform = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [Runtime.InteropServices.OSPlatform]::Windows)
$pathComparison = if ($isWindowsPlatform) {
    [System.StringComparison]::OrdinalIgnoreCase
} else {
    [System.StringComparison]::Ordinal
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $fullRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, $pathComparison)) {
        throw "$Purpose must stay inside $fullRoot. Resolved path: $fullPath"
    }

    return $fullPath
}

function Remove-RepositoryEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $verifiedPath = Assert-ChildPath -Path $Path -Root $repoRoot -Purpose $Purpose
    Remove-Item -LiteralPath $verifiedPath -Recurse -Force
}

function Get-ValidatedStagedBrowser {
    param([Parameter(Mandatory = $true)][string]$Destination)

    $manifestPath = Join-Path $Destination "chromium-arm64.manifest.json"
    $readmePath = Join-Path $Destination "README.txt"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
        return $null
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Warning "Existing Chromium ARM64 manifest is unreadable and will be replaced: $($_.Exception.Message)"
        return $null
    }

    if ([string]$manifest.product -ne "Chromium ARM64" -or
        [string]$manifest.architecture -ne "linux-arm64" -or
        [string]$manifest.playwrightPackageVersion -ne $playwrightPackageVersion -or
        [string]::IsNullOrWhiteSpace([string]$manifest.executablePath) -or
        [System.IO.Path]::IsPathRooted([string]$manifest.executablePath)) {
        return $null
    }

    try {
        $browserPath = Assert-ChildPath `
            -Path (Join-Path $Destination ([string]$manifest.executablePath)) `
            -Root $Destination `
            -Purpose "Chromium ARM64 manifest executable"
    } catch {
        Write-Warning $_.Exception.Message
        return $null
    }

    if (-not (Test-Path -LiteralPath $browserPath -PathType Leaf)) {
        return $null
    }

    try {
        & chmod +x -- $browserPath
        if ($LASTEXITCODE -ne 0) {
            return $null
        }
        Invoke-ExportDocExternal -FilePath $browserPath -Arguments @("--version") | Out-Null
    } catch {
        Write-Warning "Existing Chromium ARM64 executable failed validation and will be replaced: $($_.Exception.Message)"
        return $null
    }

    return $browserPath
}

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot "Browsers"
}
$destinationRootFullPath = [System.IO.Path]::GetFullPath($DestinationRoot)
$destination = Assert-ChildPath -Path (Join-Path $destinationRootFullPath "ChromiumArm64") -Root $repoRoot -Purpose "Chromium ARM64 destination"
$buildRoot = Assert-ChildPath -Path (Join-Path $repoRoot "artifacts/playwright-arm64-build") -Root $repoRoot -Purpose "Chromium ARM64 build output"
$cacheRoot = Assert-ChildPath -Path (Join-Path $repoRoot "artifacts/playwright-arm64-cache") -Root $repoRoot -Purpose "Chromium ARM64 browser cache"

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux) -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64) {
    throw "Chromium ARM64 provisioning must run on a Linux ARM64 runner."
}

if ((Test-Path -LiteralPath $destination -PathType Container) -and -not $Force) {
    $existingBrowser = Get-ValidatedStagedBrowser -Destination $destination
    if (-not [string]::IsNullOrWhiteSpace($existingBrowser)) {
        Remove-RepositoryEntry -Path $buildRoot -Purpose "stale Chromium ARM64 build output"
        Remove-RepositoryEntry -Path $cacheRoot -Purpose "stale Chromium ARM64 browser cache"
        Write-Host "Validated existing Chromium ARM64 runtime: $existingBrowser"
        return
    }
}

Remove-RepositoryEntry -Path $destination -Purpose "stale Chromium ARM64 destination"
Remove-RepositoryEntry -Path $buildRoot -Purpose "stale Chromium ARM64 build output"
Remove-RepositoryEntry -Path $cacheRoot -Purpose "stale Chromium ARM64 browser cache"

$playwrightHostProject = Join-Path $repoRoot "src/ExportDocManager.Api/ExportDocManager.Api.csproj"
$previousPlaywrightBrowsersPath = $env:PLAYWRIGHT_BROWSERS_PATH
try {
    Invoke-ExportDocExternal -FilePath "dotnet" -Arguments @(
        "restore", $playwrightHostProject, "-r", "linux-arm64", "--configfile", (Join-Path $repoRoot "NuGet.Config")) -WorkingDirectory $repoRoot
    Invoke-ExportDocExternal -FilePath "dotnet" -Arguments @(
        "build", $playwrightHostProject, "-c", "Release", "-r", "linux-arm64", "--no-restore", "-o", $buildRoot) -WorkingDirectory $repoRoot

    $installer = Join-Path $buildRoot "playwright.ps1"
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw "Microsoft.Playwright installer was not produced: $installer"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot "Microsoft.Playwright.dll") -PathType Leaf)) {
        throw "Microsoft.Playwright.dll was not copied beside the installer."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot ".playwright") -PathType Container)) {
        throw "Microsoft.Playwright driver directory was not copied beside the installer."
    }

    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    $env:PLAYWRIGHT_BROWSERS_PATH = $cacheRoot
    & $installer install --with-deps chromium
    if ($LASTEXITCODE -ne 0) {
        throw "Microsoft.Playwright Chromium installation failed with exit code $LASTEXITCODE."
    }

    $browserCandidates = @(Get-ChildItem -LiteralPath $cacheRoot -File -Recurse -Filter chrome |
        Where-Object { $_.FullName -match '[\\/]chromium-[^\\/]+[\\/].*[\\/]chrome$' } |
        Sort-Object FullName)
    if ($browserCandidates.Count -ne 1) {
        throw "Expected exactly one Playwright Chromium ARM64 executable under $cacheRoot, found $($browserCandidates.Count)."
    }

    $browser = $browserCandidates[0]
    $packageRoot = Assert-ChildPath -Path $browser.Directory.Parent.FullName -Root $cacheRoot -Purpose "Playwright Chromium ARM64 package"
    $relativeExecutablePath = [System.IO.Path]::GetRelativePath($packageRoot, $browser.FullName)

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $packageRoot -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse -Force
    }

    $stagedBrowserPath = Assert-ChildPath `
        -Path (Join-Path $destination $relativeExecutablePath) `
        -Root $destination `
        -Purpose "Staged Chromium ARM64 executable"
    if (-not (Test-Path -LiteralPath $stagedBrowserPath -PathType Leaf)) {
        throw "Staged Chromium ARM64 executable was not found: $stagedBrowserPath"
    }

    & chmod +x -- $stagedBrowserPath
    if ($LASTEXITCODE -ne 0) {
        throw "Could not mark the staged Chromium ARM64 executable as runnable."
    }
    Invoke-ExportDocExternal -FilePath $stagedBrowserPath -Arguments @("--version") | Out-Null

    [ordered]@{
        schemaVersion = 1
        product = "Chromium ARM64"
        architecture = "linux-arm64"
        source = "Microsoft.Playwright trusted open-source Chromium build"
        playwrightPackageVersion = $playwrightPackageVersion
        executablePath = $relativeExecutablePath
        generatedAt = (Get-Date).ToUniversalTime().ToString("o")
        storagePolicy = "Bundled below program-root Browsers/ChromiumArm64; never installed into system directories."
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination "chromium-arm64.manifest.json") -Encoding UTF8

    @"
Chromium ARM64

This directory contains the open-source Chromium build downloaded by Microsoft.Playwright $playwrightPackageVersion
on a native Linux ARM64 runner. It is not Google Chrome Headless Shell and is labelled accordingly.
Chromium source and license information: https://www.chromium.org/Home/
"@ | Set-Content -LiteralPath (Join-Path $destination "README.txt") -Encoding UTF8
} catch {
    Remove-RepositoryEntry -Path $destination -Purpose "incomplete Chromium ARM64 destination"
    throw
} finally {
    $env:PLAYWRIGHT_BROWSERS_PATH = $previousPlaywrightBrowsersPath
    Remove-RepositoryEntry -Path $buildRoot -Purpose "temporary Chromium ARM64 build output"
    Remove-RepositoryEntry -Path $cacheRoot -Purpose "temporary Chromium ARM64 browser cache"
}
