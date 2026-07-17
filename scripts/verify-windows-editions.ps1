[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$RequireInternalLicenseKeygen
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\windows-desktop-run"
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$results = New-Object System.Collections.Generic.List[object]
$powerShellExecutable = Resolve-ExportDocPowerShellExecutable

$permissionVerifier = Join-Path $scriptRoot "assert-tauri-command-permissions.ps1"
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $permissionVerifier,
    "-RepositoryRoot", $repoRoot
)

foreach ($edition in @("Document", "Sales", "Full")) {
    $folderName = switch ($edition) {
        "Document" { "ExportDocManager-Document" }
        "Sales" { "ExportDocManager-Sales" }
        default { "ExportDocManager" }
    }
    $editionRoot = Join-Path $outputRoot $folderName
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
    $files = @(Get-ChildItem -LiteralPath $editionRoot -Recurse -File)
    $mainExe = Join-Path $editionRoot "ExportDocManager.exe"
    $results.Add([pscustomobject]@{
        Edition = $edition
        DisplayName = $editionManifest.displayName
        ProductVersion = $editionManifest.productVersion
        EnabledWorkspaces = @($editionManifest.enabledWorkspaces)
        Directory = $editionRoot
        MainExecutableSha256 = (Get-FileHash -LiteralPath $mainExe -Algorithm SHA256).Hash
        FileCount = $files.Count
        Bytes = ($files | Measure-Object Length -Sum).Sum
    })
}

if (@($results | Select-Object -ExpandProperty MainExecutableSha256 -Unique).Count -ne 3) {
    throw "The three edition executables do not have distinct SHA256 hashes. Build-time product edition may not have been applied."
}

$licenseKeygen = Join-Path $outputRoot "KEY\ExportDocLicenseKeyGen.exe"
if ($RequireInternalLicenseKeygen -and -not (Test-Path -LiteralPath $licenseKeygen -PathType Leaf)) {
    throw "Internal license key generator was not found: $licenseKeygen"
}
foreach ($result in $results) {
    $packagedKeygen = Join-Path $result.Directory "Tools\ExportDocLicenseKeyGen.exe"
    if (Test-Path -LiteralPath $packagedKeygen) {
        throw "License key generator must not be packaged in customer edition: $packagedKeygen"
    }
}

$manifestPath = Join-Path $outputRoot "editions-manifest.json"
[ordered]@{
    schemaVersion = 1
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    editions = $results
    internalLicenseKeygen = if (Test-Path -LiteralPath $licenseKeygen -PathType Leaf) { $licenseKeygen } else { $null }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

[pscustomobject]@{
    Success = $true
    ManifestPath = $manifestPath
    Editions = $results
    InternalLicenseKeygen = if (Test-Path -LiteralPath $licenseKeygen -PathType Leaf) { $licenseKeygen } else { $null }
} | ConvertTo-Json -Depth 6
