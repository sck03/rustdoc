[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("windows", "macos", "linux")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Architecture,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Document", "Sales", "Full")]
    [string]$Edition,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$BundleRoot,

    [string]$PortableAssetRoot,

    [string]$Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw "GH_TOKEN is required to publish desktop release assets."
}
if ([string]::IsNullOrWhiteSpace($Repository) -or $Repository -notmatch '^[^/]+/[^/]+$') {
    throw "Repository must use owner/name format."
}

function Invoke-PublishDesktopRelease {
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$resolvedBundleRoot = (Resolve-Path -LiteralPath $BundleRoot).Path
if (-not $resolvedBundleRoot.StartsWith(
        $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "BundleRoot must stay inside $artifactsRoot. Resolved path: $resolvedBundleRoot"
}
$resolvedPortableAssetRoot = $null
if (-not [string]::IsNullOrWhiteSpace($PortableAssetRoot)) {
    $resolvedPortableAssetRoot = (Resolve-Path -LiteralPath $PortableAssetRoot).Path
    if (-not $resolvedPortableAssetRoot.StartsWith(
            $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "PortableAssetRoot must stay inside $artifactsRoot. Resolved path: $resolvedPortableAssetRoot"
    }
}

$catalogPath = Join-Path $PSScriptRoot "product-editions.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1) {
    throw "Unsupported product edition metadata schema: $($catalog.schemaVersion)"
}
$metadata = $catalog.editions.$Edition
if ($null -eq $metadata) {
    throw "Product edition metadata is missing for $Edition."
}

$normalizedVersion = $Version.Trim().TrimStart("v", "V")
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}
$isPrerelease = $normalizedVersion.Contains("-")
$releaseTag = "$($metadata.releaseTagPrefix)-v$normalizedVersion"
$channelTag = if ($isPrerelease) { [string]$metadata.prereleaseChannelTag } else { [string]$metadata.stableChannelTag }
$channelManifestName = if ($isPrerelease) { [string]$metadata.prereleaseManifestAsset } else { [string]$metadata.stableManifestAsset }
$targetOs = if ($Platform -eq "macos") { "darwin" } else { $Platform }
$targetArch = if ($Architecture -eq "arm64") { "aarch64" } else { "x86_64" }
$target = "$targetOs-$targetArch"

$signaturePattern = switch ($Platform) {
    "windows" { "*-setup.exe.sig" }
    "linux" { "*.AppImage.sig" }
    "macos" { "*.app.tar.gz.sig" }
}
$signatures = @(Get-ChildItem -LiteralPath $resolvedBundleRoot -File -Recurse -Filter $signaturePattern |
    Where-Object { $_.FullName -match '[\\/]release[\\/]bundle[\\/]' })
if ($signatures.Count -ne 1) {
    throw "Expected one updater signature matching '$signaturePattern', found $($signatures.Count)."
}
$updaterSignature = $signatures[0]
$updaterPackagePath = $updaterSignature.FullName.Substring(0, $updaterSignature.FullName.Length - 4)
if (-not (Test-Path -LiteralPath $updaterPackagePath -PathType Leaf)) {
    throw "Updater package corresponding to the signature was not found: $updaterPackagePath"
}
$signatureContent = (Get-Content -LiteralPath $updaterSignature.FullName -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($signatureContent)) {
    throw "Updater signature is empty: $($updaterSignature.FullName)"
}

$bundleFiles = @(Get-ChildItem -LiteralPath $resolvedBundleRoot -File -Recurse |
    Where-Object {
        $_.FullName -match '[\\/]release[\\/]bundle[\\/]' -and
        (Test-ReleaseAssetName -Platform $Platform -Name $_.Name)
    })
if ($bundleFiles.Count -eq 0) {
    throw "No publishable $Platform desktop bundle files were found."
}

$stagingRoot = Join-Path $artifactsRoot "desktop-release-assets\$($metadata.slug)\$normalizedVersion\$Platform-$Architecture"
$fullStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
if (-not $fullStagingRoot.StartsWith(
        $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release staging path escaped artifacts: $fullStagingRoot"
}
if (Test-Path -LiteralPath $fullStagingRoot) {
    Remove-Item -LiteralPath $fullStagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $fullStagingRoot -Force | Out-Null

$assetBaseName = "ExportDocManager-$Edition-$normalizedVersion-$Platform-$Architecture"
$stagedAssets = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$assetNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$updaterPackageAsset = $null
foreach ($bundleFile in $bundleFiles) {
    $suffix = Get-ReleaseAssetSuffix -Name $bundleFile.Name
    $assetName = "$assetBaseName$suffix"
    if (-not $assetNames.Add($assetName)) {
        throw "Multiple bundle files map to the same release asset '$assetName'."
    }
    $destination = Join-Path $fullStagingRoot $assetName
    Copy-Item -LiteralPath $bundleFile.FullName -Destination $destination
    $staged = Get-Item -LiteralPath $destination
    $stagedAssets.Add($staged)
    if ([string]::Equals($bundleFile.FullName, $updaterPackagePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $updaterPackageAsset = $staged
    }
}
if ($null -eq $updaterPackageAsset) {
    throw "The updater package was not included in the staged release assets."
}

$portableAssets = New-Object System.Collections.Generic.List[System.IO.FileInfo]
if ($null -ne $resolvedPortableAssetRoot) {
    $portableArchiveSuffix = if ($Platform -eq "windows") { ".zip" } else { ".tar.gz" }
    $portableArchiveName = "$assetBaseName-portable$portableArchiveSuffix"
    $portableHashName = "$portableArchiveName.sha256"
    $portableArchivePath = Join-Path $resolvedPortableAssetRoot $portableArchiveName
    $portableHashPath = Join-Path $resolvedPortableAssetRoot $portableHashName
    foreach ($portablePath in @($portableArchivePath, $portableHashPath)) {
        if (-not (Test-Path -LiteralPath $portablePath -PathType Leaf) -or (Get-Item -LiteralPath $portablePath).Length -le 0) {
            throw "Portable release asset is missing or empty: $portablePath"
        }
    }

    $declaredPortableHash = ((Get-Content -LiteralPath $portableHashPath -Raw -Encoding ascii).Trim() -split '\s+')[0]
    $actualPortableHash = (Get-FileHash -LiteralPath $portableArchivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($declaredPortableHash, $actualPortableHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable release archive SHA-256 mismatch for '$portableArchiveName'."
    }

    foreach ($portablePath in @($portableArchivePath, $portableHashPath)) {
        $portableName = Split-Path -Leaf $portablePath
        if (-not $assetNames.Add($portableName)) {
            throw "Duplicate release asset name '$portableName'."
        }
        $destination = Join-Path $fullStagingRoot $portableName
        Copy-Item -LiteralPath $portablePath -Destination $destination
        $stagedPortableAsset = Get-Item -LiteralPath $destination
        $stagedAssets.Add($stagedPortableAsset)
        $portableAssets.Add($stagedPortableAsset)
    }
}

Ensure-Release -Tag $releaseTag -Title "$($metadata.productName) $normalizedVersion" -Notes "$($metadata.productName) $normalizedVersion 已生成 Tauri 更新签名；Windows Authenticode 与 macOS Developer ID/公证将在正式商业发布前另行启用。" -Prerelease:$isPrerelease
$releaseNotes = (& gh release view $releaseTag --repo $Repository --json body --jq .body)
if ($LASTEXITCODE -ne 0) {
    throw "Failed to read release notes for $releaseTag."
}

$versionManifestName = "update-$($metadata.slug)-$normalizedVersion-$Platform-$Architecture.json"
$versionManifestPath = Join-Path $fullStagingRoot $versionManifestName
$versionManifest = New-UpdaterManifest -Version $normalizedVersion -Notes (($releaseNotes -join [Environment]::NewLine).Trim())
$versionManifest.platforms[$target] = [ordered]@{
    signature = $signatureContent
    url = "https://github.com/$Repository/releases/download/$releaseTag/$([System.Uri]::EscapeDataString($updaterPackageAsset.Name))"
}
$versionManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $versionManifestPath -Encoding UTF8
$stagedAssets.Add((Get-Item -LiteralPath $versionManifestPath))

$existingReleaseAssets = @(& gh release view $releaseTag --repo $Repository --json assets --jq '.assets[].name')
if ($LASTEXITCODE -ne 0) {
    throw "Failed to inspect existing assets for release $releaseTag."
}
$conflicts = @($stagedAssets | Where-Object { $existingReleaseAssets -contains $_.Name })
if ($conflicts.Count -gt 0) {
    throw "Immutable release $releaseTag already contains asset(s): $($conflicts.Name -join ', '). Delete the incorrect release explicitly instead of overwriting assets."
}

$uploadArguments = @("release", "upload", $releaseTag)
$uploadArguments += @($stagedAssets.FullName)
$uploadArguments += @("--repo", $Repository)
& gh @uploadArguments
if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload immutable desktop assets to release $releaseTag."
}

Ensure-Release -Tag $channelTag -Title "$($metadata.productName) 更新通道" -Notes "该 Release 仅承载经过签名的更新通道清单；版本安装包保存在不可覆盖的版本 Release。" -Prerelease
$channelWorkRoot = Join-Path $fullStagingRoot "channel"
New-Item -ItemType Directory -Path $channelWorkRoot -Force | Out-Null
$channelManifestPath = Join-Path $channelWorkRoot $channelManifestName
Remove-Item -LiteralPath $channelManifestPath -Force -ErrorAction SilentlyContinue

& gh release download $channelTag --repo $Repository --pattern $channelManifestName --dir $channelWorkRoot --clobber 2>$null
$hasExistingChannelManifest = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $channelManifestPath -PathType Leaf)
$channelManifest = $null
if ($hasExistingChannelManifest) {
    $channelManifest = Get-Content -LiteralPath $channelManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    $existingVersion = [System.Management.Automation.SemanticVersion]::Parse([string]$channelManifest.version)
    $requestedVersion = [System.Management.Automation.SemanticVersion]::Parse($normalizedVersion)
    if ($existingVersion -gt $requestedVersion) {
        throw "Update channel $channelTag already points to newer version $existingVersion; refusing rollback to $requestedVersion."
    }
    if ($existingVersion -lt $requestedVersion) {
        $channelManifest = $null
    }
}
if ($null -eq $channelManifest) {
    $channelManifest = New-UpdaterManifest -Version $normalizedVersion -Notes (($releaseNotes -join [Environment]::NewLine).Trim())
}
if ($channelManifest.platforms -isnot [System.Collections.IDictionary]) {
    $channelManifest.platforms = [ordered]@{}
}
$channelManifest.version = $normalizedVersion
$channelManifest.notes = (($releaseNotes -join [Environment]::NewLine).Trim())
$channelManifest.pub_date = (Get-Date).ToUniversalTime().ToString("o")
$channelManifest.platforms[$target] = $versionManifest.platforms[$target]
foreach ($entry in $channelManifest.platforms.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.Value.signature) -or
        [string]::IsNullOrWhiteSpace([string]$entry.Value.url)) {
        throw "Updater channel manifest contains an incomplete platform entry: $($entry.Key)"
    }
}
$channelManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $channelManifestPath -Encoding UTF8
Publish-ChannelManifestAtomically `
    -Tag $channelTag `
    -ManifestPath $channelManifestPath `
    -AssetName $channelManifestName `
    -PublishedVersion $normalizedVersion `
    -PlatformTarget $target

[pscustomobject]@{
    Success = $true
    Edition = $Edition
    Version = $normalizedVersion
    ReleaseTag = $releaseTag
    ChannelTag = $channelTag
    ChannelManifest = $channelManifestName
    Target = $target
    UpdaterPackageAsset = $updaterPackageAsset.Name
    PortableAssets = @($portableAssets.Name)
    ReleaseAssets = @($stagedAssets.Name)
} | ConvertTo-Json -Depth 5
}

function Test-ReleaseAssetName {
    param([string]$Platform, [string]$Name)
    switch ($Platform) {
        "windows" { return $Name -match '(?i)(-setup\.exe(?:\.sig)?|\.msi(?:\.sig)?)$' }
        "linux" { return $Name -match '(?i)(\.AppImage(?:\.sig)?|\.deb|\.rpm)$' }
        "macos" { return $Name -match '(?i)(\.app\.tar\.gz(?:\.sig)?|\.dmg|\.pkg)$' }
    }
    return $false
}

function Get-ReleaseAssetSuffix {
    param([string]$Name)
    foreach ($suffix in @(
            ".app.tar.gz.sig",
            "-setup.exe.sig",
            ".AppImage.sig",
            ".app.tar.gz",
            "-setup.exe",
            ".AppImage",
            ".msi.sig",
            ".msi",
            ".dmg",
            ".pkg",
            ".deb",
            ".rpm")) {
        if ($Name.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $suffix
        }
    }
    throw "Unsupported desktop release asset name: $Name"
}

function New-UpdaterManifest {
    param([string]$Version, [string]$Notes)
    return [ordered]@{
        version = $Version
        notes = $Notes
        pub_date = (Get-Date).ToUniversalTime().ToString("o")
        platforms = [ordered]@{}
    }
}

function Ensure-Release {
    param(
        [string]$Tag,
        [string]$Title,
        [string]$Notes,
        [switch]$Prerelease
    )
    & gh release view $Tag --repo $Repository *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }
    $arguments = @("release", "create", $Tag, "--repo", $Repository, "--title", $Title, "--notes", $Notes)
    if ($Prerelease) {
        $arguments += "--prerelease"
    }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create GitHub Release $Tag."
    }
}

function Get-ReleaseByTag {
    param([string]$Tag)
    $json = & gh api "repos/$Repository/releases/tags/$Tag"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to inspect GitHub Release $Tag."
    }
    return ($json -join [Environment]::NewLine) | ConvertFrom-Json
}

function Publish-ChannelManifestAtomically {
    param(
        [string]$Tag,
        [string]$ManifestPath,
        [string]$AssetName,
        [string]$PublishedVersion,
        [string]$PlatformTarget
    )
    $temporaryName = "$AssetName.next-$PublishedVersion-$PlatformTarget-$([Guid]::NewGuid().ToString('N'))"
    $temporaryPath = Join-Path (Split-Path -Parent $ManifestPath) $temporaryName
    Copy-Item -LiteralPath $ManifestPath -Destination $temporaryPath
    $temporaryAssetId = $null
    try {
        & gh release upload $Tag $temporaryPath --repo $Repository
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload temporary update channel manifest to $Tag."
        }

        $release = Get-ReleaseByTag -Tag $Tag
        $temporaryAsset = @($release.assets | Where-Object name -eq $temporaryName)
        if ($temporaryAsset.Count -ne 1) {
            throw "Temporary update channel asset could not be resolved after upload: $temporaryName"
        }
        $temporaryAssetId = [long]$temporaryAsset[0].id
        $existingAsset = @($release.assets | Where-Object name -eq $AssetName)
        if ($existingAsset.Count -gt 1) {
            throw "Update channel contains duplicate manifest assets named $AssetName."
        }
        if ($existingAsset.Count -eq 1) {
            & gh api --method DELETE "repos/$Repository/releases/assets/$([long]$existingAsset[0].id)"
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to remove the previous audited channel manifest $AssetName."
            }
        }

        & gh api --method PATCH "repos/$Repository/releases/assets/$temporaryAssetId" -f "name=$AssetName" *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to promote the new update channel manifest to $AssetName."
        }
        $temporaryAssetId = $null

        $verificationRoot = Join-Path (Split-Path -Parent $ManifestPath) "verify"
        New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
        $verifiedPath = Join-Path $verificationRoot $AssetName
        Remove-Item -LiteralPath $verifiedPath -Force -ErrorAction SilentlyContinue
        & gh release download $Tag --repo $Repository --pattern $AssetName --dir $verificationRoot --clobber
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $verifiedPath -PathType Leaf)) {
            throw "Failed to download the promoted update channel manifest for verification."
        }
        $localHash = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath $verifiedPath -Algorithm SHA256).Hash
        if ($localHash -ne $remoteHash) {
            throw "Promoted update channel manifest SHA-256 verification failed."
        }
    }
    finally {
        if ($null -ne $temporaryAssetId) {
            & gh api --method DELETE "repos/$Repository/releases/assets/$temporaryAssetId" *> $null
        }
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

Invoke-PublishDesktopRelease
