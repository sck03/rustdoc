# Native libraries are extracted from signed, hash-pinned package archives. No
# .NET SDK/runtime is required by the resulting Rust desktop or server package.
function Get-ExportDocNativeRuntimeLibrary {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)][string]$LibraryName
    )
    if ($PackageId -notmatch '\A[a-z0-9.]+\z' -or $RuntimeIdentifier -notmatch '\A(?:win|linux|osx)-(?:x64|arm64)\z' -or $LibraryName -notmatch '\A[a-zA-Z0-9._-]+\z') {
        throw 'Invalid governed native resource identity.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/native-runtime-packages.json') -Raw | ConvertFrom-Json
    $entry = $manifest.packages.PSObject.Properties[$PackageId].Value
    if ($manifest.schemaVersion -ne 1 -or $null -eq $entry) { throw 'Missing governed native resource package.' }
    $version = [string]$entry.version
    $expected = [string]$entry.sha512
    if ($version -notmatch '\A[0-9]+(?:\.[0-9]+){2,3}\z' -or [Convert]::FromBase64String($expected).Length -ne 64) { throw 'Invalid native resource manifest.' }
    $cacheRoot = Join-Path $RepositoryRoot '.codex-runtime/native-runtime-packages'
    Assert-NativePackagePath -Path $cacheRoot
    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
    $packageName = "$PackageId.$version.nupkg"
    $archivePath = Join-Path $cacheRoot $packageName
    Assert-NativePackagePath -Path $archivePath
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $RepositoryRoot '.codex-runtime/nuget-packages' }
        $cached = Join-Path $nugetRoot "$PackageId/$version/$packageName"
        if (Test-Path -LiteralPath $cached -PathType Leaf) {
            Assert-NativePackagePath -Path $cached
            Copy-Item -LiteralPath $cached -Destination $archivePath
        } else {
            $temporary = "$archivePath.$([Guid]::NewGuid().ToString('N')).download"
            try {
                Invoke-ExportDocExternal -FilePath 'curl' -Arguments @('--fail', '--location', '--connect-timeout', '20', '--max-time', '600', '--output', $temporary, "https://api.nuget.org/v3-flatcontainer/$PackageId/$version/$packageName") -DisplayName "Download governed native library $PackageId" -TimeoutSeconds 620
                $hash = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $temporary -Algorithm SHA512).Hash))
                if ($hash -cne $expected) { throw 'Native resource package does not match the locked SHA-512.' }
                Move-Item -LiteralPath $temporary -Destination $archivePath
            } finally {
                if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
            }
        }
    }
    $hash = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash))
    if ($hash -cne $expected) { throw 'Cached native resource package does not match the locked SHA-512.' }
    $libraryDirectory = Join-Path $cacheRoot "$PackageId-$version/$RuntimeIdentifier"
    Assert-NativePackagePath -Path $libraryDirectory
    New-Item -ItemType Directory -Force -Path $libraryDirectory | Out-Null
    $libraryPath = Join-Path $libraryDirectory $LibraryName
    Assert-NativePackagePath -Path $libraryPath
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry("runtimes/$RuntimeIdentifier/native/$LibraryName")
        if ($null -eq $entry -or $entry.Length -le 0 -or $entry.Length -gt 256MB) { throw 'The locked package does not contain the requested native library.' }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $libraryPath, $true)
        if ($PackageId -eq 'microsoft.ml.onnxruntime') {
            foreach ($name in @('LICENSE', 'ThirdPartyNotices.txt')) {
                $notice = $archive.GetEntry($name)
                if ($null -eq $notice -or $notice.Length -gt 16MB) { throw 'Native OCR package is missing its redistribution notice.' }
                $noticePath = Join-Path $libraryDirectory $name
                Assert-NativePackagePath -Path $noticePath
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($notice, $noticePath, $true)
            }
        }
    } finally { $archive.Dispose() }
    return $libraryPath
}
