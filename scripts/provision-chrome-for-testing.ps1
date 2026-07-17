param(
    [string]$Channel = "Stable",
    [string]$Version = "",
    [ValidateSet("ChromeHeadlessShell", "Chrome")]
    [string]$Product = "ChromeHeadlessShell",
    [string]$Platform = "",
    [string]$DestinationRoot = "",
    [string]$CacheDir = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Main {
    $scriptRoot = $PSScriptRoot
    $repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

    if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
        $DestinationRoot = Join-Path $repoRoot "Browsers"
    }

    if ([string]::IsNullOrWhiteSpace($CacheDir)) {
        $CacheDir = Join-Path $repoRoot "artifacts\chrome-for-testing"
    }

    if ([string]::IsNullOrWhiteSpace($Platform)) {
        $Platform = Get-DefaultChromeForTestingPlatform
    }

    $productInfo = Get-ChromeForTestingProductInfo -Product $Product
    $destinationRootFullPath = [System.IO.Path]::GetFullPath($DestinationRoot)
    $cacheDirFullPath = [System.IO.Path]::GetFullPath($CacheDir)
    $repoRootFullPath = [System.IO.Path]::GetFullPath($repoRoot)

    Assert-InRepoPath -Path $destinationRootFullPath -Purpose "Chrome for Testing destination"
    Assert-InRepoPath -Path $cacheDirFullPath -Purpose "Chrome for Testing cache"

    New-Item -ItemType Directory -Path $cacheDirFullPath -Force | Out-Null
    New-Item -ItemType Directory -Path $destinationRootFullPath -Force | Out-Null

    $installRoot = Join-Path $destinationRootFullPath "ChromeForTesting\$Platform\$($productInfo.InstallDirectory)"
    $manifestPath = Join-Path $installRoot "chrome-for-testing.manifest.json"

    if (-not $Force -and (Test-Path -LiteralPath $manifestPath)) {
        $existingExecutable = Find-ChromeExecutable -Root $installRoot
        if (-not [string]::IsNullOrWhiteSpace($existingExecutable)) {
            Write-Host "$($productInfo.DisplayName) for Testing already exists:"
            Write-Host "  $existingExecutable"
            Write-Host "Use -Force to replace it."
            return
        }
    }

    $download = Get-ChromeDownload -Channel $Channel -Version $Version -Platform $Platform -ProductInfo $productInfo
    $zipPath = Join-Path $cacheDirFullPath ("{0}-for-testing-{1}-{2}.zip" -f $productInfo.CachePrefix, $download.Version, $Platform)

    if (-not (Test-Path -LiteralPath $zipPath)) {
        Write-Host "Downloading $($productInfo.DisplayName) for Testing:"
        Write-Host "  Version : $($download.Version)"
        Write-Host "  Platform: $Platform"
        Write-Host "  Url     : $($download.Url)"
        Write-Host "  Cache   : $zipPath"
        Download-File -Url $download.Url -DestinationPath $zipPath
    }
    else {
        Write-Host "Using cached $($productInfo.DisplayName) for Testing zip:"
        Write-Host "  $zipPath"
    }

    if (Test-Path -LiteralPath $installRoot) {
        Assert-InRepoPath -Path $installRoot -Purpose "Chrome for Testing install root"
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Write-Host "Extracting $($productInfo.DisplayName) for Testing:"
    Write-Host "  $installRoot"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $installRoot, $true)

    $executablePath = Find-ChromeExecutable -Root $installRoot
    if ([string]::IsNullOrWhiteSpace($executablePath)) {
        throw "$($productInfo.DisplayName) executable was not found after extraction under '$installRoot'."
    }
    Set-UnixExecutablePermission -Path $executablePath

    $manifest = [ordered]@{
        product = $Product
        version = $download.Version
        channel = $Channel
        platform = $Platform
        sourceUrl = $download.Url
        executablePath = $executablePath
        installedAt = [DateTimeOffset]::UtcNow.ToString("O")
        storagePolicy = "Installed under program-root Browsers directory; cache kept under repo artifacts. Not installed to system C drive application folders."
    }

    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Write-Host "$($productInfo.DisplayName) for Testing is ready:"
    Write-Host "  $executablePath"
}

