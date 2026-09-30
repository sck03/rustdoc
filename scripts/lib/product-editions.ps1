function Get-ExportDocProductEditionNames {
    param([switch]$IncludeLocalTests)
    $catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../product-editions.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $names = @($catalog.editions.PSObject.Properties.Name)
    if ($IncludeLocalTests) { $names += @($catalog.localTestEditions.PSObject.Properties.Name) }
    return $names
}

function Resolve-ExportDocProductEdition {
    param([Parameter(Mandatory = $true)][string]$Edition, [switch]$IncludeLocalTests)
    $names = @(Get-ExportDocProductEditionNames -IncludeLocalTests:$IncludeLocalTests)
    $normalized = $names | Where-Object { $_ -eq $Edition.Trim() } | Select-Object -First 1
    if (-not $normalized) {
        throw "Unsupported product edition '$Edition'. Allowed values: $($names -join ', ')."
    }
    return $normalized
}
