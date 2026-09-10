. (Join-Path $PSScriptRoot "microsoft-runtime-support.ps1")

function Read-ExportDocVisualCppRelease {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)
    $release = Read-ExportDocMicrosoftRuntimeRelease -ManifestPath $ManifestPath
    $files = @($release.files)
    $names = @($files | ForEach-Object { [string]$_.name })
    if ($release.deployment -ne "app-local" -or $release.fileName -ne "VC_redist.x64.exe" -or
        $files.Count -ne 4 -or @($names | Select-Object -Unique).Count -ne 4 -or
        [long]$release.cabinet.offset -le 0 -or [long]$release.cabinet.bytes -le 0 -or
        ([long]$release.cabinet.offset + [long]$release.cabinet.bytes) -gt [long]$release.bytes -or
        [string]$release.cabinet.payload -notmatch '\Aa[0-9]+\z' -or
        ($files | Measure-Object -Property bytes -Sum).Sum -gt 1MB) {
        throw "Invalid minimal Visual C++ release manifest: $ManifestPath"
    }
    foreach ($file in $files) {
        if ([string]$file.name -notmatch '\A[a-z0-9_]+\.dll\z' -or
            [string]$file.sourceName -cne "$($file.name)_amd64" -or [long]$file.bytes -le 0 -or
            [string]$file.sha256 -notmatch '\A[0-9a-fA-F]{64}\z') {
            throw "Invalid Visual C++ DLL entry: $($file.name)"
        }
    }
    return $release
}

function Assert-ExportDocVisualCppFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][psobject]$Release,
        [switch]$ExtractedNames
    )
    foreach ($file in $Release.files) {
        $name = if ($ExtractedNames) { [string]$file.sourceName } else { [string]$file.name }
        $expected = [pscustomobject]@{
            bytes = $file.bytes
            sha256 = $file.sha256
            fileVersion = $Release.fileVersion
            originalFileName = $file.name
        }
        Assert-ExportDocMicrosoftRuntimeFile -Path (Join-Path $Directory $name) -Release $expected | Out-Null
    }
}
