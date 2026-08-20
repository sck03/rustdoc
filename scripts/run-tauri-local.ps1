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

    [string]$MsysUcrtBinDir,

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

function Resolve-CargoBinDir {
    $cargoExecutableName = if ($IsWindows) { "cargo.exe" } else { "cargo" }
    if (-not [string]::IsNullOrWhiteSpace($CargoBinDir)) {
        if (-not (Test-Path -LiteralPath (Join-Path $CargoBinDir $cargoExecutableName) -PathType Leaf)) {
            throw "$cargoExecutableName was not found under CargoBinDir: $CargoBinDir"
        }

        return (Resolve-Path -LiteralPath $CargoBinDir).Path
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_HOME)) {
        $candidates.Add((Join-Path $env:CARGO_HOME "bin"))
    }

    $cargoCommand = Get-Command $cargoExecutableName -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $cargoCommand) {
        $candidates.Add((Split-Path -Parent $cargoCommand.Source))
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath (Join-Path $candidate $cargoExecutableName) -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "$cargoExecutableName was not found. Set -CargoBinDir or install Rust/Cargo."
}

function Ensure-NpmProjectDependencies {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string[]]$RequiredRelativePaths,
        [Parameter(Mandatory = $true)][string]$ProjectName
    )

    $missingPaths = @($RequiredRelativePaths | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $ProjectRoot $_) -PathType Leaf)
    })
    if ($missingPaths.Count -eq 0) {
        return
    }

    $packageLockPath = Join-Path $ProjectRoot "package-lock.json"
    if (-not (Test-Path -LiteralPath $packageLockPath -PathType Leaf)) {
        throw "$ProjectName dependencies are missing and package-lock.json was not found: $packageLockPath"
    }

    Write-Host "$ProjectName npm dependencies are missing; restoring the locked dependency tree..."
    Invoke-ExportDocExternal `
        -FilePath "npm" `
        -Arguments @("ci", "--no-audit", "--no-fund") `
        -WorkingDirectory $ProjectRoot

    $unrestoredPaths = @($RequiredRelativePaths | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $ProjectRoot $_) -PathType Leaf)
    })
    if ($unrestoredPaths.Count -gt 0) {
        throw "$ProjectName dependency restore completed without required files: $($unrestoredPaths -join ', ')"
    }
}

function Enter-TauriLocalBuildLock {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $lockRoot = Join-Path $RepositoryRoot ".codex-runtime\locks"
    New-Item -ItemType Directory -Path $lockRoot -Force | Out-Null
    $lockPath = Join-Path $lockRoot "tauri-local-build.lock"

    try {
        $stream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    } catch [System.IO.IOException] {
        throw "Another local Tauri build is already using the shared Cargo and bundle output. Wait for that build to finish, then run this command once: $lockPath"
    }

    $metadata = "pid=$PID`nstarted=$([DateTimeOffset]::Now.ToString('O'))`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($metadata)
    $stream.SetLength(0)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush($true)
    return $stream
}

if ($RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -eq "--") {
    $RemainingArgs = @($RemainingArgs | Select-Object -Skip 1)
}
if ($IsWindows -and [string]::IsNullOrWhiteSpace($MsysUcrtBinDir)) {
    $MsysUcrtBinDir = $env:EXPORTDOCMANAGER_MSYS_UCRT_BIN
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$tauriRoot = Join-Path $repoRoot "apps\export-doc-tauri"
$webRoot = Join-Path $repoRoot "apps\export-doc-web"
$srcTauriRoot = Join-Path $tauriRoot "src-tauri"
$tauriCliPath = Join-Path $tauriRoot "node_modules\@tauri-apps\cli\tauri.js"
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot

$tauriBuildLock = $null
if ($Command -in @("dev", "build", "prepare-bundle")) {
    $tauriBuildLock = Enter-TauriLocalBuildLock -RepositoryRoot $repoRoot
}

try {
    if ($Command -in @("info", "dev", "build")) {
        Ensure-NpmProjectDependencies `
            -ProjectRoot $tauriRoot `
            -ProjectName "Tauri desktop" `
            -RequiredRelativePaths @("node_modules\@tauri-apps\cli\tauri.js")
    }
    if ($Command -in @("dev", "build", "prepare-bundle")) {
        Ensure-NpmProjectDependencies `
            -ProjectRoot $webRoot `
            -ProjectName "Web frontend" `
            -RequiredRelativePaths @(
                "node_modules\typescript\bin\tsc",
                "node_modules\vite\bin\vite.js"
            )
    }

    Invoke-ExportDocExternal `
        -FilePath "node" `
        -Arguments @((Join-Path $scriptRoot "sync-version.mjs")) `
        -WorkingDirectory $repoRoot

    $resolvedCargoBinDir = Resolve-CargoBinDir
    Assert-ExportDocNonSystemDrivePath -Path $resolvedCargoBinDir -Purpose "Cargo binary directory" -AllowSystemDrive:$AllowSystemDrive
    $cargoExe = Join-Path $resolvedCargoBinDir $(if ($IsWindows) { "cargo.exe" } else { "cargo" })
    $rustcExe = Join-Path $resolvedCargoBinDir $(if ($IsWindows) { "rustc.exe" } else { "rustc" })
    if (-not (Test-Path -LiteralPath $rustcExe)) {
        throw "rustc was not found beside cargo: $resolvedCargoBinDir"
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

    Assert-ExportDocNonSystemDrivePath -Path $env:CARGO_HOME -Purpose "Cargo home directory" -AllowSystemDrive:$AllowSystemDrive
    if (-not [string]::IsNullOrWhiteSpace($env:RUSTUP_HOME)) {
        Assert-ExportDocNonSystemDrivePath -Path $env:RUSTUP_HOME -Purpose "Rustup home directory" -AllowSystemDrive:$AllowSystemDrive
    }

    if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
        if (-not [string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
            $CargoTargetDir = $env:CARGO_TARGET_DIR
        } else {
            $CargoTargetDir = Join-Path $repoRoot "artifacts\cargo-target-exportdoc"
        }
    }

    $resolvedCargoTargetDir = Get-FullPath -Path $CargoTargetDir
    Assert-ExportDocNonSystemDrivePath -Path $resolvedCargoTargetDir -Purpose "Cargo target directory" -AllowSystemDrive:$AllowSystemDrive
    New-Item -ItemType Directory -Path $resolvedCargoTargetDir -Force | Out-Null
    $env:CARGO_TARGET_DIR = $resolvedCargoTargetDir
    $env:EXPORTDOCMANAGER_PRODUCT_EDITION = $ProductEdition

    if ([string]::IsNullOrWhiteSpace($env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL)) {
        $env:CARGO_REGISTRIES_CRATES_IO_PROTOCOL = "sparse"
    }
    if ([string]::IsNullOrWhiteSpace($env:CARGO_BUILD_JOBS)) {
        # Windows GNU linking can need several GiB per rustc process. Keep
        # local builds single-flight on ordinary development machines.
        $env:CARGO_BUILD_JOBS = "1"
    }

    $pathParts = New-Object System.Collections.Generic.List[string]
    $pathParts.Add($resolvedCargoBinDir)
    if (-not [string]::IsNullOrWhiteSpace($MsysUcrtBinDir)) {
        if (-not (Test-Path -LiteralPath $MsysUcrtBinDir -PathType Container)) {
            throw "MSYS2 UCRT tool directory was not found: $MsysUcrtBinDir"
        }
        $resolvedMsysUcrtBinDir = (Resolve-Path -LiteralPath $MsysUcrtBinDir).Path
        Assert-ExportDocNonSystemDrivePath -Path $resolvedMsysUcrtBinDir -Purpose "MSYS2 UCRT tool directory" -AllowSystemDrive:$AllowSystemDrive
        $pathParts.Add($resolvedMsysUcrtBinDir)
    }
    $pathParts.Add($env:PATH)
    $env:PATH = $pathParts -join [System.IO.Path]::PathSeparator

    Write-Host "Tauri local build environment"
    Write-Host "  CargoBinDir      $resolvedCargoBinDir"
    Write-Host "  CARGO_HOME       $env:CARGO_HOME"
    Write-Host "  RUSTUP_HOME      $env:RUSTUP_HOME"
    Write-Host "  CARGO_TARGET_DIR $env:CARGO_TARGET_DIR"
    Write-Host "  CARGO_BUILD_JOBS $env:CARGO_BUILD_JOBS"
    Write-Host "  ProductEdition   $env:EXPORTDOCMANAGER_PRODUCT_EDITION"
    Invoke-ExportDocExternal -FilePath $cargoExe -Arguments @("--version")
    Invoke-ExportDocExternal -FilePath $rustcExe -Arguments @("--version")

    switch ($Command) {
        "cargo-check" {
            Invoke-ExportDocExternal `
                -FilePath $cargoExe `
                -Arguments (@("check") + $RemainingArgs) `
                -WorkingDirectory $srcTauriRoot
            break
        }

        "prepare-bundle" {
            Invoke-ExportDocExternal `
                -FilePath "npm" `
                -Arguments (@("run", "prepare:bundle") + $RemainingArgs) `
                -WorkingDirectory $tauriRoot
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

            Invoke-ExportDocExternal `
                -FilePath "node" `
                -Arguments (@($tauriCliPath, $Command) + $tauriArgs.ToArray()) `
                -WorkingDirectory $tauriRoot
            break
        }
    }
} finally {
    if ($null -ne $tauriBuildLock) {
        $tauriBuildLock.Dispose()
    }
}
