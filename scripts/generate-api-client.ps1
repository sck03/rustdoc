param(
    [string]$OutputPath = "apps/export-doc-web/src/api/generated/exportDocManagerApi.ts",
    [string]$OpenApiPath = "",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib/build-script-support.ps1")
$cargoEnvironment = @{ CARGO_HOME = $(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $repoRoot '.codex-runtime/cargo-home' }) }
$generatorArguments = @('run', '--locked', '-p', 'export-doc-contracts', '--example', 'generate_clients', '--', '--output', $OutputPath)
if (-not [string]::IsNullOrWhiteSpace($OpenApiPath)) { $generatorArguments += @('--openapi', $OpenApiPath) }
if ($Check) { $generatorArguments += '--check' }
Invoke-ExportDocExternal -FilePath 'cargo' -Arguments $generatorArguments -WorkingDirectory $repoRoot -Environment $cargoEnvironment
