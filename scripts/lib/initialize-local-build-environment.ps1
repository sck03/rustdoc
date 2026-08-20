[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$repositoryFullPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$runtimeRoot = Join-Path $repositoryFullPath ".codex-runtime"

$dotnetArchitectureVariable = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()) {
    "X64" { "DOTNET_ROOT_X64" }
    "Arm64" { "DOTNET_ROOT_ARM64" }
    "X86" { "DOTNET_ROOT_X86" }
    default { $null }
}
$dotnetExecutableName = if ($env:OS -eq "Windows_NT") { "dotnet.exe" } else { "dotnet" }
$dotnetRootCandidates = New-Object System.Collections.Generic.List[string]
if ($dotnetArchitectureVariable) {
    $dotnetRootCandidates.Add([System.Environment]::GetEnvironmentVariable($dotnetArchitectureVariable, "Process"))
}
$dotnetRootCandidates.Add([System.Environment]::GetEnvironmentVariable("DOTNET_ROOT", "Process"))
if ($env:OS -eq "Windows_NT") {
    if ($dotnetArchitectureVariable) {
        $dotnetRootCandidates.Add([System.Environment]::GetEnvironmentVariable($dotnetArchitectureVariable, "User"))
    }
    $dotnetRootCandidates.Add([System.Environment]::GetEnvironmentVariable("DOTNET_ROOT", "User"))
}
$dotnetRoot = $dotnetRootCandidates | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_) -and
    (Test-Path -LiteralPath (Join-Path $_ $dotnetExecutableName) -PathType Leaf)
} | Select-Object -First 1

if (-not [string]::IsNullOrWhiteSpace($dotnetRoot)) {
    $dotnetRoot = [System.IO.Path]::GetFullPath($dotnetRoot)
    $env:DOTNET_ROOT = $dotnetRoot
    if ($dotnetArchitectureVariable) {
        [System.Environment]::SetEnvironmentVariable($dotnetArchitectureVariable, $dotnetRoot, "Process")
    }
    $pathEntries = @($env:Path -split [System.IO.Path]::PathSeparator) |
        Where-Object { -not [string]::Equals($_.TrimEnd('\', '/'), $dotnetRoot.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase) }
    $env:Path = (@($dotnetRoot) + $pathEntries) -join [System.IO.Path]::PathSeparator
}

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
