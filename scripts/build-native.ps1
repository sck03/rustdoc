[CmdletBinding()]
param(
    [ValidateSet('Full', 'Document', 'Sales', 'Administration')][string]$Edition = 'Full',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$PdfiumPath,
    [string]$OnnxRuntimePath,
    [string]$RustTarget,
    [string]$Bundles,
    [switch]$PreflightOnly,
    [switch]$WithoutOcr,
    [switch]$SkipBuild,
    [switch]$SkipFrontendBuild,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
. (Join-Path $PSScriptRoot 'lib/native-package-resources.ps1')
$Edition = Resolve-ExportDocProductEdition -Edition $Edition
$editionMetadata = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'product-editions.json') -Raw | ConvertFrom-Json).editions.$Edition
$WithoutOcr = $WithoutOcr -or -not $editionMetadata.resourceProfile.ocr
$env:EXPORTDOCMANAGER_PRODUCT_EDITION = $Edition
$env:TAURI_CONFIG = @{ identifier = $editionMetadata.identifier; productName = $editionMetadata.productName } | ConvertTo-Json -Compress
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
    $packageName = if ($Edition -eq 'Full') { 'ExportDocManager.Tauri' } else { "ExportDocManager.Tauri.$Edition" }
    $OutputRoot = Join-Path $repositoryRoot "artifacts/native-desktop/$packageName"
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
        $existingEdition = if ($existingMarker.edition) { $existingMarker.edition } else { 'Full' }
        if ($existingEdition -ne $Edition) { throw 'Refusing to replace a different edition in the same package directory.' }
    } elseif (@(Get-ChildItem -LiteralPath $outputFullPath -Force).Count -gt 0) {
        throw 'Refusing to overwrite an unmarked existing directory.'
    }
}
Initialize-ExportDocRustBuildEnvironment -RepositoryRoot $repositoryRoot
$artifactDirectory = Get-ExportDocCargoArtifactDirectory -RepositoryRoot $repositoryRoot -Configuration $Configuration -RustTarget $RustTarget
$editionDirectory = Join-Path $artifactDirectory "editions/$Edition"
$executableSuffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$editionBinary = Join-Path $editionDirectory "export-doc-tauri$executableSuffix"
$editionReceipt = Join-Path $editionDirectory 'build.json'
if ($SkipBuild) {
    Assert-ExportDocRustBuildOutputs -ArtifactDirectory $editionDirectory -Names @('export-doc-tauri')
    if (-not (Test-Path -LiteralPath $editionReceipt -PathType Leaf)) { throw 'Edition build receipt is missing; rebuild this edition.' }
    $receipt = Get-Content -LiteralPath $editionReceipt -Raw | ConvertFrom-Json
    $version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'version.json') -Raw | ConvertFrom-Json).version
    if ($receipt.edition -ne $Edition -or $receipt.version -ne $version -or $receipt.sha256 -ne (Get-FileHash -LiteralPath $editionBinary -Algorithm SHA256).Hash) { throw 'Edition binary does not match its build receipt or product version.' }
    if (-not $WithoutOcr) { Assert-ExportDocRustBuildOutputs -ArtifactDirectory $artifactDirectory -Names @('exportdoc-ocr') }
}
if (-not $SkipBuild) {
    $env:npm_config_cache = Join-Path $runtimeRoot 'npm-cache'
    if (-not $SkipFrontendBuild) {
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-web', 'ci') -WorkingDirectory $repositoryRoot -DisplayName 'Restore shared React dependencies'
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-web', 'run', 'build') -WorkingDirectory $repositoryRoot -DisplayName 'Build original React UI'
    } elseif (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'apps/export-doc-web/dist/index.html') -PathType Leaf)) { throw 'Shared React build is missing.' }
    $cargoArguments = @('build', '--locked', '-p', 'export-doc-tauri', '--features', 'custom-protocol')
    if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
    if (-not [string]::IsNullOrWhiteSpace($RustTarget)) { $cargoArguments += @('--target', $RustTarget) }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $cargoArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build Tauri + React + Rust desktop'
    Assert-NativePackagePath -Path $editionDirectory
    New-Item -ItemType Directory -Force -Path $editionDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $artifactDirectory "export-doc-tauri$executableSuffix") -Destination $editionBinary -Force
    @{ edition = $Edition; version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'version.json') -Raw | ConvertFrom-Json).version; sha256 = (Get-FileHash -LiteralPath $editionBinary -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath $editionReceipt -Encoding utf8
}
$copies = [ordered]@{}
$copies[$editionBinary] = "ExportDocManager$executableSuffix"
$webviewLoader = Join-Path $artifactDirectory 'WebView2Loader.dll'
if ($env:OS -eq 'Windows_NT' -and (Test-Path -LiteralPath $webviewLoader -PathType Leaf)) {
    $copies[$webviewLoader] = 'WebView2Loader.dll'
}
Add-ExportDocRustPackageResources -RepositoryRoot $repositoryRoot -Configuration $Configuration -RustTarget $RustTarget -Copies $copies -PdfiumPath $PdfiumPath -OnnxRuntimePath $OnnxRuntimePath -WithoutOcr:$WithoutOcr -SkipBuild:$SkipBuild -DocumentResources:$editionMetadata.resourceProfile.documentResources
$targetIsWindowsX64 = $env:OS -eq 'Windows_NT' -and ($RustTarget -eq 'x86_64-pc-windows-msvc' -or $RustTarget -eq 'x86_64-pc-windows-gnu' -or ([string]::IsNullOrWhiteSpace($RustTarget) -and [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'X64'))
if ($targetIsWindowsX64) {
    Invoke-ExportDocExternal -FilePath 'pwsh' -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'provision-webview2-runtime.ps1')) -WorkingDirectory $repositoryRoot -DisplayName 'Verify Windows WebView2 bootstrap assets'
    $release = Get-Content -LiteralPath (Join-Path $repositoryRoot 'WebView2Runtime/webview2-runtime.json') -Raw | ConvertFrom-Json
    foreach ($name in @('webview2-runtime.json', 'README.md', $release.fileName)) {
        $copies[(Join-Path $repositoryRoot "WebView2Runtime/$name")] = "WebView2Runtime/$name"
    }
}
Copy-ExportDocNativePackageFiles -Copies $copies -OutputRoot $outputFullPath
@{ schemaVersion = 1; mode = 'portable' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'portable-runtime.json') -Encoding utf8
@{ schemaVersion = 1; target = 'tauri-desktop'; backend = 'Rust' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'runtime-layout.json') -Encoding utf8
$packageMarker = [ordered]@{
    schemaVersion = 1; purpose = 'rust-native-application'; product = 'ExportDocManager'
    version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'version.json') -Raw | ConvertFrom-Json).version
    configuration = $Configuration; backend = 'Rust'; frontend = 'Tauri'; webView = $true; ocr = (-not $WithoutOcr)
    edition = $Edition; productName = $editionMetadata.productName; documentResources = $editionMetadata.resourceProfile.documentResources
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
