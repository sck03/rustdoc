param(
    [string]$OutputPath = "apps\export-doc-web\src\api\generated\exportDocManagerApi.ts",
    [string]$BaseUrl = "http://127.0.0.1:5188",
    [string]$OpenApiPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "tools\ExportDocManager.ApiClientGenerator\ExportDocManager.ApiClientGenerator.csproj"
. (Join-Path $PSScriptRoot "lib\build-script-support.ps1")
. (Join-Path $PSScriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

if ([string]::IsNullOrWhiteSpace($OpenApiPath)) {
    $OpenApiPath = "artifacts/contracts/openapi.json"
    New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot "artifacts/contracts") | Out-Null
    $cargoEnvironment = @{ CARGO_HOME = $(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $repoRoot '.codex-runtime/cargo-home' }) }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments @('run', '--locked', '-p', 'export-doc-contracts', '--example', 'export_openapi', '--', $OpenApiPath) -WorkingDirectory $repoRoot -Environment $cargoEnvironment
    Invoke-ExportDocExternal -FilePath 'node' -Arguments @('scripts/generate-native-api-client.mjs', '--openapi', $OpenApiPath) -WorkingDirectory $repoRoot
}
$resolvedOpenApi = if ([System.IO.Path]::IsPathRooted($OpenApiPath)) { $OpenApiPath } else { Join-Path $repoRoot $OpenApiPath }

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $resolvedOutput = $OutputPath
} else {
    $resolvedOutput = Join-Path $repoRoot $OutputPath
}

Invoke-ExportDocExternal -FilePath "dotnet" -Arguments @(
    "run",
    "--project", $toolProject,
    "-p:NuGetAudit=false",
    "--",
    "--output", $resolvedOutput,
    "--base-url", $BaseUrl,
    "--openapi", $resolvedOpenApi
) -WorkingDirectory $repoRoot
