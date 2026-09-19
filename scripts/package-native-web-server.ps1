[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$RuntimeIdentifier,
    [switch]$SkipBuild,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
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
$profile = $Configuration.ToLowerInvariant()
$targetRoot = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $repositoryRoot 'target' }
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $targetDirectory = Join-Path $targetRoot $profile
} else {
    $targetDirectory = Join-Path (Join-Path $targetRoot $RuntimeIdentifier) $profile
}
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null
if (-not $SkipBuild) {
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
    (Join-Path $repositoryRoot 'apps/export-doc-web/dist') = 'Web'
    (Join-Path $repositoryRoot 'Resources/ExcelTemplates') = 'Resources/ExcelTemplates'
    (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') = 'THIRD_PARTY_NOTICES.md'
    (Join-Path $repositoryRoot 'THIRD_PARTY_DEPENDENCIES.md') = 'THIRD_PARTY_DEPENDENCIES.md'
}
foreach ($entry in $copyMap.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Key)) {
        throw "Required web server package input is missing: $($entry.Key)"
    }
    $destination = Join-Path $outputFullPath $entry.Value
    if (-not (Test-ExportDocPathUnderRoot -Path $destination -Root $outputFullPath)) {
        throw 'Package destination escaped output root.'
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $entry.Key -Destination $destination -Recurse -Force
}
$marker = [ordered]@{
    schemaVersion = 1
    purpose = 'rust-native-web-server-package'
    product = 'ExportDocManager'
    frontend = 'React'
    server = 'Rust HTTP'
    database = 'PostgreSQL 18'
    configuration = $Configuration
    runtimeIdentifier = if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) { 'host' } else { $RuntimeIdentifier }
    builtAt = [DateTimeOffset]::UtcNow.ToString('o')
}
$marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputFullPath 'exportdoc-native-web-server.json') -Encoding utf8
Write-Host "Rust native web server package: $outputFullPath"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
