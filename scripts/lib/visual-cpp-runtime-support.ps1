. (Join-Path $PSScriptRoot "microsoft-runtime-support.ps1")

function Read-ExportDocVisualCppRelease {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][ValidateSet('x64', 'arm64')][string]$Architecture
    )
    $release = Read-ExportDocMicrosoftRuntimeRelease -ManifestPath $ManifestPath
    $runtime = $release.runtimes.$Architecture
    $files = @($runtime.files)
    $names = @($files | ForEach-Object { [string]$_.name })
    if ($release.deployment -ne "app-local" -or $release.fileName -ne "VC_redist.x64.exe" -or
        $files.Count -ne 4 -or @($names | Select-Object -Unique).Count -ne 4 -or
        [long]$release.cabinet.offset -le 0 -or [long]$release.cabinet.bytes -le 0 -or
        ([long]$release.cabinet.offset + [long]$release.cabinet.bytes) -gt [long]$release.bytes -or
        [string]$runtime.payload -notmatch '\Aa[0-9]+\z' -or
        ($files | Measure-Object -Property bytes -Sum).Sum -gt 3MB) {
        throw "Invalid minimal Visual C++ release manifest: $ManifestPath"
    }
    $suffix = if ($Architecture -eq 'x64') { 'amd64' } else { 'arm64' }
    foreach ($file in $files) {
        if ([string]$file.name -notmatch '\A[a-z0-9_]+\.dll\z' -or
            [string]$file.sourceName -cne "$($file.name)_$suffix" -or [long]$file.bytes -le 0 -or
            [string]$file.sha256 -notmatch '\A[0-9a-fA-F]{64}\z') {
            throw "Invalid Visual C++ DLL entry: $($file.name)"
        }
    }
    $release.PSObject.Properties.Remove('runtimes')
    $release | Add-Member -NotePropertyName runtimeArchitecture -NotePropertyValue $Architecture
    $release | Add-Member -NotePropertyName files -NotePropertyValue $files
    $release.cabinet | Add-Member -NotePropertyName payload -NotePropertyValue $runtime.payload
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
