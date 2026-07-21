[CmdletBinding()]
param(
    [string]$CargoTargetDir,
    [string]$LicenseCargoTargetDir,
    [string]$ExcelAnalyzerCargoTargetDir,
    [string]$OutputDir,
    [string]$LicenseOutputDir,
    [ValidateSet("Document", "Sales", "Full")]
    [string]$ProductEdition = "Full",
    [switch]$IncludeLicenseKeygen,
    [switch]$SkipExcelAnalyzer
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-Inside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = Resolve-FullPath -Path $Path
    $fullRoot = Resolve-FullPath -Path $Root
    if (-not $fullPath.StartsWith($fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($fullPath, $fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay inside $fullRoot. Resolved path: $fullPath"
    }

    return $fullPath
}

function Assert-Outside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = Resolve-FullPath -Path $Path
    $fullRoot = Resolve-FullPath -Path $Root
    if ($fullPath.StartsWith($fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($fullPath, $fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay outside $fullRoot. Resolved path: $fullPath"
    }

    return $fullPath
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required file was not found: $Source"
    }

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        $sourceItem = Get-Item -LiteralPath $Source
        $destinationItem = Get-Item -LiteralPath $Destination
        if ($sourceItem.Length -eq $destinationItem.Length) {
            $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
            if ([string]::Equals($sourceHash, $destinationHash, [System.StringComparison]::OrdinalIgnoreCase)) {
                return
            }
        }
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-RequiredEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required runtime entry was not found: $Source"
    }

    if (Test-Path -LiteralPath $Source -PathType Leaf) {
        Copy-RequiredFile -Source $Source -Destination $Destination
        return
    }

    $sourceRoot = Resolve-FullPath -Path $Source
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    foreach ($child in Get-ChildItem -LiteralPath $Source -Force -Recurse) {
        $childPath = Resolve-FullPath -Path $child.FullName
        $relativePath = $childPath.Substring($sourceRoot.Length).TrimStart([char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        ))
        $targetPath = Join-Path $Destination $relativePath

        if ($child.PSIsContainer) {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
            continue
        }

        Copy-RequiredFile -Source $child.FullName -Destination $targetPath
    }
}

function Remove-GeneratedEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $fullPath = Resolve-FullPath -Path $Path
    $fullRoot = Resolve-FullPath -Path $Root
    if (-not $fullPath.StartsWith($fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($fullPath, $fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay inside $fullRoot. Resolved path: $fullPath"
    }

    $maximumAttempts = 8
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -ge $maximumAttempts) {
                throw "$Purpose could not be removed after $maximumAttempts attempts: $fullPath. $($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function ConvertTo-NormalizedWindowsPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $normalized = $Path.Replace('/', '\')
    if ($normalized.StartsWith('\\?\', [System.StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(4)
    }

    return $normalized.TrimEnd('\')
}

function Test-PathInsideRoot {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $normalizedPath = ConvertTo-NormalizedWindowsPath -Path $Path
    $normalizedRoot = ConvertTo-NormalizedWindowsPath -Path $Root
    if ([string]::IsNullOrWhiteSpace($normalizedPath) -or [string]::IsNullOrWhiteSpace($normalizedRoot)) {
        return $false
    }

    return [string]::Equals($normalizedPath, $normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalizedPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-OutputOwnedProcesses {
    param([Parameter(Mandatory = $true)][string]$OutputRoot)

    $currentProcessId = $PID
    return @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessId -ne $currentProcessId -and
        (Test-PathInsideRoot -Path ([string]$_.ExecutablePath) -Root $OutputRoot)
    })
}

function Stop-OutputOwnedProcesses {
    param([Parameter(Mandatory = $true)][string]$OutputRoot)

    $processes = Get-OutputOwnedProcesses -OutputRoot $OutputRoot
    if ($processes.Count -eq 0) {
        return
    }

    foreach ($processInfo in $processes) {
        Write-Host "Stopping stale packaged process $($processInfo.Name) ($($processInfo.ProcessId)) before replacing $OutputRoot."
        try {
            $process = [System.Diagnostics.Process]::GetProcessById([int]$processInfo.ProcessId)
            $process.Kill($true)
        } catch {
            Stop-Process -Id $processInfo.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 200
        $remaining = Get-OutputOwnedProcesses -OutputRoot $OutputRoot
    } while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($remaining.Count -gt 0) {
        $details = ($remaining | ForEach-Object { "$($_.Name) ($($_.ProcessId))" }) -join ', '
        throw "Packaged processes are still running under $OutputRoot after cleanup: $details"
    }
}

function Get-ChromeForTestingPlatform {
    if ([Environment]::Is64BitOperatingSystem) {
        return "win64"
    }

    return "win32"
}

function Copy-BrowserRuntimeResources {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Required browser runtime directory was not found: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Remove-GeneratedEntry -Path (Join-Path $Destination "Browsers") -Root $OutputRoot -Purpose "stale nested browser directory"

    $platform = Get-ChromeForTestingPlatform
    foreach ($relativePath in @(
        "ChromeForTesting\$platform\chrome-win64",
        "ChromeForTesting\$platform\chrome-win32",
        "ChromeForTesting\$platform\Chrome",
        "ChromeForTesting\$platform\chrome",
        "ChromeForTesting\$platform\chrome-mac-x64",
        "ChromeForTesting\$platform\chrome-mac-arm64",
        "ChromeForTesting\$platform\chrome-linux64"
    )) {
        Remove-GeneratedEntry -Path (Join-Path $Destination $relativePath) -Root $OutputRoot -Purpose "unused packaged browser runtime"
    }

    $readmePath = Join-Path $Source "README.md"
    if (Test-Path -LiteralPath $readmePath -PathType Leaf) {
        Copy-RequiredFile -Source $readmePath -Destination (Join-Path $Destination "README.md")
    }

    $platformSource = Join-Path $Source "ChromeForTesting\$platform"
    $platformDestination = Join-Path $Destination "ChromeForTesting\$platform"
    $headlessShellSource = Join-Path $platformSource "ChromeHeadlessShell"
    if (Test-Path -LiteralPath $headlessShellSource -PathType Container) {
        New-Item -ItemType Directory -Path $platformDestination -Force | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $platformSource -Force -File) {
            Copy-RequiredFile -Source $file.FullName -Destination (Join-Path $platformDestination $file.Name)
        }

        Copy-RequiredEntry -Source $headlessShellSource -Destination (Join-Path $platformDestination "ChromeHeadlessShell")
        return
    }

    $directCandidates = @(
        "chrome-headless-shell.exe",
        "chrome-headless-shell",
        "ChromeHeadlessShell\chrome-headless-shell.exe",
        "ChromeHeadlessShell\chrome-headless-shell"
    )
    foreach ($relativeCandidate in $directCandidates) {
        $candidate = Join-Path $Source $relativeCandidate
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $topLevel = ($relativeCandidate -split "[\\/]", 2)[0]
            Copy-RequiredEntry -Source (Join-Path $Source $topLevel) -Destination (Join-Path $Destination $topLevel)
            return
        }
    }

    throw "Chrome Headless Shell for $platform was not found under $Source. Run scripts\provision-chrome-for-testing.ps1 -Product ChromeHeadlessShell before preparing the desktop run directory."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"

if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
        $CargoTargetDir = $env:CARGO_TARGET_DIR
    } else {
        $CargoTargetDir = Join-Path $artifactsRoot "cargo-target-exportdoc"
    }
}

if ([string]::IsNullOrWhiteSpace($LicenseCargoTargetDir)) {
    $LicenseCargoTargetDir = Join-Path $artifactsRoot "cargo-target-license-keygen"
}

if ([string]::IsNullOrWhiteSpace($ExcelAnalyzerCargoTargetDir)) {
    $ExcelAnalyzerCargoTargetDir = Join-Path $artifactsRoot "cargo-target-excel-analyzer"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $artifactsRoot "windows-desktop-run\ExportDocManager"
}

$resolvedCargoTargetDir = Resolve-FullPath -Path $CargoTargetDir
$resolvedLicenseCargoTargetDir = if ($IncludeLicenseKeygen) { Resolve-FullPath -Path $LicenseCargoTargetDir } else { $null }
$resolvedExcelAnalyzerCargoTargetDir = Resolve-FullPath -Path $ExcelAnalyzerCargoTargetDir
$resolvedOutputDir = Assert-Inside -Path $OutputDir -Root $artifactsRoot -Purpose "Windows desktop run output"
if ($IncludeLicenseKeygen -and [string]::IsNullOrWhiteSpace($LicenseOutputDir)) {
    $LicenseOutputDir = Join-Path (Split-Path -Parent $resolvedOutputDir) "KEY"
}

$resolvedLicenseOutputDir = $null
if ($IncludeLicenseKeygen) {
    $resolvedLicenseOutputDir = Assert-Inside -Path $LicenseOutputDir -Root $artifactsRoot -Purpose "License key generator output"
    $resolvedLicenseOutputDir = Assert-Outside -Path $resolvedLicenseOutputDir -Root $resolvedOutputDir -Purpose "License key generator output"
}

$resourcesRoot = Join-Path $artifactsRoot "tauri-bundle\resources"
$mainExe = Join-Path $resolvedCargoTargetDir "release\export-doc-tauri.exe"
$licenseExe = if ($IncludeLicenseKeygen) { Join-Path $resolvedLicenseCargoTargetDir "release\export-doc-license-keygen-tauri.exe" } else { $null }
$excelAnalyzerExe = Join-Path $resolvedExcelAnalyzerCargoTargetDir "release\exportdoc-excel-analyzer.exe"
$mainWebView2Loader = Join-Path $resourcesRoot "WebView2Loader.dll"
$licenseWebView2Loader = if ($IncludeLicenseKeygen) { Join-Path $resolvedLicenseCargoTargetDir "release\WebView2Loader.dll" } else { $null }

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

# The portable output is disposable. A previously launched edition can leave
# its API/browser children holding App_Data or executable files open, so only
# processes whose executable belongs to this exact output root are terminated.
Stop-OutputOwnedProcesses -OutputRoot $resolvedOutputDir

# Customer packages contain stable program files only. The artifacts output is
# a disposable build directory, so stale runtime data is always removed before
# program files are copied into place.
foreach ($runtimeEntryName in @("App_Data", "logs")) {
    Remove-GeneratedEntry `
        -Path (Join-Path $resolvedOutputDir $runtimeEntryName) `
        -Root $resolvedOutputDir `
        -Purpose "stale customer runtime $runtimeEntryName directory"
}

Copy-RequiredFile -Source $mainExe -Destination (Join-Path $resolvedOutputDir "ExportDocManager.exe")

$versionManifestPath = Join-Path $repoRoot "version.json"
$productVersion = ""
if (Test-Path -LiteralPath $versionManifestPath -PathType Leaf) {
    $versionManifest = Get-Content -LiteralPath $versionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $productVersion = [string]$versionManifest.version
}
$editionMetadata = switch ($ProductEdition) {
    "Document" { @{ displayName = "单证员版"; workspaces = @("document") } }
    "Sales" { @{ displayName = "业务员版"; workspaces = @("sales") } }
    default { @{ displayName = "全功能版"; workspaces = @("document", "sales") } }
}
[ordered]@{
    schemaVersion = 1
    product = "ExportDocManager"
    productVersion = $productVersion
    edition = $ProductEdition
    displayName = $editionMetadata.displayName
    enabledWorkspaces = $editionMetadata.workspaces
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    runtimeDataPolicy = "Runtime business data defaults to App_Data beside this program directory."
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $resolvedOutputDir "product-edition.json") -Encoding UTF8

foreach ($entryName in @("sidecar", "Templates", "Resources", "OcrModels", "Browsers", "runtime-layout.json", "WebView2Loader.dll")) {
    $entryDestination = Join-Path $resolvedOutputDir $entryName

    if ($entryName -eq "Browsers") {
        Copy-BrowserRuntimeResources -Source (Join-Path $resourcesRoot $entryName) -Destination $entryDestination -OutputRoot $resolvedOutputDir
        continue
    }

    $entrySource = Join-Path $resourcesRoot $entryName
    if (Test-Path -LiteralPath $entrySource -PathType Container) {
        Remove-GeneratedEntry -Path (Join-Path $entryDestination $entryName) -Root $resolvedOutputDir -Purpose "stale nested $entryName directory"
    }

    Copy-RequiredEntry -Source $entrySource -Destination $entryDestination
}

$toolsDir = Join-Path $resolvedOutputDir "Tools"
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
Remove-GeneratedEntry -Path (Join-Path $toolsDir "ExportDocLicenseKeyGen.exe") -Root $resolvedOutputDir -Purpose "stale packaged license key generator"
Remove-GeneratedEntry -Path (Join-Path $toolsDir "WebView2Loader.dll") -Root $resolvedOutputDir -Purpose "stale packaged license key generator WebView2 loader"

if ($IncludeLicenseKeygen) {
    New-Item -ItemType Directory -Path $resolvedLicenseOutputDir -Force | Out-Null
    Copy-RequiredFile -Source $licenseExe -Destination (Join-Path $resolvedLicenseOutputDir "ExportDocLicenseKeyGen.exe")
}
$packagedExcelAnalyzer = Join-Path $toolsDir "exportdoc-excel-analyzer.exe"
if ($SkipExcelAnalyzer) {
    Remove-GeneratedEntry -Path $packagedExcelAnalyzer -Root $resolvedOutputDir -Purpose "optional external Excel analyzer"
    Write-Host "External Rust Excel analyzer skipped; the API sidecar will use the built-in .NET module."
} elseif (Test-Path -LiteralPath $excelAnalyzerExe -PathType Leaf) {
    Copy-RequiredFile -Source $excelAnalyzerExe -Destination (Join-Path $toolsDir "exportdoc-excel-analyzer.exe")
} else {
    Write-Warning "Rust Excel analyzer was not found at $excelAnalyzerExe. The API sidecar will use the built-in .NET module."
}

if ($IncludeLicenseKeygen) {
    if (Test-Path -LiteralPath $licenseWebView2Loader -PathType Leaf) {
        Copy-RequiredFile -Source $licenseWebView2Loader -Destination (Join-Path $resolvedLicenseOutputDir "WebView2Loader.dll")
    } else {
        Copy-RequiredFile -Source $mainWebView2Loader -Destination (Join-Path $resolvedLicenseOutputDir "WebView2Loader.dll")
    }
}

Write-Host "Prepared Windows desktop run directory:"
Write-Host "  $resolvedOutputDir"
Write-Host "Main executable:"
Write-Host "  $(Join-Path $resolvedOutputDir "ExportDocManager.exe")"
Write-Host "Product edition:"
Write-Host "  $ProductEdition ($(Join-Path $resolvedOutputDir "product-edition.json"))"
if ($IncludeLicenseKeygen) {
    Write-Host "Internal license key generator directory:"
    Write-Host "  $resolvedLicenseOutputDir"
    Write-Host "License key generator:"
    Write-Host "  $(Join-Path $resolvedLicenseOutputDir "ExportDocLicenseKeyGen.exe")"
}
if (-not $SkipExcelAnalyzer) {
    Write-Host "Rust Excel analyzer:"
    Write-Host "  $(Join-Path $toolsDir "exportdoc-excel-analyzer.exe")"
}
