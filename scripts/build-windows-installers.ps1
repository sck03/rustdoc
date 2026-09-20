# Original installer entry, now packages the shared Rust backend in Tauri.
[CmdletBinding()]
param([string]$OutputDir, [string]$CargoTargetDir, [switch]$PreflightOnly, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'This entry requires Windows.' }
if ($CargoTargetDir) { $env:CARGO_TARGET_DIR = [IO.Path]::GetFullPath($CargoTargetDir) }
$parameters = @{ Bundles = 'nsis'; NoPause = $NoPause; PreflightOnly = $PreflightOnly }
if ($OutputDir) { $parameters.OutputRoot = $OutputDir }
& (Join-Path $PSScriptRoot 'build-native.ps1') @parameters
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
