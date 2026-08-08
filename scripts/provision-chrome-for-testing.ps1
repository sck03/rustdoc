param(
    [string]$Channel = "",
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

    if ($Platform.StartsWith("mac-", [StringComparison]::OrdinalIgnoreCase) -and
        -not $Platform.Equals("mac-arm64", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Intel macOS is retired; Chrome for Testing provisioning only supports Apple Silicon ARM64."
    }

    $productInfo = Get-ChromeForTestingProductInfo -Product $Product
    $playwrightRuntime = Get-PlaywrightRuntimeMetadata -RepositoryRoot $repoRoot
    $versionWasExplicit = -not [string]::IsNullOrWhiteSpace($Version)
    $channelWasExplicit = -not [string]::IsNullOrWhiteSpace($Channel)
    if (-not $versionWasExplicit -and -not $channelWasExplicit) {
        $Version = $playwrightRuntime.ChromiumVersion
    }
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

    $playwrightCache = $null
    if ($Version -eq $playwrightRuntime.ChromiumVersion) {
        $playwrightCache = Get-CompatiblePlaywrightBrowserCache `
            -RepositoryRoot $repoRoot `
            -ProductInfo $productInfo `
            -Revision $playwrightRuntime.ChromiumRevision
    }

    if ($null -ne $playwrightCache) {
        $download = [PSCustomObject]@{
            Version = $playwrightRuntime.ChromiumVersion
            Url = "https://playwright.dev/dotnet/docs/browsers"
            PayloadRoot = $playwrightCache.PayloadRoot
            SourceKind = "PlaywrightLocalCache"
        }
    }
    else {
        $download = Get-ChromeDownload -Channel $Channel -Version $Version -Platform $Platform -ProductInfo $productInfo
    }

    $zipPath = Join-Path $cacheDirFullPath ("{0}-for-testing-{1}-{2}.zip" -f $productInfo.CachePrefix, $download.Version, $Platform)
    if ([string]::IsNullOrWhiteSpace($download.PayloadRoot)) {
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
    }
    else {
        Write-Host "Using Playwright-compatible browser cache:"
        Write-Host "  $($download.PayloadRoot)"
    }

    if (Test-Path -LiteralPath $installRoot) {
        Assert-InRepoPath -Path $installRoot -Purpose "Chrome for Testing install root"
        Assert-NoRunningBrowserFromRoot -Root $installRoot
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Write-Host "Staging $($productInfo.DisplayName) for Testing:"
    Write-Host "  $installRoot"
    if ([string]::IsNullOrWhiteSpace($download.PayloadRoot)) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $installRoot, $true)
    }
    else {
        Copy-Item -LiteralPath $download.PayloadRoot -Destination $installRoot -Recurse -Force
    }

    $executablePath = Find-ChromeExecutable -Root $installRoot
    if ([string]::IsNullOrWhiteSpace($executablePath)) {
        throw "$($productInfo.DisplayName) executable was not found after extraction under '$installRoot'."
    }
    Set-UnixExecutablePermission -Path $executablePath
    $versionOutput = Test-ChromeExecutable -Path $executablePath

    $manifest = [ordered]@{
        product = $Product
        version = $download.Version
        channel = if ($channelWasExplicit) { $Channel } elseif ($versionWasExplicit) { "ExplicitVersion" } else { "PlaywrightCompatible" }
        platform = $Platform
        sourceUrl = $download.Url
        executablePath = $executablePath
        playwrightPackageVersion = $playwrightRuntime.PackageVersion
        playwrightChromiumVersion = $playwrightRuntime.ChromiumVersion
        playwrightChromiumRevision = $playwrightRuntime.ChromiumRevision
        playwrightCompatible = $download.Version -eq $playwrightRuntime.ChromiumVersion
        sourceKind = $download.SourceKind
        validatedVersionOutput = $versionOutput
        installedAt = [DateTimeOffset]::UtcNow.ToString("O")
        storagePolicy = "Installed under program-root Browsers directory; cache kept under repo artifacts. Not installed to system C drive application folders."
        compatibilityPolicy = "Default provisioning pins Chrome for Testing to the Chromium version declared for Microsoft.Playwright in Directory.Packages.props. Pass -Channel or -Version only for explicit compatibility diagnostics."
    }

    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Write-Host "$($productInfo.DisplayName) for Testing is ready:"
    Write-Host "  $executablePath"
}

function Assert-NoRunningBrowserFromRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        return
    }

    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $running = @(Get-Process -Name "chrome", "chrome-headless-shell" -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                -not [string]::IsNullOrWhiteSpace($_.Path) -and
                    [IO.Path]::GetFullPath($_.Path).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        })
    if ($running.Count -gt 0) {
        throw "Refusing to replace a browser runtime while its process is running: $($running.Id -join ', '). Close ExportDocManager and retry."
    }
}

function Test-ChromeExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $Path
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.ErrorDialog = $false
    $process.StartInfo.ArgumentList.Add("--version")
    try {
        if (-not $process.Start()) {
            throw "Browser executable did not start."
        }
        if (-not $process.WaitForExit(15000)) {
            $process.Kill($true)
            throw "Browser executable version check timed out."
        }

        [string]$output = ($process.StandardOutput.ReadToEnd() + " " + $process.StandardError.ReadToEnd()).Trim()
        if ($process.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($output)) {
            throw "Browser executable validation failed with exit code $($process.ExitCode): $output"
        }

        return $output
    }
    finally {
        if (-not $process.HasExited) {
            try { $process.Kill($true) } catch { }
        }
        $process.Dispose()
    }
}

function Get-PlaywrightRuntimeMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $packagePropsPath = Join-Path $RepositoryRoot "Directory.Packages.props"
    if (-not (Test-Path -LiteralPath $packagePropsPath -PathType Leaf)) {
        throw "Directory.Packages.props was not found: $packagePropsPath"
    }

    [xml]$packageProps = Get-Content -LiteralPath $packagePropsPath -Raw
    $playwrightPackage = @($packageProps.Project.ItemGroup.PackageVersion) |
        Where-Object { $_.Include -eq "Microsoft.Playwright" } |
        Select-Object -First 1
    $chromiumVersion = @($packageProps.Project.PropertyGroup.PlaywrightChromiumVersion) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    $chromiumRevision = @($packageProps.Project.PropertyGroup.PlaywrightChromiumRevision) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ($null -eq $playwrightPackage -or [string]::IsNullOrWhiteSpace($playwrightPackage.Version)) {
        throw "Microsoft.Playwright package version is missing from Directory.Packages.props."
    }
    if ([string]::IsNullOrWhiteSpace($chromiumVersion)) {
        throw "PlaywrightChromiumVersion is missing from Directory.Packages.props."
    }
    if ([string]::IsNullOrWhiteSpace($chromiumRevision)) {
        throw "PlaywrightChromiumRevision is missing from Directory.Packages.props."
    }

    [PSCustomObject]@{
        PackageVersion = [string]$playwrightPackage.Version
        ChromiumVersion = [string]$chromiumVersion
        ChromiumRevision = [string]$chromiumRevision
    }
}

function Get-CompatiblePlaywrightBrowserCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [object]$ProductInfo,

        [Parameter(Mandatory = $true)]
        [string]$Revision
    )

    $cacheRoot = Join-Path $RepositoryRoot "artifacts\playwright-browsers"
    $revisionRoot = Join-Path $cacheRoot "$($ProductInfo.PlaywrightCachePrefix)-$Revision"
    if (-not (Test-Path -LiteralPath (Join-Path $revisionRoot "INSTALLATION_COMPLETE") -PathType Leaf)) {
        return $null
    }

    $executable = Find-ChromeExecutable -Root $revisionRoot
    if ([string]::IsNullOrWhiteSpace($executable)) {
        return $null
    }

    $relativeExecutable = [IO.Path]::GetRelativePath($revisionRoot, $executable)
    $topLevelName = @($relativeExecutable -split '[\\/]')[0]
    $payloadRoot = Join-Path $revisionRoot $topLevelName
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
        return $null
    }

    [PSCustomObject]@{
        PayloadRoot = $payloadRoot
        ExecutablePath = $executable
    }
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
            PlaywrightCachePrefix = "chromium_headless_shell"
        }
    }

    [PSCustomObject]@{
        DisplayName = "Chrome"
        MetadataDownloadKey = "chrome"
        InstallDirectory = "Chrome"
        CachePrefix = "chrome"
        PlaywrightCachePrefix = "chromium"
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
        if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::Arm64) {
            throw "Intel macOS is retired; automatic Chrome for Testing provisioning requires Apple Silicon ARM64."
        }

        return "mac-arm64"
    }

    throw "Unsupported OS for Chrome for Testing auto platform detection. Pass -Platform explicitly."
}

function Get-ChromeDownload {
    param(
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
        if ([string]::IsNullOrWhiteSpace($Channel)) {
            throw "Chrome for Testing channel must be provided when no exact version is selected."
        }

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
        PayloadRoot = ""
        SourceKind = "ChromeForTestingMetadata"
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

    # macOS uses BSD chmod, which treats the GNU-style `--` separator as a
    # file name. The path is already passed as one PowerShell argument and is
    # always an absolute repository path, so no separator is required.
    & $chmod.Source "+x" $Path
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
