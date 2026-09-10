function Read-ExportDocMicrosoftRuntimeRelease {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "The pinned Microsoft runtime release manifest was not found: $ManifestPath"
    }

    $release = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceUri = [Uri][string]$release.sourceUrl
    if ($release.schemaVersion -ne 1 -or
        [string]$release.fileName -notmatch '\A[A-Za-z0-9._-]+\.exe\z' -or
        $release.architecture -ne "x64" -or
        [long]$release.bytes -lt 10MB -or
        [string]$release.originalFileName -notmatch '\A[A-Za-z0-9._-]+\.exe\z' -or
        [string]$release.sha256 -notmatch '\A[0-9a-fA-F]{64}\z' -or
        [string]::IsNullOrWhiteSpace([string]$release.fileVersion) -or
        -not $sourceUri.IsAbsoluteUri -or
        $sourceUri.Scheme -ne [Uri]::UriSchemeHttps -or
        -not ($sourceUri.Host -eq "microsoft.com" -or $sourceUri.Host.EndsWith(".microsoft.com", [StringComparison]::OrdinalIgnoreCase))) {
        throw "The pinned Microsoft runtime release manifest is invalid: $ManifestPath"
    }

    return $release
}

function Assert-ExportDocMicrosoftRuntimeFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][psobject]$Release
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Microsoft runtime file was not found: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    # Downloads use a temporary suffix until all content checks have passed.
    # The pinned hash and Microsoft metadata identify the installer, not its local name.
    if ($file.Length -ne [long]$Release.bytes) {
        throw "Microsoft runtime file size does not match the pinned release manifest: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw "Microsoft runtime file must have a valid Microsoft Corporation Authenticode signature: $Path"
    }

    $versionInfo = $file.VersionInfo
    if ($versionInfo.CompanyName -ne "Microsoft Corporation" -or
        $versionInfo.OriginalFilename -ne [string]$Release.originalFileName -or
        $versionInfo.FileVersion -ne [string]$Release.fileVersion) {
        throw "Microsoft runtime file metadata does not match the pinned release: $Path"
    }

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($hash, [string]$Release.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Microsoft runtime file SHA-256 does not match the pinned release manifest: $Path"
    }

    return [pscustomobject]@{
        File = $file
        VersionInfo = $versionInfo
        Hash = $hash.ToLowerInvariant()
    }
}
