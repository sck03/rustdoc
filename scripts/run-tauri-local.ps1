[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("info", "dev", "build", "cargo-check", "prepare-bundle")]
    [string]$Command = "info",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @(),

    [switch]$BuildDebug,

    [switch]$NoBundle,

    [ValidateSet("msi", "nsis")]
    [string[]]$Bundles = @(),

    [switch]$NoSign,

    [string]$CargoBinDir,

    [string]$MsysUcrtBinDir = "D:\msys64\ucrt64\bin",

    [string]$CargoTargetDir,

    [ValidateSet("Document", "Sales", "Full")]
    [string]$ProductEdition = "Full",

    [string]$Config,

    [switch]$TauriVerbose,

    [switch]$AllowSystemDrive
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-SystemDrivePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $systemDrive = $env:SystemDrive
    if ([string]::IsNullOrWhiteSpace($systemDrive)) {
        return $false
    }

    $fullPath = Get-FullPath -Path $Path
    return $fullPath.StartsWith($systemDrive, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NonSystemDrivePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (-not $AllowSystemDrive -and (Test-SystemDrivePath -Path $Path)) {
        throw "$Purpose resolved to the system drive: $Path. Pass -AllowSystemDrive only for an intentional local override."
    }
}

function Resolve-CargoBinDir {
    if (-not [string]::IsNullOrWhiteSpace($CargoBinDir)) {
        if (-not (Test-Path -LiteralPath (Join-Path $CargoBinDir "cargo.exe"))) {
            throw "cargo.exe was not found under CargoBinDir: $CargoBinDir"
        }

        return (Resolve-Path -LiteralPath $CargoBinDir).Path
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_HOME)) {
        $candidates.Add((Join-Path $env:CARGO_HOME "bin"))
    }

    $candidates.Add("D:\Rust\.cargo\bin")

    $cargoCommand = Get-Command cargo.exe -ErrorAction SilentlyContinue
    if ($null -ne $cargoCommand) {
        $candidates.Add((Split-Path -Parent $cargoCommand.Source))
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath (Join-Path $candidate "cargo.exe"))) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "cargo.exe was not found. Set -CargoBinDir or install Rust/Cargo outside the system drive."
}

if ($RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -eq "--") {
    $RemainingArgs = @($RemainingArgs | Select-Object -Skip 1)
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$tauriRoot = Join-Path $repoRoot "apps\export-doc-tauri"
$srcTauriRoot = Join-Path $tauriRoot "src-tauri"
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

Invoke-ExportDocExternal -FilePath "node" -Arguments @((Join-Path $scriptRoot "sync-version.mjs"))

$resolvedCargoBinDir = Resolve-CargoBinDir
Assert-NonSystemDrivePath -Path $resolvedCargoBinDir -Purpose "Cargo binary directory"
$cargoExe = Join-Path $resolvedCargoBinDir "cargo.exe"
$rustcExe = Join-Path $resolvedCargoBinDir "rustc.exe"
if (-not (Test-Path -LiteralPath $rustcExe)) {
    throw "rustc.exe was not found beside cargo.exe: $resolvedCargoBinDir"
}

if ([string]::IsNullOrWhiteSpace($env:CARGO_HOME)) {
    $env:CARGO_HOME = (Split-Path -Parent $resolvedCargoBinDir)
}

if ([string]::IsNullOrWhiteSpace($env:RUSTUP_HOME)) {
    $rustupHomeCandidate = Join-Path (Split-Path -Parent $env:CARGO_HOME) ".rustup"
    if (Test-Path -LiteralPath $rustupHomeCandidate) {
        $env:RUSTUP_HOME = (Resolve-Path -LiteralPath $rustupHomeCandidate).Path
    }
}

Assert-NonSystemDrivePath -Path $env:CARGO_HOME -Purpose "Cargo home directory"
if (-not [string]::IsNullOrWhiteSpace($env:RUSTUP_HOME)) {
    Assert-NonSystemDrivePath -Path $env:RUSTUP_HOME -Purpose "Rustup home directory"
}

if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
        $CargoTargetDir = $env:CARGO_TARGET_DIR
    } else {
        $CargoTargetDir = Join-Path $repoRoot "artifacts\cargo-target-exportdoc"
    }
}

$resolvedCargoTargetDir = Get-FullPath -Path $CargoTargetDir
Assert-NonSystemDrivePath -Path $resolvedCargoTargetDir -Purpose "Cargo target directory"
New-Item -ItemType Directory -Path $resolvedCargoTargetDir -Force | Out-Null
$env:CARGO_TARGET_DIR = $resolvedCargoTargetDir
$env:EXPORTDOCMANAGER_PRODUCT_EDITION = $ProductEdition

if ([string]::IsNullOrWhiteSpace($env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL)) {
    $env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL = "sparse"
}

$pathParts = New-Object System.Collections.Generic.List[string]
$pathParts.Add($resolvedCargoBinDir)
if (-not [string]::IsNullOrWhiteSpace($MsysUcrtBinDir) -and (Test-Path -LiteralPath $MsysUcrtBinDir)) {
    $pathParts.Add((Resolve-Path -LiteralPath $MsysUcrtBinDir).Path)
}
$pathParts.Add($env:PATH)
$env:PATH = $pathParts -join [System.IO.Path]::PathSeparator

Write-Host "Tauri local build environment"
Write-Host "  CargoBinDir     $resolvedCargoBinDir"
Write-Host "  CARGO_HOME      $env:CARGO_HOME"
Write-Host "  RUSTUP_HOME     $env:RUSTUP_HOME"
Write-Host "  CARGO_TARGET_DIR $env:CARGO_TARGET_DIR"
Write-Host "  ProductEdition  $env:EXPORTDOCMANAGER_PRODUCT_EDITION"
Invoke-ExportDocExternal -FilePath $cargoExe -Arguments @("--version")
Invoke-ExportDocExternal -FilePath $rustcExe -Arguments @("--version")

switch ($Command) {
    "cargo-check" {
        Push-Location $srcTauriRoot
        try {
            Invoke-ExportDocExternal -FilePath $cargoExe -Arguments (@("check") + $RemainingArgs)
        } finally {
            Pop-Location
        }
        break
    }

    "prepare-bundle" {
        Push-Location $tauriRoot
        try {
            Invoke-ExportDocExternal -FilePath "npm" -Arguments (@("run", "prepare:bundle") + $RemainingArgs)
        } finally {
            Pop-Location
        }
        break
    }

    default {
        $tauriArgs = New-Object System.Collections.Generic.List[string]
        if ($BuildDebug) {
            $tauriArgs.Add("--debug")
        }
        if ($NoBundle) {
            $tauriArgs.Add("--no-bundle")
        }
        if ($Bundles.Count -gt 0) {
            $tauriArgs.Add("--bundles")
            foreach ($bundle in $Bundles) {
                $tauriArgs.Add($bundle)
            }
        }
        if ($NoSign) {
            $tauriArgs.Add("--no-sign")
        }
        if (-not [string]::IsNullOrWhiteSpace($Config)) {
            $tauriArgs.Add("--config")
            $tauriArgs.Add((Get-FullPath -Path $Config))
        }
        if ($TauriVerbose) {
            $tauriArgs.Add("--verbose")
        }
        foreach ($argument in $RemainingArgs) {
            $tauriArgs.Add($argument)
        }

        Push-Location $tauriRoot
        try {
            Invoke-ExportDocExternal -FilePath "npm" -Arguments (@("run", "tauri", "--", $Command) + $tauriArgs.ToArray())
        } finally {
            Pop-Location
        }
        break
    }
}
