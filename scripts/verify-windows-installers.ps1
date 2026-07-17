[CmdletBinding()]
param(
    [string[]]$ExpectedEditions = @("Document", "Sales", "Full")
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

$ExpectedEditions = @($ExpectedEditions |
    ForEach-Object { $_ -split "," } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique)
$invalidEditions = @($ExpectedEditions | Where-Object { $_ -notin @("Document", "Sales", "Full") })
if ($ExpectedEditions.Count -eq 0 -or $invalidEditions.Count -gt 0) {
    throw "ExpectedEditions must contain one or more of: Document, Sales, Full. Invalid values: $($invalidEditions -join ', ')"
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$outputRoot = Join-Path $repoRoot "artifacts\windows-installers"
$manifestPath = Join-Path $outputRoot "installers-manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Windows installer manifest was not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$results = @($manifest.installers | Where-Object { $_.Edition -in $ExpectedEditions })
if ($results.Count -ne $ExpectedEditions.Count) {
    throw "Installer manifest does not contain every expected edition."
}

foreach ($edition in $ExpectedEditions) {
    $result = $results | Where-Object Edition -eq $edition | Select-Object -First 1
    if ($null -eq $result) {
        throw "Installer metadata for $edition was not found."
    }
    if (-not (Test-Path -LiteralPath $result.Installer -PathType Leaf)) {
        throw "Installer for $edition was not found: $($result.Installer)"
    }
    $actualHash = (Get-FileHash -LiteralPath $result.Installer -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $result.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer SHA256 mismatch for $edition."
    }
    $editionManifestPath = Join-Path $outputRoot "product-edition-$edition.json"
    $editionManifest = Get-Content -LiteralPath $editionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($editionManifest.edition -ne $edition) {
        throw "Installer edition manifest mismatch for $edition."
    }
}

if (@($results | Select-Object -ExpandProperty Identifier -Unique).Count -ne $results.Count) {
    throw "Windows installers must use distinct application identifiers."
}
if (@($results | Select-Object -ExpandProperty Sha256 -Unique).Count -ne $results.Count) {
    throw "Windows installer files must have distinct SHA256 hashes."
}

[pscustomobject]@{
    Success = $true
    ManifestPath = $manifestPath
    Installers = $results
} | ConvertTo-Json -Depth 6
