# Original public Windows entry retained; shared Rust packaging owns the build.
[CmdletBinding()]
param(
    [ValidateSet('All', 'Full', 'Document', 'Sales', 'Administration')][string]$Edition = 'All',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$RustTarget, [string]$OutputDir, [string]$CargoTargetDir,
    [switch]$PreflightOnly, [switch]$SkipMainBuild, [switch]$WithoutOcr, [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'This entry requires Windows.' }
if ($CargoTargetDir) { $env:CARGO_TARGET_DIR = [IO.Path]::GetFullPath($CargoTargetDir) }
$editions = if ($Edition -eq 'All') { @('Full', 'Sales', 'Document', 'Administration') } else { @($Edition) }
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
try {
    for ($index = 0; $index -lt $editions.Count; $index++) {
        $current = $editions[$index]
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'build-native.ps1'), '-Edition', $current, '-Configuration', $Configuration, '-NoPause')
        if ($PreflightOnly) { $arguments += '-PreflightOnly' }
        if ($SkipMainBuild) { $arguments += '-SkipBuild' }
        if ($WithoutOcr) { $arguments += '-WithoutOcr' }
        if ($index -gt 0) { $arguments += '-SkipFrontendBuild' }
        if ($RustTarget) { $arguments += @('-RustTarget', $RustTarget) }
        if ($OutputDir) {
            $name = if ($current -eq 'Full') { 'ExportDocManager.Tauri' } else { "ExportDocManager.Tauri.$current" }
            $arguments += @('-OutputRoot', (Join-Path $OutputDir $name))
        }
        Invoke-ExportDocExternal -FilePath 'pwsh' -Arguments $arguments -WorkingDirectory (Split-Path -Parent $PSScriptRoot) -DisplayName "Package $current desktop"
        if ($PreflightOnly) { break }
    }
} catch {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
