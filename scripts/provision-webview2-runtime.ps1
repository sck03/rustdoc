[CmdletBinding()]
param(
    [string]$SourceInstaller,
    [string]$DestinationDirectory,
    [switch]$Refresh
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
. (Join-Path $scriptRoot "lib\webview2-runtime-support.ps1")

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "WebView2 Runtime provisioning is supported only on Windows."
}

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $repoRoot "WebView2Runtime"
}
$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
$sourceManifestPath = Join-Path $repoRoot "WebView2Runtime\webview2-runtime.json"
if (-not (Test-Path -LiteralPath $sourceManifestPath -PathType Leaf)) {
    throw "The pinned WebView2 Runtime release manifest was not found: $sourceManifestPath"
}

$release = Read-ExportDocWebView2Release -ManifestPath $sourceManifestPath
$fileName = [string]$release.fileName
$officialUrl = [string]$release.sourceUrl

$manifestPath = Join-Path $destinationRoot "webview2-runtime.json"
$installerPath = Join-Path $destinationRoot $fileName

New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
$resolvedSourceManifestPath = [IO.Path]::GetFullPath($sourceManifestPath)
$resolvedManifestPath = [IO.Path]::GetFullPath($manifestPath)
if (-not [string]::Equals($resolvedSourceManifestPath, $resolvedManifestPath, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $resolvedSourceManifestPath -Destination $resolvedManifestPath -Force
}
if ([string]::IsNullOrWhiteSpace($SourceInstaller)) {
    $SourceInstaller = $env:EXPORTDOCMANAGER_WEBVIEW2_RUNTIME_INSTALLER
}

if (-not [string]::IsNullOrWhiteSpace($SourceInstaller)) {
    $sourcePath = (Resolve-Path -LiteralPath $SourceInstaller).Path
    Assert-ExportDocWebView2Installer -Path $sourcePath -Release $release | Out-Null
    $resolvedInstallerPath = [IO.Path]::GetFullPath($installerPath)
    if (-not [string]::Equals($sourcePath, $resolvedInstallerPath, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $sourcePath -Destination $installerPath -Force
    }
} elseif ($Refresh -or -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    $downloadRoot = Join-Path $artifactsRoot "tool-downloads\webview2"
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    $downloadPath = Join-Path $downloadRoot "$fileName.download"
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    Invoke-ExportDocExternal `
        -FilePath "curl.exe" `
        -Arguments @("-L", "--fail", "--connect-timeout", "20", "--max-time", "900", "--retry", "2", "--retry-delay", "2", "-o", $downloadPath, $officialUrl) `
        -DisplayName "Download Microsoft WebView2 Evergreen Standalone Installer x64" `
        -TimeoutSeconds 960 `
        -HeartbeatSeconds 20
    Assert-ExportDocWebView2Installer -Path $downloadPath -Release $release | Out-Null
    Move-Item -LiteralPath $downloadPath -Destination $installerPath -Force
}

$verified = Assert-ExportDocWebView2Installer -Path $installerPath -Release $release

Write-Host "Verified Microsoft WebView2 Runtime installer:"
Write-Host "  $installerPath"
Write-Host "  version=$($verified.VersionInfo.FileVersion) bytes=$($verified.File.Length) sha256=$($verified.Hash)"
