[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$PdfiumPath,
    [string]$OnnxRuntimePath,
    [string]$RustTarget,
    [switch]$WithoutOcr,
    [switch]$SkipBuild,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
. (Join-Path $PSScriptRoot 'lib/native-ocr-resources.ps1')
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repositoryRoot '.codex-runtime'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts/native-desktop/ExportDocManager.Slint'
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputRoot)
function Assert-NativePackagePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $nativePath = [System.IO.Path]::GetFullPath($Path)
    if ($nativePath -eq [System.IO.Path]::GetPathRoot($nativePath)) { throw 'A native package path must not be a disk root.' }
    $currentPath = $nativePath
    while ($currentPath) {
        if (Test-Path -LiteralPath $currentPath) {
            $entry = Get-Item -LiteralPath $currentPath -Force
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Linked paths are not allowed: $currentPath" }
        }
        $parentPath = [System.IO.Path]::GetDirectoryName($currentPath)
        if ($parentPath -eq $currentPath) { break }
        $currentPath = $parentPath
    }
}
Assert-NativePackagePath -Path $outputFullPath
if (Test-ExportDocPathEqual -Left $outputFullPath -Right $repositoryRoot) { throw 'Output must be a dedicated package directory.' }
$marker = Join-Path $outputFullPath 'exportdoc-native-package.json'
if (Test-Path -LiteralPath $outputFullPath) {
    if (Test-Path -LiteralPath $marker -PathType Leaf) {
        $existingMarker = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($existingMarker.schemaVersion -ne 1 -or $existingMarker.purpose -ne 'rust-native-application' -or $existingMarker.frontend -ne 'Slint') {
            throw 'Existing output does not belong to the Slint package.'
        }
    } elseif (@(Get-ChildItem -LiteralPath $outputFullPath -Force).Count -gt 0) {
        throw 'Refusing to overwrite an unmarked existing directory.'
    }
}
if ([string]::IsNullOrWhiteSpace($env:CARGO_HOME)) { $env:CARGO_HOME = Join-Path $runtimeRoot 'cargo-home' }
if ([string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) { $env:CARGO_TARGET_DIR = Join-Path $runtimeRoot 'cargo-target-native' }
$env:TEMP = Join-Path $runtimeRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP, $outputFullPath | Out-Null
Invoke-ExportDocExternal -FilePath 'node' -Arguments @((Join-Path $PSScriptRoot 'provision-report-fonts.mjs')) -WorkingDirectory $repositoryRoot -DisplayName 'Provision verified report fonts'
if (-not $SkipBuild) {
    $cargoArguments = @('build', '--locked', '-p', 'export-doc-slint')
    if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
    if (-not [string]::IsNullOrWhiteSpace($RustTarget)) { $cargoArguments += @('--target', $RustTarget) }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $cargoArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build Rust + Slint desktop'
}
$executableSuffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$profile = $Configuration.ToLowerInvariant()
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($RustTarget)) {
    Join-Path $env:CARGO_TARGET_DIR $profile
} else {
    Join-Path (Join-Path $env:CARGO_TARGET_DIR $RustTarget) $profile
}
$copies = [ordered]@{}
if (-not $WithoutOcr) {
    Add-ExportDocNativeOcrResources -RepositoryRoot $repositoryRoot -Configuration $Configuration -Copies $copies -OnnxRuntimePath $OnnxRuntimePath -SkipBuild:$SkipBuild
}
$copies[(Join-Path $artifactDirectory "export-doc-slint$executableSuffix")] = "ExportDocManager$executableSuffix"
foreach ($name in @('NotoSansCJKsc-Regular.otf', 'NotoSansCJKsc-Bold.otf', 'NotoSerifCJKsc-Regular.otf', 'OFL-Noto-CJK.txt', 'font-manifest.json')) {
    $copies[(Join-Path $repositoryRoot "Resources/Fonts/OpenSource/$name")] = "Resources/Fonts/OpenSource/$name"
}
$copies[(Join-Path $repositoryRoot 'Resources/ExcelTemplates/invoice-import-template.xlsx')] = 'Resources/ExcelTemplates/invoice-import-template.xlsx'
if ([string]::IsNullOrWhiteSpace($PdfiumPath)) {
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $runtimeRoot 'nuget-packages' }
    $platform = if ($env:OS -eq 'Windows_NT') { 'win32' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
    $nativeName = if ($env:OS -eq 'Windows_NT') { 'pdfium.dll' } elseif ($IsMacOS) { 'libpdfium.dylib' } else { 'libpdfium.so' }
    $packageId = "bblanchon.pdfium.$platform"
    $lockedPackages = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/ExportDocManager.Infrastructure.PdfOcr/packages.lock.json') -Raw | ConvertFrom-Json
    $versions = @($lockedPackages.dependencies.PSObject.Properties | ForEach-Object {
        $_.Value.PSObject.Properties | Where-Object { $_.Name -ieq $packageId } | ForEach-Object { $_.Value.resolved }
    } | Select-Object -Unique)
    if ($versions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versions[0])) { throw 'The PDFium package must have one exact version in the governed lockfile.' }
    $packageRoot = Join-Path (Join-Path $nugetRoot $packageId) $versions[0]
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $candidates = if (Test-Path -LiteralPath $packageRoot) { @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter $nativeName | Where-Object { $_.FullName -match "[-/]$architecture[/\\]" }) } else { @() }
    if ($candidates.Count -ne 1) { throw 'Pass -PdfiumPath with the governed PDFium library from the current locked package.' }
    $PdfiumPath = $candidates[0].FullName
}
Assert-NativePackagePath -Path $PdfiumPath
$copies[$PdfiumPath] = 'Resources/Pdf/' + [System.IO.Path]::GetFileName($PdfiumPath)
foreach ($name in @('THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_DEPENDENCIES.md', 'eng/licenses/LicenseRef-Slint-Royalty-free-2.0.md')) {
    $copies[(Join-Path $repositoryRoot $name)] = $name
}
foreach ($copy in $copies.GetEnumerator()) {
    $destination = Join-Path $outputFullPath $copy.Value
    Assert-NativePackagePath -Path $copy.Key
    Assert-NativePackagePath -Path $destination
    if (-not (Test-ExportDocPathUnderRoot -Path $destination -Root $outputFullPath)) { throw 'Package destination escaped output root.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $copy.Key -Destination $destination -Force
}
$packageMarker = [ordered]@{
    schemaVersion = 1; purpose = 'rust-native-application'; product = 'ExportDocManager'
    configuration = $Configuration; backend = 'Rust'; frontend = 'Slint'; webView = $false; ocr = (-not $WithoutOcr)
    builtAt = [DateTimeOffset]::UtcNow.ToString('o')
}
$packageMarker | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
Write-Host "Rust + Slint package: $outputFullPath"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
