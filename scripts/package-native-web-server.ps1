[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$RuntimeIdentifier,
    [switch]$SkipBuild,
    [switch]$WithoutOcr,
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
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts/native-web-server'
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputRoot)
if ($outputFullPath -eq [System.IO.Path]::GetPathRoot($outputFullPath) -or (Test-ExportDocPathEqual -Left $outputFullPath -Right $repositoryRoot)) {
    throw 'Use a dedicated web server package directory.'
}
Assert-NativePackagePath -Path $outputFullPath
$packageMarkerPath = Join-Path $outputFullPath 'exportdoc-native-web-server.json'
if (Test-Path -LiteralPath $outputFullPath) {
    if (Test-Path -LiteralPath $packageMarkerPath -PathType Leaf) {
        $existing = Get-Content -LiteralPath $packageMarkerPath -Raw | ConvertFrom-Json
        if ($existing.schemaVersion -ne 1 -or $existing.purpose -ne 'rust-native-web-server-package') {
            throw 'Output does not belong to the Rust web server package.'
        }
    } elseif (@(Get-ChildItem -LiteralPath $outputFullPath -Force).Count -gt 0) {
        throw 'Refusing to overwrite an unmarked existing directory.'
    }
}
$runtimeRoot = Join-Path $repositoryRoot '.codex-runtime'
if (-not $env:CARGO_HOME) { $env:CARGO_HOME = Join-Path $runtimeRoot 'cargo-home' }
if (-not $env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR = Join-Path $runtimeRoot 'cargo-target-native' }
$env:TEMP = Join-Path $runtimeRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
$profile = $Configuration.ToLowerInvariant()
$targetRoot = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $repositoryRoot 'target' }
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $targetDirectory = Join-Path $targetRoot $profile
} else {
    $targetDirectory = Join-Path (Join-Path $targetRoot $RuntimeIdentifier) $profile
}
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null
if (-not $SkipBuild) {
    $env:npm_config_cache = Join-Path $runtimeRoot 'npm-cache'
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments @('--prefix', 'apps/export-doc-web', 'ci') -WorkingDirectory $repositoryRoot -DisplayName 'Restore shared React dependencies'
    $npmArguments = @('--prefix', 'apps/export-doc-web', 'run', 'build')
    Invoke-ExportDocExternal -FilePath 'npm' -Arguments $npmArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build React web assets'
    $cargoArguments = @('build', '--locked', '-p', 'export-doc-server')
    if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $cargoArguments += @('--target', $RuntimeIdentifier)
    }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $cargoArguments -WorkingDirectory $repositoryRoot -DisplayName 'Build Rust HTTP server'
}
$suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$serverSource = Join-Path $targetDirectory "export-doc-server$suffix"
if (-not (Test-Path -LiteralPath $serverSource -PathType Leaf)) {
    throw "Rust HTTP server executable was not found: $serverSource"
}
$copyMap = [ordered]@{
    $serverSource = "ExportDocManager.Server$suffix"
}
$webRoot = Join-Path $repositoryRoot 'apps/export-doc-web/dist'
if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'index.html') -PathType Leaf)) { throw 'Build the React frontend first.' }
foreach ($file in Get-ChildItem -LiteralPath $webRoot -Recurse -File) {
    Assert-NativePackagePath -Path $file.FullName
    $copyMap[$file.FullName] = Join-Path 'Web' ([IO.Path]::GetRelativePath($webRoot, $file.FullName))
}
Add-ExportDocRustPackageResources -RepositoryRoot $repositoryRoot -Configuration $Configuration -RustTarget $RuntimeIdentifier -Copies $copyMap -WithoutOcr:$WithoutOcr -SkipBuild:$SkipBuild
$clientRoot = Join-Path $runtimeRoot 'postgresql-client'
$platform = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
$clientVersion = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/native-runtime-packages.json') -Raw | ConvertFrom-Json).postgresqlClient.version
$clientRoot = Join-Path $clientRoot "$clientVersion-$platform-$([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)"
if (-not (Test-Path -LiteralPath (Join-Path $clientRoot 'postgresql-client.json'))) {
    Invoke-ExportDocExternal -FilePath pwsh -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'provision-postgresql-client.ps1'), '-Platform', $platform, '-Destination', $clientRoot) -WorkingDirectory $repositoryRoot -TimeoutSeconds 1800 -DisplayName 'Prepare PostgreSQL 18 clients'
}
$clientMarker = Get-Content -LiteralPath (Join-Path $clientRoot 'postgresql-client.json') -Raw | ConvertFrom-Json
if ($clientMarker.version -ne $clientVersion -or $clientMarker.platform -ne $platform) { throw 'PostgreSQL client cache does not match the declared platform/version.' }
foreach ($file in Get-ChildItem -LiteralPath $clientRoot -Recurse -File) { $copyMap[$file.FullName] = Join-Path 'Tools/PostgreSQL' ([IO.Path]::GetRelativePath($clientRoot, $file.FullName)) }
foreach ($entry in $copyMap.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Key)) {
        throw "Required web server package input is missing: $($entry.Key)"
    }
    Assert-NativePackagePath -Path $entry.Key
    $destination = Join-Path $outputFullPath $entry.Value
    Assert-NativePackagePath -Path $destination
    if (-not (Test-ExportDocPathUnderRoot -Path $destination -Root $outputFullPath)) {
        throw 'Package destination escaped output root.'
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $entry.Key -Destination $destination -Force
}
$marker = [ordered]@{
    schemaVersion = 1
    purpose = 'rust-native-web-server-package'
    product = 'ExportDocManager'
    version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'version.json') -Raw | ConvertFrom-Json).version
    frontend = 'React'
    server = 'Rust HTTP'
    database = 'PostgreSQL 18'
    ocr = (-not $WithoutOcr)
    configuration = $Configuration
    runtimeIdentifier = if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) { 'host' } else { $RuntimeIdentifier }
    builtAt = [DateTimeOffset]::UtcNow.ToString('o')
}
$marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'exportdoc-native-web-server.json') -Encoding utf8
Write-Host "Rust native web server package: $outputFullPath"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