function Get-ChromeForTestingProductInfo {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("ChromeHeadlessShell", "Chrome")]
        [string]$Product
    )

    if ($Product -eq "ChromeHeadlessShell") {
        return [PSCustomObject]@{
            DisplayName = "Chrome Headless Shell"
            MetadataDownloadKey = "chrome-headless-shell"
            InstallDirectory = "ChromeHeadlessShell"
            CachePrefix = "chrome-headless-shell"
        }
    }

    [PSCustomObject]@{
        DisplayName = "Chrome"
        MetadataDownloadKey = "chrome"
        InstallDirectory = "Chrome"
        CachePrefix = "chrome"
    }
}

function Get-DefaultChromeForTestingPlatform {
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        if ([Environment]::Is64BitOperatingSystem) {
            return "win64"
        }

        return "win32"
    }

    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) {
        return "linux64"
    }

    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) {
        if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
            return "mac-arm64"
        }

        return "mac-x64"
    }

    throw "Unsupported OS for Chrome for Testing auto platform detection. Pass -Platform explicitly."
}

function Get-ChromeDownload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Channel,

        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$Platform,

        [Parameter(Mandatory = $true)]
        [object]$ProductInfo
    )

    $metadataUri = if ([string]::IsNullOrWhiteSpace($Version)) {
        "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json"
    }
    else {
        "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json"
    }

    $metadataText = Download-Text -Url $metadataUri
    $metadata = $metadataText | ConvertFrom-Json -Depth 100

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $channelProperty = $metadata.channels.PSObject.Properties |
            Where-Object { $_.Name -ieq $Channel } |
            Select-Object -First 1

        if ($null -eq $channelProperty) {
            throw "Chrome for Testing channel '$Channel' was not found."
        }

        $selectedVersion = $channelProperty.Value
    }
    else {
        $selectedVersion = $metadata.versions |
            Where-Object { $_.version -eq $Version } |
            Select-Object -First 1

        if ($null -eq $selectedVersion) {
            throw "Chrome for Testing version '$Version' was not found."
        }
    }

    $downloadProperty = $selectedVersion.downloads.PSObject.Properties |
        Where-Object { $_.Name -eq $ProductInfo.MetadataDownloadKey } |
        Select-Object -First 1

    if ($null -eq $downloadProperty) {
        throw "$($ProductInfo.DisplayName) for Testing downloads were not found in version '$($selectedVersion.version)'."
    }

    $chromeDownload = $downloadProperty.Value |
        Where-Object { $_.platform -eq $Platform } |
        Select-Object -First 1

    if ($null -eq $chromeDownload) {
        throw "$($ProductInfo.DisplayName) for Testing download for platform '$Platform' was not found in version '$($selectedVersion.version)'."
    }

    [PSCustomObject]@{
        Version = $selectedVersion.version
        Url = $chromeDownload.url
    }
}

function Download-Text {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd("ExportDocManager-BuildTools/1.0")
        return $client.GetStringAsync($Url).GetAwaiter().GetResult()
    }
    finally {
        $client.Dispose()
    }
}

function Download-File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd("ExportDocManager-BuildTools/1.0")
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode() | Out-Null

        $parent = Split-Path -Parent $DestinationPath
        New-Item -ItemType Directory -Path $parent -Force | Out-Null

        $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $outputStream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $inputStream.CopyTo($outputStream)
        }
        finally {
            $outputStream.Dispose()
            $inputStream.Dispose()
            $response.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

function Find-ChromeExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    if (-not (Test-Path -LiteralPath $Root)) {
        return $null
    }

    $candidateNames = @(
        "chrome-headless-shell.exe",
        "chrome-headless-shell",
        "chrome.exe",
        "chrome",
        "Chromium",
        "Google Chrome for Testing"
    )

    foreach ($candidateName in $candidateNames) {
        $candidate = Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $candidateName } |
            Select-Object -First 1

        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Set-UnixExecutablePermission {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        return
    }

    $chmod = Get-Command chmod -ErrorAction SilentlyContinue
    if ($null -eq $chmod) {
        throw "chmod was not found; cannot make the bundled browser executable: $Path"
    }

    & $chmod.Source "+x" "--" $Path
    if ($LASTEXITCODE -ne 0) {
        throw "chmod failed for the bundled browser executable: $Path"
    }
}

function Assert-InRepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repoPrefix = $repoRootFullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change $Purpose outside repo: $fullPath"
    }
}

Invoke-Main
