[CmdletBinding()]
param(
    [string]$CargoTargetDir,
    [string]$LicenseCargoTargetDir,
    [string]$OutputDir,
    [string]$LicenseOutputDir,
    [string]$CargoBinDir,
    [ValidateSet("Document", "Sales", "Full")]
    [string]$ProductEdition = "Full",
    [string]$MsysUcrtBinDir = "D:\msys64\ucrt64\bin",
    [switch]$AllowSystemDrive,
    [switch]$SkipMainBuild,
    [switch]$IncludeLicenseKeygen,
    [switch]$SkipLaunchSmoke,
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
$smokeScript = Join-Path $scriptRoot "smoke-tauri-desktop.ps1"
$licenseRoot = Join-Path $repoRoot "apps\license-keygen-tauri"
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

$editionCatalogPath = Join-Path $scriptRoot "product-editions.json"
$editionCatalog = Get-Content -LiteralPath $editionCatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$editionMetadataProperty = $editionCatalog.editions.PSObject.Properties[$ProductEdition]
if ($null -eq $editionMetadataProperty) {
    throw "Product edition metadata was not found for $ProductEdition in $editionCatalogPath."
}
$editionMetadata = $editionMetadataProperty.Value
foreach ($profileKey in @("browserRenderer", "ocr", "documentResources", "excelAnalyzer")) {
    $profileProperty = $editionMetadata.resourceProfile.PSObject.Properties[$profileKey]
    if ($null -eq $profileProperty -or $profileProperty.Value -isnot [bool]) {
        throw "Product edition $ProductEdition has an invalid resource profile '$profileKey'."
    }
}

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
$resolvedOutputDir = Get-FullPath -Path $OutputDir
if ($IncludeLicenseKeygen -and [string]::IsNullOrWhiteSpace($LicenseOutputDir)) {
    $LicenseOutputDir = Join-Path (Split-Path -Parent $resolvedOutputDir) "KEY"
}

$resolvedLicenseOutputDir = if ($IncludeLicenseKeygen) { Get-FullPath -Path $LicenseOutputDir } else { $null }

Assert-ExportDocNonSystemDrivePath -Path $resolvedCargoTargetDir -Purpose "Main Cargo target directory" -AllowSystemDrive:$AllowSystemDrive
if ($IncludeLicenseKeygen) {
    if (-not (Test-Path -LiteralPath $licenseRoot -PathType Container)) {
        throw "Private license key generator source was not found. Keep it outside the public repository and restore it locally before using -IncludeLicenseKeygen: $licenseRoot"
    }
    Assert-ExportDocNonSystemDrivePath -Path $resolvedLicenseCargoTargetDir -Purpose "License keygen Cargo target directory" -AllowSystemDrive:$AllowSystemDrive
}
Assert-ExportDocNonSystemDrivePath -Path $resolvedOutputDir -Purpose "Windows desktop output directory" -AllowSystemDrive:$AllowSystemDrive
if ($IncludeLicenseKeygen) {
    Assert-ExportDocNonSystemDrivePath -Path $resolvedLicenseOutputDir -Purpose "License key generator output directory" -AllowSystemDrive:$AllowSystemDrive
}

$resolvedCargoBinDir = Resolve-CargoBinDir
Assert-ExportDocNonSystemDrivePath -Path $resolvedCargoBinDir -Purpose "Cargo binary directory" -AllowSystemDrive:$AllowSystemDrive
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

Assert-ExportDocNonSystemDrivePath -Path $env:CARGO_HOME -Purpose "Cargo home directory" -AllowSystemDrive:$AllowSystemDrive
if (-not [string]::IsNullOrWhiteSpace($env:RUSTUP_HOME)) {
    Assert-ExportDocNonSystemDrivePath -Path $env:RUSTUP_HOME -Purpose "Rustup home directory" -AllowSystemDrive:$AllowSystemDrive
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
        OutputDir = $resolvedOutputDir
        LicenseOutputDir = $resolvedLicenseOutputDir
        IncludesLicenseKeygen = [bool]$IncludeLicenseKeygen
        ResourceProfile = $editionMetadata.resourceProfile
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

$prepareArgs = @(
    "-NoProfile",
    "-File",
    $prepareScript,
    "-CargoTargetDir",
    $resolvedCargoTargetDir,
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
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $prepareArgs

$payloadVerifier = Join-Path $scriptRoot "verify-package-payload.ps1"
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $payloadVerifier,
    "-PackageRoot", $resolvedOutputDir,
    "-Profile", "Desktop",
    "-RuntimeIdentifier", "win-x64",
    "-Edition", $ProductEdition
)

if (-not $SkipLaunchSmoke) {
    $smokeRoot = Get-FullPath -Path (Join-Path $resolvedOutputDir "App_Data")
    $resolvedOutputRoot = $resolvedOutputDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $smokeRoot.StartsWith($resolvedOutputRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Windows desktop launch smoke data directory escaped the portable output root: $smokeRoot"
    }
    Assert-ExportDocNonSystemDrivePath -Path $smokeRoot -Purpose "Windows desktop launch smoke data directory" -AllowSystemDrive:$AllowSystemDrive
    try {
        Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", $smokeScript,
            "-ExecutablePath", (Join-Path $resolvedOutputDir "ExportDocManager.exe"),
            "-AppRoot", $resolvedOutputDir,
            "-UseDefaultAppRoot",
            "-UsePortableDataRoot",
            "-SkipVite",
            "-TimeoutSeconds", "60"
        )
    } finally {
        if (Test-Path -LiteralPath $smokeRoot) {
            Remove-ExportDocDirectoryWithRetry `
                -Path $smokeRoot `
                -AllowedRoot $artifactsRoot `
                -QuarantineRoot (Join-Path $artifactsRoot "runtime-cleanup-quarantine")
        }
    }
}

Write-Host "Complete Windows desktop run directory is ready:"
Write-Host "  $resolvedOutputDir"
if ($IncludeLicenseKeygen) {
    Write-Host "Internal license key generator directory:"
    Write-Host "  $resolvedLicenseOutputDir"
}
Write-Host "Run:"
Write-Host "  $(Join-Path $resolvedOutputDir "ExportDocManager.exe")"
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
