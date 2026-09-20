[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$PdfiumPath,
    [string]$OnnxRuntimePath,
    [string]$RustTarget,
    [string]$Bundles,
    [switch]$PreflightOnly,
    [switch]$WithoutOcr,
    [switch]$SkipBuild,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
. (Join-Path $PSScriptRoot 'lib/native-package-resources.ps1')
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repositoryRoot '.codex-runtime'
if ($PreflightOnly) {
    foreach ($commandName in @('cargo', 'rustc', 'node', 'npm', 'curl')) {
        $command = Resolve-ExportDocExternalCommand -FilePath $commandName
        Write-Host "$commandName : $command"
    }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments @('--version') -WorkingDirectory $repositoryRoot -DisplayName 'Rust toolchain'
    Invoke-ExportDocExternal -FilePath 'node' -Arguments @('--version') -WorkingDirectory $repositoryRoot -DisplayName 'Frontend build runtime'
    Write-Host 'Desktop/backend build uses Rust; .NET SDK and .NET runtime are not required.'
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
    return
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts/native-desktop/ExportDocManager.Tauri'
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputRoot)
Assert-NativePackagePath -Path $outputFullPath
if (Test-ExportDocPathEqual -Left $outputFullPath -Right $repositoryRoot) { throw 'Output must be a dedicated package directory.' }
$marker = Join-Path $outputFullPath 'exportdoc-native-package.json'
if (Test-Path -LiteralPath $outputFullPath) {
    if (Test-Path -LiteralPath $marker -PathType Leaf) {
        $existingMarker = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($existingMarker.schemaVersion -ne 1 -or $existingMarker.purpose -ne 'rust-native-application' -or $existingMarker.frontend -ne 'Tauri') {
            throw 'Existing output does not belong to the Tauri package.'
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
if (-not $SkipBuild) {
    $env:npm_config_cache = Join-Path $runtimeRoot 'npm-cache'
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-web', 'ci') -WorkingDirectory $repositoryRoot -DisplayName 'Restore shared React dependencies'
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-web', 'run', 'build') -WorkingDirectory $repositoryRoot -DisplayName 'Build original React UI'
    $cargoArguments = @('build', '--locked', '-p', 'export-doc-tauri', '--features', 'custom-protocol')
    if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
    if (-not [string]::IsNullOrWhiteSpace($RustTarget)) { $cargoArguments += @('--target', $RustTarget) }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $cargoArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build Tauri + React + Rust desktop'
}
$executableSuffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$profile = $Configuration.ToLowerInvariant()
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($RustTarget)) {
    Join-Path $env:CARGO_TARGET_DIR $profile
} else {
    Join-Path (Join-Path $env:CARGO_TARGET_DIR $RustTarget) $profile
}
$copies = [ordered]@{}
$copies[(Join-Path $artifactDirectory "export-doc-tauri$executableSuffix")] = "ExportDocManager$executableSuffix"
$webviewLoader = Join-Path $artifactDirectory 'WebView2Loader.dll'
if ($env:OS -eq 'Windows_NT' -and (Test-Path -LiteralPath $webviewLoader -PathType Leaf)) {
    $copies[$webviewLoader] = 'WebView2Loader.dll'
}
Add-ExportDocRustPackageResources -RepositoryRoot $repositoryRoot -Configuration $Configuration -RustTarget $RustTarget -Copies $copies -PdfiumPath $PdfiumPath -OnnxRuntimePath $OnnxRuntimePath -WithoutOcr:$WithoutOcr -SkipBuild:$SkipBuild
$targetIsWindowsX64 = $env:OS -eq 'Windows_NT' -and ($RustTarget -eq 'x86_64-pc-windows-msvc' -or $RustTarget -eq 'x86_64-pc-windows-gnu' -or ([string]::IsNullOrWhiteSpace($RustTarget) -and [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'X64'))
if ($targetIsWindowsX64) {
    Invoke-ExportDocExternal -FilePath 'pwsh' -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'provision-webview2-runtime.ps1')) -WorkingDirectory $repositoryRoot -DisplayName 'Verify Windows WebView2 bootstrap assets'
    $release = Get-Content -LiteralPath (Join-Path $repositoryRoot 'WebView2Runtime/webview2-runtime.json') -Raw | ConvertFrom-Json
    foreach ($name in @('webview2-runtime.json', 'README.md', $release.fileName)) {
        $copies[(Join-Path $repositoryRoot "WebView2Runtime/$name")] = "WebView2Runtime/$name"
    }
}
foreach ($copy in $copies.GetEnumerator()) {
    $destination = Join-Path $outputFullPath $copy.Value
    Assert-NativePackagePath -Path $copy.Key
    Assert-NativePackagePath -Path $destination
    if (-not (Test-ExportDocPathUnderRoot -Path $destination -Root $outputFullPath)) { throw 'Package destination escaped output root.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $copy.Key -Destination $destination -Force
}
@{ schemaVersion = 1; mode = 'portable' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'portable-runtime.json') -Encoding utf8
@{ schemaVersion = 1; target = 'tauri-desktop'; backend = 'Rust' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'runtime-layout.json') -Encoding utf8
$packageMarker = [ordered]@{
    schemaVersion = 1; purpose = 'rust-native-application'; product = 'ExportDocManager'
    configuration = $Configuration; backend = 'Rust'; frontend = 'Tauri'; webView = $true; ocr = (-not $WithoutOcr)
    builtAt = [DateTimeOffset]::UtcNow.ToString('o')
}
$packageMarker | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($Bundles)) {
    $bundleResources = [ordered]@{}
    foreach ($relative in $copies.Values) {
        if ($relative -ne "ExportDocManager$executableSuffix") {
            $bundleResources[(Join-Path $outputFullPath $relative)] = ($relative -replace '\\', '/')
        }
    }
    foreach ($name in @('exportdoc-native-package.json', 'runtime-layout.json')) {
        $bundleResources[(Join-Path $outputFullPath $name)] = $name
    }
    $bundleConfig = Join-Path $runtimeRoot 'tauri-rust-bundle.conf.json'
    @{ build = @{ beforeBuildCommand = '' }; bundle = @{ resources = $bundleResources } } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $bundleConfig -Encoding utf8
    $env:npm_config_cache = Join-Path $runtimeRoot 'npm-cache'
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-tauri', 'ci') -WorkingDirectory $repositoryRoot -DisplayName 'Restore pinned Tauri CLI'
    $bundleArguments = @((Join-Path $PSScriptRoot 'run-tauri-build.mjs'), '--bundles', $Bundles, '--config', $bundleConfig)
    if ($Configuration -eq 'Debug') { $bundleArguments += '--debug' }
    if ($RustTarget) { $bundleArguments += @('--target', $RustTarget) }
    Invoke-ExportDocExternal -FilePath 'node' -Arguments $bundleArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build platform Tauri installer and application bundle'
}
Write-Host "Rust + Tauri package: $outputFullPath"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
