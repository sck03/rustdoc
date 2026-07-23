[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$IncludeLicenseKeygen,
    [switch]$AllowSystemDrive,
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

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $artifactsRoot "windows-desktop-run"
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$artifactsRootFullPath = [System.IO.Path]::GetFullPath($artifactsRoot)
$artifactsPrefix = $artifactsRootFullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Windows edition output must stay inside $artifactsRootFullPath. Resolved path: $outputRoot"
}
$builder = Join-Path $scriptRoot "build-windows-desktop-run.ps1"
$first = $true
$powerShellExecutable = Resolve-ExportDocPowerShellExecutable

$permissionVerifier = Join-Path $scriptRoot "assert-tauri-command-permissions.ps1"
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $permissionVerifier,
    "-RepositoryRoot", $repoRoot
)

if ($PreflightOnly) {
    foreach ($edition in @("Document", "Sales", "Full")) {
        $folderName = switch ($edition) {
            "Document" { "ExportDocManager-Document" }
            "Sales" { "ExportDocManager-Sales" }
            default { "ExportDocManager" }
        }
        $arguments = @(
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", $builder,
            "-ProductEdition", $edition,
            "-OutputDir", (Join-Path $outputRoot $folderName),
            "-PreflightOnly",
            "-NoPause"
        )
        if ($AllowSystemDrive) {
            $arguments += "-AllowSystemDrive"
        }
        if ($IncludeLicenseKeygen) {
            $arguments += "-IncludeLicenseKeygen"
        }
        Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $arguments
    }
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
    return
}

if (-not $IncludeLicenseKeygen) {
    $staleLicenseOutput = Join-Path $outputRoot "KEY"
    if (Test-Path -LiteralPath $staleLicenseOutput) {
        $staleLicenseOutputFullPath = [System.IO.Path]::GetFullPath($staleLicenseOutput)
        $outputPrefix = $outputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $staleLicenseOutputFullPath.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Stale license key generator cleanup escaped the edition output root: $staleLicenseOutputFullPath"
        }
        Remove-Item -LiteralPath $staleLicenseOutputFullPath -Recurse -Force
    }
}

foreach ($edition in @("Document", "Sales", "Full")) {
    $folderName = switch ($edition) {
        "Document" { "ExportDocManager-Document" }
        "Sales" { "ExportDocManager-Sales" }
        default { "ExportDocManager" }
    }
    $editionRoot = Join-Path $outputRoot $folderName

    $arguments = @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", $builder,
        "-ProductEdition", $edition,
        "-OutputDir", $editionRoot,
        "-NoPause"
    )
    if ($IncludeLicenseKeygen -and $first) {
        $arguments += "-IncludeLicenseKeygen"
    }
    if ($AllowSystemDrive) {
        $arguments += "-AllowSystemDrive"
    }
    Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $arguments

    $editionManifestPath = Join-Path $editionRoot "product-edition.json"
    $requiredFiles = @(
        (Join-Path $editionRoot "ExportDocManager.exe"),
        (Join-Path $editionRoot "sidecar\ExportDocManager.Api.exe"),
        (Join-Path $editionRoot "runtime-layout.json"),
        $editionManifestPath,
        (Join-Path $editionRoot "Tools\exportdoc-excel-analyzer.exe")
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Required $edition artifact was not found: $requiredFile"
        }
    }

    $editionManifest = Get-Content -LiteralPath $editionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($editionManifest.edition -ne $edition) {
        throw "Edition manifest mismatch in '$editionManifestPath'. Expected '$edition', got '$($editionManifest.edition)'."
    }
    $first = $false
}

$verifier = Join-Path $scriptRoot "verify-windows-editions.ps1"
    $verifyArguments = @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $verifier,
    "-OutputRoot", $outputRoot
)
if ($IncludeLicenseKeygen) {
    $verifyArguments += "-RequireInternalLicenseKeygen"
}
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $verifyArguments
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
