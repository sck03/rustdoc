function Get-ExportDocProductEditionNames {
    $catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../product-editions.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    return @($catalog.editions.PSObject.Properties.Name)
}

function Resolve-ExportDocProductEdition {
    param([Parameter(Mandatory = $true)][string]$Edition)
    $names = @(Get-ExportDocProductEditionNames)
    $normalized = $names | Where-Object { $_ -eq $Edition.Trim() } | Select-Object -First 1
    if (-not $normalized) {
        throw "Unsupported product edition '$Edition'. Allowed values: $($names -join ', ')."
    }
    return $normalized
}
