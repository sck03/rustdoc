# Original installer entry, now packages the shared Rust backend in Tauri.
[CmdletBinding()]
param([ValidateSet('Document', 'Sales')][string]$Edition = 'Document', [string]$RustTarget, [string]$OutputDir, [string]$CargoTargetDir, [switch]$PreflightOnly, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'This entry requires Windows.' }
if ($CargoTargetDir) { $env:CARGO_TARGET_DIR = [IO.Path]::GetFullPath($CargoTargetDir) }
$arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'build-native.ps1'), '-Edition', $Edition, '-Bundles', 'nsis')
if ($OutputDir) { $arguments += @('-OutputRoot', $OutputDir) }
if ($RustTarget) { $arguments += @('-RustTarget', $RustTarget) }
if ($NoPause) { $arguments += '-NoPause' }
if ($PreflightOnly) { $arguments += '-PreflightOnly' }
Invoke-ExportDocExternal -FilePath 'pwsh' -Arguments $arguments -WorkingDirectory (Split-Path -Parent $PSScriptRoot) -DisplayName "Build $Edition Windows installer"
