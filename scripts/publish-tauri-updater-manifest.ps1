[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("windows", "macos", "linux")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Architecture,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$BundleRoot,

    [string]$Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw "GH_TOKEN is required to publish the updater manifest."
}
if ([string]::IsNullOrWhiteSpace($Repository) -or $Repository -notmatch '^[^/]+/[^/]+$') {
    throw "Repository must use owner/name format."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$resolvedBundleRoot = (Resolve-Path -LiteralPath $BundleRoot).Path
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
if (-not $resolvedBundleRoot.StartsWith($allowedRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "BundleRoot must stay inside $allowedRoot. Resolved path: $resolvedBundleRoot"
}

$signaturePattern = switch ($Platform) {
    "windows" { "*-setup.exe.sig" }
    "linux" { "*.AppImage.sig" }
    "macos" { "*.app.tar.gz.sig" }
}
$signatures = @(Get-ChildItem -LiteralPath $resolvedBundleRoot -File -Recurse -Filter $signaturePattern)
if ($signatures.Count -ne 1) {
    throw "Expected one updater signature matching '$signaturePattern', found $($signatures.Count)."
}

$signature = $signatures[0]
$packagePath = $signature.FullName.Substring(0, $signature.FullName.Length - 4)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Updater package corresponding to the signature was not found: $packagePath"
}
$package = Get-Item -LiteralPath $packagePath
$signatureContent = (Get-Content -LiteralPath $signature.FullName -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($signatureContent)) {
    throw "Updater signature is empty: $($signature.FullName)"
}

$normalizedVersion = $Version.Trim().TrimStart("v", "V")
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}
$tag = "v$normalizedVersion"
$targetOs = if ($Platform -eq "macos") { "darwin" } else { $Platform }
$targetArch = if ($Architecture -eq "arm64") { "aarch64" } else { "x86_64" }
$target = "$targetOs-$targetArch"

& gh release upload $tag $package.FullName $signature.FullName --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload updater package and signature to release $tag."
}

$manifestRoot = Join-Path $allowedRoot "tauri-updater-manifest"
$manifestPath = Join-Path $manifestRoot "latest.json"
New-Item -ItemType Directory -Path $manifestRoot -Force | Out-Null
Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue

& gh release download $tag --repo $Repository --pattern latest.json --dir $manifestRoot --clobber 2>$null
$downloadedExistingManifest = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)
if ($downloadedExistingManifest) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    if ([string]$manifest.version -ne $normalizedVersion) {
        $manifest = $null
    }
}
if ($null -eq $manifest) {
    $manifest = [ordered]@{
        version = $normalizedVersion
        notes = ""
        pub_date = (Get-Date).ToUniversalTime().ToString("o")
        platforms = [ordered]@{}
    }
}
if ($manifest.platforms -isnot [System.Collections.IDictionary]) {
    $manifest.platforms = [ordered]@{}
}

$releaseNotes = (& gh release view $tag --repo $Repository --json body --jq .body)
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($releaseNotes)) {
    $manifest.notes = ($releaseNotes -join [Environment]::NewLine).Trim()
}
$manifest.version = $normalizedVersion
$manifest.pub_date = (Get-Date).ToUniversalTime().ToString("o")
$assetName = [System.Uri]::EscapeDataString($package.Name)
$manifest.platforms[$target] = [ordered]@{
    signature = $signatureContent
    url = "https://github.com/$Repository/releases/download/$tag/$assetName"
}

foreach ($entry in $manifest.platforms.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.Value.signature) -or
        [string]::IsNullOrWhiteSpace([string]$entry.Value.url)) {
        throw "Updater manifest contains an incomplete platform entry: $($entry.Key)"
    }
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
& gh release upload $tag $manifestPath --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload latest.json to release $tag."
}

[pscustomobject]@{
    Success = $true
    Version = $normalizedVersion
    Target = $target
    Package = $package.FullName
    Signature = $signature.FullName
    Manifest = $manifestPath
    ExistingManifestMerged = $downloadedExistingManifest
} | ConvertTo-Json -Depth 4
