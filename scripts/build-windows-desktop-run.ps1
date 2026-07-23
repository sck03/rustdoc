[CmdletBinding()]
param(
    [string]$CargoTargetDir,
    [string]$LicenseCargoTargetDir,
    [string]$ExcelAnalyzerCargoTargetDir,
    [string]$OutputDir,
    [string]$LicenseOutputDir,
    [string]$CargoBinDir,
    [ValidateSet("Document", "Sales", "Full")]
    [string]$ProductEdition = "Full",
    [string]$MsysUcrtBinDir = "D:\msys64\ucrt64\bin",
    [switch]$AllowSystemDrive,
    [switch]$SkipMainBuild,
    [switch]$IncludeLicenseKeygen,
    [switch]$SkipExcelAnalyzerBuild,
    [switch]$PreflightOnly,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
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

    return (Get-FullPath -Path $Path).StartsWith($systemDrive, [System.StringComparison]::OrdinalIgnoreCase)
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

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$mainBuildScript = Join-Path $scriptRoot "run-tauri-local.ps1"
$prepareScript = Join-Path $scriptRoot "prepare-windows-desktop-run.ps1"
$licenseRoot = Join-Path $repoRoot "apps\license-keygen-tauri"
$excelAnalyzerRoot = Join-Path $repoRoot "tools\excel-analyzer-rs"
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

$permissionVerifier = Join-Path $scriptRoot "assert-tauri-command-permissions.ps1"
Invoke-ExportDocExternal -FilePath (Resolve-ExportDocPowerShellExecutable) -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $permissionVerifier,
    "-RepositoryRoot", $repoRoot
)

Invoke-ExportDocExternal -FilePath "node" -Arguments @((Join-Path $scriptRoot "sync-version.mjs")) -WorkingDirectory $repoRoot

if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
        $CargoTargetDir = $env:CARGO_TARGET_DIR
    } else {
        $CargoTargetDir = Join-Path $artifactsRoot "cargo-target-exportdoc"
    }
}

if ([string]::IsNullOrWhiteSpace($LicenseCargoTargetDir)) {
    $LicenseCargoTargetDir = Join-Path $artifactsRoot "cargo-target-license-keygen"
}

