[CmdletBinding()]
param(
    [string]$CargoTargetDir,
    [switch]$AllowSystemDrive
)

$ErrorActionPreference = "Stop"
$nsisArchiveSha1 = "EF7FF767E5CBD9EDD22ADD3A32C9B8F4500BB10D"
$tauriPluginSha1 = "75197FEE3C6A814FE035788D1C34EAD39349B860"

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

function Test-ExpectedHash {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha1
    )
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        [string]::Equals((Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash, $ExpectedSha1, [System.StringComparison]::OrdinalIgnoreCase)
}

function Download-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)][string[]]$Urls,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$ExpectedSha1
    )

    foreach ($url in $Urls) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        & curl.exe -L --fail --connect-timeout 15 --max-time 45 --retry 1 --retry-delay 2 -o $Destination $url
        if ($LASTEXITCODE -eq 0 -and (Test-ExpectedHash -Path $Destination -ExpectedSha1 $ExpectedSha1)) {
            return
        }
    }
    throw "Could not download a verified Tauri NSIS dependency to $Destination."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    $CargoTargetDir = Join-Path $artifactsRoot "cargo-target-exportdoc"
}
$resolvedCargoTargetDir = Resolve-FullPath -Path $CargoTargetDir
if (-not $AllowSystemDrive -and -not [string]::IsNullOrWhiteSpace($env:SystemDrive) -and
    $resolvedCargoTargetDir.StartsWith($env:SystemDrive, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Tauri NSIS tools must not be provisioned on the system drive: $resolvedCargoTargetDir"
}

$toolDownloadsRoot = Assert-Inside -Path (Join-Path $artifactsRoot "tool-downloads") -Root $artifactsRoot -Purpose "Tauri tool download cache"
$extractRoot = Assert-Inside -Path (Join-Path $toolDownloadsRoot "nsis-extract") -Root $artifactsRoot -Purpose "Tauri NSIS extraction directory"
$nsisRoot = Assert-Inside -Path (Join-Path $resolvedCargoTargetDir ".tauri\NSIS") -Root $resolvedCargoTargetDir -Purpose "Tauri local NSIS cache"
$archivePath = Join-Path $toolDownloadsRoot "nsis-3.11.zip"
$pluginPath = Join-Path $nsisRoot "Plugins\x86-unicode\additional\nsis_tauri_utils.dll"
$makensisPath = Join-Path $nsisRoot "Bin\makensis.exe"

if ((Test-Path -LiteralPath $makensisPath -PathType Leaf) -and (Test-ExpectedHash -Path $pluginPath -ExpectedSha1 $tauriPluginSha1)) {
    Write-Host "Tauri NSIS tools are ready: $nsisRoot"
    exit 0
}

New-Item -ItemType Directory -Path $toolDownloadsRoot -Force | Out-Null
if (-not (Test-ExpectedHash -Path $archivePath -ExpectedSha1 $nsisArchiveSha1)) {
    Download-VerifiedFile -Urls @(
        "https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip",
        "https://ghproxy.net/https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip"
    ) -Destination $archivePath -ExpectedSha1 $nsisArchiveSha1
}

foreach ($generatedPath in @($extractRoot, $nsisRoot)) {
    if (Test-Path -LiteralPath $generatedPath) {
        Remove-Item -LiteralPath $generatedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $generatedPath -Force | Out-Null
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
$extractedNsisRoot = Join-Path $extractRoot "nsis-3.11"
foreach ($entry in Get-ChildItem -LiteralPath $extractedNsisRoot -Force) {
    Copy-Item -LiteralPath $entry.FullName -Destination $nsisRoot -Recurse -Force
}

New-Item -ItemType Directory -Path (Split-Path -Parent $pluginPath) -Force | Out-Null
Download-VerifiedFile -Urls @(
    "https://github.com/tauri-apps/nsis-tauri-utils/releases/download/nsis_tauri_utils-v0.5.3/nsis_tauri_utils.dll",
    "https://ghproxy.net/https://github.com/tauri-apps/nsis-tauri-utils/releases/download/nsis_tauri_utils-v0.5.3/nsis_tauri_utils.dll"
) -Destination $pluginPath -ExpectedSha1 $tauriPluginSha1

if (-not (Test-Path -LiteralPath $makensisPath -PathType Leaf)) {
    throw "makensis.exe was not provisioned: $makensisPath"
}
Write-Host "Provisioned verified Tauri NSIS tools under: $nsisRoot"
