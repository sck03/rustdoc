function Read-ExportDocWebView2Release {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "The pinned WebView2 Runtime release manifest was not found: $ManifestPath"
    }

    $release = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceUri = [Uri][string]$release.sourceUrl
    if ($release.schemaVersion -ne 1 -or
        $release.fileName -ne "MicrosoftEdgeWebView2RuntimeInstallerX64.exe" -or
        $release.architecture -ne "x64" -or
        [long]$release.bytes -lt 50MB -or
        [string]$release.sha256 -notmatch '\A[0-9a-fA-F]{64}\z' -or
        [string]::IsNullOrWhiteSpace([string]$release.fileVersion) -or
        -not $sourceUri.IsAbsoluteUri -or
        $sourceUri.Scheme -ne [Uri]::UriSchemeHttps -or
        -not ($sourceUri.Host -eq "microsoft.com" -or $sourceUri.Host.EndsWith(".microsoft.com", [StringComparison]::OrdinalIgnoreCase))) {
        throw "The pinned WebView2 Runtime release manifest is invalid: $ManifestPath"
    }

    return $release
}

function Assert-ExportDocWebView2Installer {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][psobject]$Release
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "WebView2 Runtime installer was not found: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    if ($file.Name -ne [string]$Release.fileName -or $file.Length -ne [long]$Release.bytes) {
        throw "WebView2 Runtime installer name or size does not match the pinned release manifest: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw "WebView2 Runtime installer must have a valid Microsoft Corporation Authenticode signature: $Path"
    }

    $versionInfo = $file.VersionInfo
    if ($versionInfo.CompanyName -ne "Microsoft Corporation" -or
        $versionInfo.OriginalFilename -ne "MicrosoftEdgeUpdateSetup.exe" -or
        $versionInfo.FileVersion -ne [string]$Release.fileVersion) {
        throw "WebView2 Runtime installer metadata does not match the pinned Microsoft release: $Path"
    }

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($hash, [string]$Release.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "WebView2 Runtime installer SHA-256 does not match the pinned release manifest: $Path"
    }

    return [pscustomobject]@{
        File = $file
        VersionInfo = $versionInfo
        Hash = $hash.ToLowerInvariant()
    }
}
