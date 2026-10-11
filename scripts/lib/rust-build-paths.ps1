# One build tree for direct Cargo commands, local entries and CI. An explicit
# CARGO_TARGET_DIR remains supported, relative to the repository command cwd.
function Get-ExportDocCargoTargetDirectory {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
    $target = if ([string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) { 'target' } else { $env:CARGO_TARGET_DIR }
    if (-not [IO.Path]::IsPathRooted($target)) { $target = Join-Path $RepositoryRoot $target }
    return [IO.Path]::GetFullPath($target)
}

function Get-ExportDocCargoArtifactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [ValidateSet('Debug', 'Release')][string]$Configuration,
        [string]$RustTarget
    )
    $target = Get-ExportDocCargoTargetDirectory -RepositoryRoot $RepositoryRoot
    if ($RustTarget) { $target = Join-Path $target $RustTarget }
    return Join-Path $target $Configuration.ToLowerInvariant()
}

function Initialize-ExportDocRustBuildEnvironment {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
    $runtime = Join-Path $RepositoryRoot '.codex-runtime'
    if (-not $env:CARGO_HOME) { $env:CARGO_HOME = Join-Path $runtime 'cargo-home' }
    $env:CARGO_TARGET_DIR = Get-ExportDocCargoTargetDirectory -RepositoryRoot $RepositoryRoot
    $env:TEMP = Join-Path $runtime 'temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
}

function Assert-ExportDocRustBuildOutputs {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string[]]$Names
    )
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    $missing = @($Names | ForEach-Object { Join-Path $ArtifactDirectory "$_$suffix" } | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($missing.Count) { throw "-SkipBuild requires existing binaries for this profile/target. Build without -SkipBuild first. Missing:`n$($missing -join "`n")" }
}

function Write-ExportDocRustBuildReceipt {
    param([string]$Binary, [string]$Receipt, [string]$Version, [string]$Edition)
    $value = @{ version = $Version; sha256 = (Get-FileHash -LiteralPath $Binary -Algorithm SHA256).Hash }
    if ($Edition) { $value.edition = $Edition }
    $value | ConvertTo-Json | Set-Content -LiteralPath $Receipt -Encoding utf8
}

function Assert-ExportDocRustBuildReceipt {
    param([string]$Binary, [string]$Receipt, [string]$Version, [string]$Edition)
    if (-not (Test-Path -LiteralPath $Receipt -PathType Leaf)) { throw 'Build receipt is missing; rebuild this product without -SkipBuild.' }
    $value = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
    if ($value.version -ne $Version -or ($Edition -and $value.edition -ne $Edition) -or
        $value.sha256 -ne (Get-FileHash -LiteralPath $Binary -Algorithm SHA256).Hash) {
        throw 'Rust binary does not match its build receipt or product version; rebuild without -SkipBuild.'
    }
}
