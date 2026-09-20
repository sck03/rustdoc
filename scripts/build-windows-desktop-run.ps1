# Original public Windows entry retained; shared Rust packaging owns the build.
[CmdletBinding()]
param([string]$OutputDir, [string]$CargoTargetDir, [switch]$PreflightOnly, [switch]$SkipMainBuild, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'This entry requires Windows.' }
if ($CargoTargetDir) { $env:CARGO_TARGET_DIR = [IO.Path]::GetFullPath($CargoTargetDir) }
$parameters = @{ NoPause = $NoPause; PreflightOnly = $PreflightOnly; SkipBuild = $SkipMainBuild }
if ($OutputDir) { $parameters.OutputRoot = $OutputDir }
& (Join-Path $PSScriptRoot 'build-native.ps1') @parameters
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