if ([string]::IsNullOrWhiteSpace($ExcelAnalyzerCargoTargetDir)) {
    $ExcelAnalyzerCargoTargetDir = Join-Path $artifactsRoot "cargo-target-excel-analyzer"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $editionFolder = switch ($ProductEdition) {
        "Document" { "ExportDocManager-Document" }
        "Sales" { "ExportDocManager-Sales" }
        default { "ExportDocManager" }
    }
    $OutputDir = Join-Path $artifactsRoot ("windows-desktop-run\" + $editionFolder)
}

$resolvedCargoTargetDir = Get-FullPath -Path $CargoTargetDir
$resolvedLicenseCargoTargetDir = if ($IncludeLicenseKeygen) { Get-FullPath -Path $LicenseCargoTargetDir } else { $null }
$resolvedExcelAnalyzerCargoTargetDir = Get-FullPath -Path $ExcelAnalyzerCargoTargetDir
$resolvedOutputDir = Get-FullPath -Path $OutputDir
if ($IncludeLicenseKeygen -and [string]::IsNullOrWhiteSpace($LicenseOutputDir)) {
    $LicenseOutputDir = Join-Path (Split-Path -Parent $resolvedOutputDir) "KEY"
}

$resolvedLicenseOutputDir = if ($IncludeLicenseKeygen) { Get-FullPath -Path $LicenseOutputDir } else { $null }

Assert-NonSystemDrivePath -Path $resolvedCargoTargetDir -Purpose "Main Cargo target directory"
if ($IncludeLicenseKeygen) {
    if (-not (Test-Path -LiteralPath $licenseRoot -PathType Container)) {
        throw "Private license key generator source was not found. Keep it outside the public repository and restore it locally before using -IncludeLicenseKeygen: $licenseRoot"
    }
    Assert-NonSystemDrivePath -Path $resolvedLicenseCargoTargetDir -Purpose "License keygen Cargo target directory"
}
Assert-NonSystemDrivePath -Path $resolvedExcelAnalyzerCargoTargetDir -Purpose "Excel analyzer Cargo target directory"
Assert-NonSystemDrivePath -Path $resolvedOutputDir -Purpose "Windows desktop output directory"
if ($IncludeLicenseKeygen) {
    Assert-NonSystemDrivePath -Path $resolvedLicenseOutputDir -Purpose "License key generator output directory"
}

$resolvedCargoBinDir = Resolve-CargoBinDir
Assert-NonSystemDrivePath -Path $resolvedCargoBinDir -Purpose "Cargo binary directory"
$pathParts = New-Object System.Collections.Generic.List[string]
$pathParts.Add($resolvedCargoBinDir)
if (-not [string]::IsNullOrWhiteSpace($MsysUcrtBinDir) -and (Test-Path -LiteralPath $MsysUcrtBinDir)) {
    $pathParts.Add((Resolve-Path -LiteralPath $MsysUcrtBinDir).Path)
}
$pathParts.Add($env:PATH)
$env:PATH = $pathParts -join [System.IO.Path]::PathSeparator

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

$powerShellExecutable = Resolve-ExportDocPowerShellExecutable

if ($PreflightOnly) {
    [pscustomobject]@{
        Success = $true
        ProductEdition = $ProductEdition
        PowerShell = $powerShellExecutable
        CargoBinDir = $resolvedCargoBinDir
        CargoHome = $env:CARGO_HOME
        RustupHome = $env:RUSTUP_HOME
        CargoTargetDir = $resolvedCargoTargetDir
        LicenseCargoTargetDir = $resolvedLicenseCargoTargetDir
        ExcelAnalyzerCargoTargetDir = $resolvedExcelAnalyzerCargoTargetDir
        OutputDir = $resolvedOutputDir
        LicenseOutputDir = $resolvedLicenseOutputDir
        IncludesLicenseKeygen = [bool]$IncludeLicenseKeygen
        RuntimeDataCleanup = "unconditional"
    } | ConvertTo-Json -Depth 4
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
    return
}

if (-not $SkipMainBuild) {
    $mainBuildArgs = @(
        "-NoProfile",
        "-File",
        $mainBuildScript,
        "build",
        "-NoBundle",
        "-CargoTargetDir",
        $resolvedCargoTargetDir,
        "-CargoBinDir",
        $resolvedCargoBinDir,
        "-MsysUcrtBinDir",
        $MsysUcrtBinDir,
        "-ProductEdition",
        $ProductEdition
    )
    if ($AllowSystemDrive) {
        $mainBuildArgs += "-AllowSystemDrive"
    }

    Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $mainBuildArgs
}

if ($IncludeLicenseKeygen) {
    New-Item -ItemType Directory -Path $resolvedLicenseCargoTargetDir -Force | Out-Null
    $previousCargoTargetDir = $env:CARGO_TARGET_DIR
    try {
        $env:CARGO_TARGET_DIR = $resolvedLicenseCargoTargetDir
        if ([string]::IsNullOrWhiteSpace($env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL)) {
            $env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL = "sparse"
        }

        Invoke-ExportDocExternal -FilePath "npm" -Arguments @("run", "build:no-bundle") -WorkingDirectory $licenseRoot
    } finally {
        $env:CARGO_TARGET_DIR = $previousCargoTargetDir
    }
}

if (-not $SkipExcelAnalyzerBuild) {
    New-Item -ItemType Directory -Path $resolvedExcelAnalyzerCargoTargetDir -Force | Out-Null
    $previousCargoTargetDir = $env:CARGO_TARGET_DIR
    try {
        $env:CARGO_TARGET_DIR = $resolvedExcelAnalyzerCargoTargetDir
        if ([string]::IsNullOrWhiteSpace($env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL)) {
            $env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL = "sparse"
        }

        Invoke-ExportDocExternal -FilePath "cargo" -Arguments @("build", "--release", "--manifest-path", (Join-Path $excelAnalyzerRoot "Cargo.toml")) -WorkingDirectory $repoRoot
    } finally {
        $env:CARGO_TARGET_DIR = $previousCargoTargetDir
    }
}

$prepareArgs = @(
    "-NoProfile",
    "-File",
    $prepareScript,
    "-CargoTargetDir",
    $resolvedCargoTargetDir,
    "-ExcelAnalyzerCargoTargetDir",
    $resolvedExcelAnalyzerCargoTargetDir,
    "-OutputDir",
    $resolvedOutputDir,
    "-ProductEdition",
    $ProductEdition
)
if ($IncludeLicenseKeygen) {
    $prepareArgs += @(
        "-IncludeLicenseKeygen",
        "-LicenseCargoTargetDir",
        $resolvedLicenseCargoTargetDir,
        "-LicenseOutputDir",
        $resolvedLicenseOutputDir
    )
}
if ($SkipExcelAnalyzerBuild) {
    $prepareArgs += "-SkipExcelAnalyzer"
}
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $prepareArgs

$payloadVerifier = Join-Path $scriptRoot "verify-package-payload.ps1"
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $payloadVerifier,
    "-PackageRoot", $resolvedOutputDir,
    "-Profile", "Desktop",
    "-RuntimeIdentifier", "win-x64"
)

Write-Host "Complete Windows desktop run directory is ready:"
Write-Host "  $resolvedOutputDir"
if ($IncludeLicenseKeygen) {
    Write-Host "Internal license key generator directory:"
    Write-Host "  $resolvedLicenseOutputDir"
}
Write-Host "Run:"
Write-Host "  $(Join-Path $resolvedOutputDir "ExportDocManager.exe")"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
