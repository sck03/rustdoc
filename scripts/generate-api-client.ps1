param(
    [string]$OutputPath = "apps\export-doc-web\src\api\generated\exportDocManagerApi.ts",
    [string]$BaseUrl = "http://127.0.0.1:5188"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "tools\ExportDocManager.ApiClientGenerator\ExportDocManager.ApiClientGenerator.csproj"
. (Join-Path $PSScriptRoot "lib\build-script-support.ps1")
. (Join-Path $PSScriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $resolvedOutput = $OutputPath
} else {
    $resolvedOutput = Join-Path $repoRoot $OutputPath
}

Invoke-ExportDocExternal -FilePath "dotnet" -Arguments @(
    "run",
    "--project", $toolProject,
    "--",
    "--output", $resolvedOutput,
    "--base-url", $BaseUrl
) -WorkingDirectory $repoRoot
