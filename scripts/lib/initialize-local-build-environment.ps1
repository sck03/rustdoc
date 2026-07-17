[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$repositoryFullPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$runtimeRoot = Join-Path $repositoryFullPath ".codex-runtime"

function Test-SystemDriveEnvironmentPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($env:SystemDrive)) {
        return $false
    }

    return [System.IO.Path]::GetFullPath($Path).StartsWith(
        $env:SystemDrive,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Set-LocalEnvironmentPath {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $currentValue = [System.Environment]::GetEnvironmentVariable($Name, "Process")
    if (-not [string]::IsNullOrWhiteSpace($currentValue) -and -not (Test-SystemDriveEnvironmentPath -Path $currentValue)) {
        return
    }

    $resolvedPath = Join-Path $runtimeRoot $RelativePath
    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
    [System.Environment]::SetEnvironmentVariable($Name, $resolvedPath, "Process")
}

Set-LocalEnvironmentPath -Name "DOTNET_CLI_HOME" -RelativePath "dotnet-cli"
Set-LocalEnvironmentPath -Name "NUGET_PACKAGES" -RelativePath "nuget-packages"
Set-LocalEnvironmentPath -Name "NUGET_HTTP_CACHE_PATH" -RelativePath "nuget-http-cache"
Set-LocalEnvironmentPath -Name "npm_config_cache" -RelativePath "npm-cache"
Set-LocalEnvironmentPath -Name "TEMP" -RelativePath "temp"
Set-LocalEnvironmentPath -Name "TMP" -RelativePath "temp"
