[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("windows", "linux", "macos")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "osx-arm64")]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "x86_64-pc-windows-msvc",
        "x86_64-pc-windows-gnu",
        "x86_64-unknown-linux-gnu",
        "aarch64-unknown-linux-gnu",
        "aarch64-apple-darwin"
    )]
    [string]$RustTarget,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Architecture,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Document", "Sales", "Full")]
    [string]$Edition,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$CargoTargetRoot,

    [Parameter(Mandatory = $true)]
    [string]$ResourceRoot,

    [string]$OutputRoot,

    [switch]$SkipLaunchSmoke
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
. (Join-Path $PSScriptRoot "lib\build-script-support.ps1")

function Test-ChildPath {
    param([string]$Path, [string]$Root)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    return $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-GeneratedPath {
    param([string]$Path, [string]$Purpose)

    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-ChildPath -Path $resolved -Root $artifactsRoot)) {
        throw "$Purpose must stay inside '$artifactsRoot'. Resolved path: $resolved"
    }
    return $resolved
}

function Remove-GeneratedPath {
    param([string]$Path, [string]$Purpose)

    $resolved = Assert-GeneratedPath -Path $Path -Purpose $Purpose
    if (Test-Path -LiteralPath $resolved -PathType Container) {
        Remove-ExportDocDirectoryWithRetry `
            -Path $resolved `
            -AllowedRoot $artifactsRoot `
            -QuarantineRoot (Join-Path $artifactsRoot "runtime-cleanup-quarantine")
    } elseif (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Force -ErrorAction Stop
    }
}

function Copy-DirectoryContents {
    param([string]$Source, [string]$Destination)

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $Destination $entry.Name) -Recurse -Force
    }
}

function Invoke-NativeCommand {
    param([string]$Command, [string[]]$Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Write-PortableMarker {
    param([string]$Destination)

    [ordered]@{
        schemaVersion = 1
        mode = "portable"
        product = "ExportDocManager"
        version = $normalizedVersion
        edition = $Edition
        platform = $Platform
        architecture = $Architecture
        runtimeIdentifier = $RuntimeIdentifier
        dataRoot = "App_Data"
        storagePolicy = "Business data, configuration, logs, caches and backups stay under App_Data beside this portable package."
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Destination -Encoding utf8
}

function Write-ChecksumManifest {
    param([string]$PackageRoot)

    $manifestPath = Join-Path $PackageRoot "SHA256SUMS"
    $lines = foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -File -Recurse -Force |
        Where-Object FullName -ne $manifestPath |
        Sort-Object FullName) {
        $relativePath = [IO.Path]::GetRelativePath($PackageRoot, $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relativePath"
    }
    $lines | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

function Invoke-PayloadVerification {
    param([string]$PayloadRoot)

    & (Join-Path $PSScriptRoot "verify-package-payload.ps1") `
        -PackageRoot $PayloadRoot `
        -Profile Desktop `
        -RuntimeIdentifier $RuntimeIdentifier `
        -Edition $Edition
}

function Resolve-MacOsBundleExecutable {
    param([Parameter(Mandatory = $true)][string]$AppBundle)

    $infoPlist = Join-Path $AppBundle "Contents/Info.plist"
    if (-not (Test-Path -LiteralPath $infoPlist -PathType Leaf)) {
        throw "macOS portable app is missing Contents/Info.plist: $AppBundle"
    }

    $bundleExecutable = & /usr/libexec/PlistBuddy -c "Print :CFBundleExecutable" $infoPlist
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to read CFBundleExecutable from '$infoPlist'."
    }

    $executableName = ([string]($bundleExecutable | Select-Object -First 1)).Trim()
    if ([string]::IsNullOrWhiteSpace($executableName)) {
        throw "CFBundleExecutable is empty in '$infoPlist'."
    }

    $executablePath = Join-Path $AppBundle "Contents/MacOS/$executableName"
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "macOS portable executable was not found: $executablePath"
    }
    return $executablePath
}

$normalizedVersion = $Version.Trim().TrimStart("v", "V")
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$supportedTarget = switch ($Platform) {
    "windows" {
        $RuntimeIdentifier -eq "win-x64" -and
        $Architecture -eq "x64" -and
        $RustTarget -in @("x86_64-pc-windows-gnu", "x86_64-pc-windows-msvc")
    }
    "linux" {
        ($RuntimeIdentifier -eq "linux-x64" -and $Architecture -eq "x64" -and $RustTarget -eq "x86_64-unknown-linux-gnu") -or
        ($RuntimeIdentifier -eq "linux-arm64" -and $Architecture -eq "arm64" -and $RustTarget -eq "aarch64-unknown-linux-gnu")
    }
    "macos" {
        $RuntimeIdentifier -eq "osx-arm64" -and
        $Architecture -eq "arm64" -and
        $RustTarget -eq "aarch64-apple-darwin"
    }
}
if (-not $supportedTarget) {
    throw "Unsupported portable desktop target: Platform=$Platform RID=$RuntimeIdentifier Architecture=$Architecture"
}

$cargoTargetRoot = (Resolve-Path -LiteralPath $CargoTargetRoot).Path
$resourceRoot = (Resolve-Path -LiteralPath $ResourceRoot).Path
if (-not (Test-ChildPath -Path $cargoTargetRoot -Root $artifactsRoot)) {
    throw "CargoTargetRoot must stay inside '$artifactsRoot'. Resolved path: $cargoTargetRoot"
}
if (-not (Test-ChildPath -Path $resourceRoot -Root $artifactsRoot)) {
    throw "ResourceRoot must stay inside '$artifactsRoot'. Resolved path: $resourceRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $artifactsRoot "desktop-portable"
}
$outputRoot = Assert-GeneratedPath -Path $OutputRoot -Purpose "Portable desktop output"
$stagingParent = Assert-GeneratedPath -Path (Join-Path $outputRoot "staging") -Purpose "Portable staging root"
$packageOutputRoot = Assert-GeneratedPath -Path (Join-Path $outputRoot "packages") -Purpose "Portable archive output"
$inspectionParent = Assert-GeneratedPath -Path (Join-Path $outputRoot "inspection") -Purpose "Portable inspection root"

$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot "product-editions.json") -Raw -Encoding utf8 | ConvertFrom-Json
$editionMetadata = $catalog.editions.$Edition
if ($catalog.schemaVersion -ne 1 -or $null -eq $editionMetadata) {
    throw "Product edition metadata is missing or unsupported for $Edition."
}

$versionManifestPath = Join-Path $repoRoot "version.json"
$versionManifest = Get-Content -LiteralPath $versionManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
if ([string]$versionManifest.version -ne $normalizedVersion) {
    throw "Repository version '$($versionManifest.version)' does not match requested portable version '$normalizedVersion'."
}

$editionManifestPath = Join-Path $resourceRoot "product-edition.json"
$editionManifest = Get-Content -LiteralPath $editionManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($editionManifest.edition -ne $Edition -or $editionManifest.productVersion -ne $normalizedVersion) {
    throw "Packaged edition manifest does not match Edition=$Edition Version=$normalizedVersion."
}

$assetBaseName = "ExportDocManager-$Edition-$normalizedVersion-$Platform-$Architecture-portable"
$stagingRoot = Join-Path $stagingParent $assetBaseName
$inspectionRoot = Join-Path $inspectionParent $assetBaseName
Remove-GeneratedPath -Path $stagingRoot -Purpose "Portable staging cleanup"
Remove-GeneratedPath -Path $inspectionRoot -Purpose "Portable inspection cleanup"
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageOutputRoot -Force | Out-Null

$releaseRoot = Join-Path (Join-Path $cargoTargetRoot $RustTarget) "release"
$bundleRoot = Join-Path $releaseRoot "bundle"
$entryPoint = ""
$payloadVerificationRoot = $resourceRoot
$launchExecutablePath = $null
$portableRoot = $stagingRoot
$archiveExtension = if ($Platform -eq "windows") { ".zip" } else { ".tar.gz" }

switch ($Platform) {
    "windows" {
        $sourceExecutable = Join-Path $releaseRoot "export-doc-tauri.exe"
        if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
            throw "Windows Tauri executable was not found: $sourceExecutable"
        }
        $entryPoint = "ExportDocManager.exe"
        Copy-Item -LiteralPath $sourceExecutable -Destination (Join-Path $stagingRoot $entryPoint) -Force
        Copy-DirectoryContents -Source $resourceRoot -Destination $stagingRoot
        $payloadVerificationRoot = $stagingRoot
        $launchExecutablePath = Join-Path $stagingRoot $entryPoint
    }
    "linux" {
        $appImageRoot = Join-Path $bundleRoot "appimage"
        $appImages = @(Get-ChildItem -LiteralPath $appImageRoot -File -Filter "*.AppImage")
        if ($appImages.Count -ne 1) {
            throw "Expected exactly one Linux AppImage under '$appImageRoot', found $($appImages.Count)."
        }
        $entryPoint = "$assetBaseName.AppImage"
        $portableAppImage = Join-Path $stagingRoot $entryPoint
        Copy-Item -LiteralPath $appImages[0].FullName -Destination $portableAppImage -Force
        Invoke-NativeCommand -Command "chmod" -Arguments @("+x", $portableAppImage)

        New-Item -ItemType Directory -Path $inspectionRoot -Force | Out-Null
        Push-Location $inspectionRoot
        try {
            Invoke-NativeCommand -Command $portableAppImage -Arguments @("--appimage-extract")
        }
        finally {
            Pop-Location
        }
        $layoutManifests = @(Get-ChildItem -LiteralPath (Join-Path $inspectionRoot "squashfs-root") -File -Recurse -Filter "runtime-layout.json")
        if ($layoutManifests.Count -ne 1) {
            throw "Expected exactly one runtime-layout.json inside the AppImage, found $($layoutManifests.Count)."
        }
        $payloadVerificationRoot = $layoutManifests[0].Directory.FullName
        $launchExecutablePath = Join-Path (Join-Path $inspectionRoot "squashfs-root") "AppRun"
        if (-not (Test-Path -LiteralPath $launchExecutablePath -PathType Leaf)) {
            throw "Extracted Linux AppImage is missing AppRun: $launchExecutablePath"
        }
        Invoke-NativeCommand -Command "chmod" -Arguments @("+x", $launchExecutablePath)
    }
    "macos" {
        $macOsBundleRoot = Join-Path $bundleRoot "macos"
        $appBundles = @(Get-ChildItem -LiteralPath $macOsBundleRoot -Directory -Filter "*.app")
        if ($appBundles.Count -ne 1) {
            throw "Expected exactly one macOS app bundle under '$macOsBundleRoot', found $($appBundles.Count)."
        }
        $entryPoint = $appBundles[0].Name
        $portableAppBundle = Join-Path $stagingRoot $entryPoint
        Invoke-NativeCommand -Command "ditto" -Arguments @($appBundles[0].FullName, $portableAppBundle)
        $payloadVerificationRoot = Join-Path $portableAppBundle "Contents/Resources"
        if (-not (Test-Path -LiteralPath (Join-Path $payloadVerificationRoot "runtime-layout.json") -PathType Leaf)) {
            throw "macOS portable app is missing Contents/Resources/runtime-layout.json."
        }
        $launchExecutablePath = Resolve-MacOsBundleExecutable -AppBundle $portableAppBundle
    }
}

Write-PortableMarker -Destination (Join-Path $stagingRoot "portable-runtime.json")
Copy-Item -LiteralPath $versionManifestPath -Destination (Join-Path $stagingRoot "version.json") -Force

[ordered]@{
    schemaVersion = 1
    product = "ExportDocManager"
    productName = [string]$editionMetadata.productName
    edition = $Edition
    version = $normalizedVersion
    platform = $Platform
    architecture = $Architecture
    runtimeIdentifier = $RuntimeIdentifier
    entryPoint = $entryPoint
    archiveFormat = $archiveExtension.TrimStart('.')
    selfContainedApi = $true
    bundledReportBrowser = [bool]$editionMetadata.resourceProfile.browserRenderer
    systemSigned = $false
    notarized = $false
    dataRoot = "App_Data"
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stagingRoot "portable-package.json") -Encoding utf8

$launchInstruction = switch ($Platform) {
    "windows" { "双击 ExportDocManager.exe。" }
    "linux" { "运行 ./$entryPoint；若系统没有 FUSE，可在本次启动前设置 APPIMAGE_EXTRACT_AND_RUN=1。" }
    "macos" { "打开 $entryPoint；当前阶段按要求未执行 Developer ID 签名或 Apple 公证。" }
}
@"
ExportDocManager 绿色便携版

产品版：$($editionMetadata.displayName)
版本：$normalizedVersion
平台：$Platform $Architecture
启动：$launchInstruction

无需安装。首次启动会在解包目录旁创建 App_Data。
备份或迁移时，请先退出程序，再复制完整解包目录。
本交付包不预置数据库、密码、许可证、日志或用户配置。
"@ | Set-Content -LiteralPath (Join-Path $stagingRoot "PORTABLE_README.txt") -Encoding utf8

Invoke-PayloadVerification -PayloadRoot $payloadVerificationRoot

if (-not $SkipLaunchSmoke) {
    $portableDataRoot = Join-Path $stagingRoot "App_Data"
    try {
        $smokeArguments = @{
            ExecutablePath = $launchExecutablePath
            AppRoot = $payloadVerificationRoot
            PortableRoot = $portableRoot
            UsePortableDataRoot = $true
            SkipVite = $true
            TimeoutSeconds = 60
        }
        if ($Platform -eq "windows") {
            $smokeArguments.UseDefaultAppRoot = $true
        }
        & (Join-Path $PSScriptRoot "smoke-tauri-desktop.ps1") @smokeArguments
    } finally {
        Remove-GeneratedPath -Path $portableDataRoot -Purpose "Portable launch smoke data cleanup"
    }
}

Remove-GeneratedPath -Path $inspectionRoot -Purpose "Portable inspection cleanup"

if (Test-Path -LiteralPath (Join-Path $stagingRoot "App_Data")) {
    throw "Portable release staging must not contain App_Data."
}
Write-ChecksumManifest -PackageRoot $stagingRoot

$archivePath = Join-Path $packageOutputRoot "$assetBaseName$archiveExtension"
$archiveHashPath = "$archivePath.sha256"
Remove-GeneratedPath -Path $archivePath -Purpose "Portable archive replacement"
Remove-GeneratedPath -Path $archiveHashPath -Purpose "Portable archive hash replacement"

if ($Platform -eq "windows") {
    Compress-Archive -LiteralPath $stagingRoot -DestinationPath $archivePath -CompressionLevel Optimal
}
else {
    Invoke-NativeCommand -Command "tar" -Arguments @(
        "-czf", $archivePath,
        "-C", (Split-Path -Parent $stagingRoot),
        (Split-Path -Leaf $stagingRoot)
    )
}

$archive = Get-Item -LiteralPath $archivePath
if ($archive.Length -le 0) {
    throw "Portable archive is empty: $archivePath"
}
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash *$($archive.Name)" | Set-Content -LiteralPath $archiveHashPath -Encoding ascii
Remove-GeneratedPath -Path $stagingRoot -Purpose "Completed portable staging cleanup"

[ordered]@{
    archive = $archive.FullName
    sha256 = $archiveHash
    hashFile = (Get-Item -LiteralPath $archiveHashPath).FullName
    entryPoint = $entryPoint
    payloadProfile = "Desktop/$RuntimeIdentifier"
    stagingCleaned = $true
} | ConvertTo-Json -Depth 4
