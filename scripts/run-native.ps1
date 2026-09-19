[CmdletBinding()]
param([string]$AppRoot, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
if ([string]::IsNullOrWhiteSpace($AppRoot)) {
    $AppRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/native-desktop/ExportDocManager.Slint'
}
$executableName = if ($env:OS -eq 'Windows_NT') { 'ExportDocManager.exe' } else { 'ExportDocManager' }
$executable = Join-Path $AppRoot $executableName
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Build the native program first with scripts/build-native.ps1 (or build-native.cmd on Windows).'
}
Invoke-ExportDocExternal -FilePath $executable -Arguments @('--app-root', $AppRoot) -WorkingDirectory $AppRoot -DisplayName 'ExportDocManager Rust + Slint' -TimeoutSeconds 86400
