[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DestinationDirectory)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib/build-script-support.ps1")
. (Join-Path $PSScriptRoot "lib/visual-cpp-runtime-support.ps1")
if (-not $IsWindows) { throw "Visual C++ app-local provisioning requires Windows." }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Assert-WorkspacePath {
    param([string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-ExportDocPathUnderRoot -Path $fullPath -Root $repositoryRoot)) {
        throw "Runtime asset path must remain inside the workspace: $fullPath"
    }
    for ($entry = $fullPath; $entry; $entry = [IO.Path]::GetDirectoryName($entry)) {
        if ((Test-Path -LiteralPath $entry) -and
            ((Get-Item -LiteralPath $entry -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Runtime asset path must not traverse a link: $entry"
        }
    }
    return $fullPath
}

$manifestPath = Join-Path $repositoryRoot "VisualCppRuntime/visual-cpp-runtime.json"
$release = Read-ExportDocVisualCppRelease -ManifestPath $manifestPath
$destination = Assert-WorkspacePath $DestinationDirectory
$cacheRoot = Assert-WorkspacePath (Join-Path $repositoryRoot "artifacts/tool-downloads/visual-cpp")
New-Item -ItemType Directory -Path $destination, $cacheRoot -Force | Out-Null
$installerPath = Assert-WorkspacePath (Join-Path $cacheRoot $release.fileName)
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    $downloadPath = Assert-WorkspacePath "$installerPath.download"
    Invoke-ExportDocExternal -FilePath "curl.exe" -Arguments @(
        "-L", "--fail", "--proto", "=https", "--proto-redir", "=https",
        "--connect-timeout", "20", "--max-time", "300", "--retry", "2",
        "-o", $downloadPath, [string]$release.sourceUrl
    ) -DisplayName "Download official Visual C++ build-time source" -TimeoutSeconds 960
    Assert-ExportDocMicrosoftRuntimeFile -Path $downloadPath -Release $release | Out-Null
    Move-Item -LiteralPath $downloadPath -Destination $installerPath -Force
}
Assert-ExportDocMicrosoftRuntimeFile -Path $installerPath -Release $release | Out-Null

# The pinned cabinet coordinates are part of the reviewed Microsoft release.
# Extract data with the Windows CAB reader; never run the full installer.
$staging = Assert-WorkspacePath (Join-Path $cacheRoot $release.sha256)
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$cabinetPath = Assert-WorkspacePath (Join-Path $staging "runtime.cab")
$sourceBytes = [IO.File]::ReadAllBytes($installerPath)
$cabinetBytes = [byte[]]::new([int]$release.cabinet.bytes)
[Buffer]::BlockCopy($sourceBytes, [int]$release.cabinet.offset, $cabinetBytes, 0, $cabinetBytes.Length)
if ([Text.Encoding]::ASCII.GetString($cabinetBytes, 0, 4) -ne "MSCF" -or
    [BitConverter]::ToUInt32($cabinetBytes, 8) -ne $cabinetBytes.Length) {
    throw "Visual C++ source cabinet does not match the pinned release."
}
[IO.File]::WriteAllBytes($cabinetPath, $cabinetBytes)
$expand = Join-Path ([Environment]::GetFolderPath("System")) "expand.exe"
$payloadPath = Assert-WorkspacePath (Join-Path $staging $release.cabinet.payload)
Invoke-ExportDocExternal -FilePath $expand -Arguments @(
    "-F:$($release.cabinet.payload)", $cabinetPath, $staging
) -TimeoutSeconds 60 -CaptureOutput | Out-Null
foreach ($file in $release.files) {
    [void](Assert-WorkspacePath (Join-Path $staging $file.sourceName))
    Invoke-ExportDocExternal -FilePath $expand -Arguments @(
        "-F:$($file.sourceName)", $payloadPath, $staging
    ) -TimeoutSeconds 60 -CaptureOutput | Out-Null
}
Assert-ExportDocVisualCppFiles -Directory $staging -Release $release -ExtractedNames
foreach ($file in $release.files) {
    $targetPath = Assert-WorkspacePath (Join-Path $destination $file.name)
    Copy-Item -LiteralPath (Join-Path $staging $file.sourceName) -Destination $targetPath -Force
}
Copy-Item -LiteralPath $manifestPath -Destination (Assert-WorkspacePath (Join-Path $destination "msvc-runtime.json")) -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "VisualCppRuntime/README.md") -Destination (Assert-WorkspacePath (Join-Path $destination "MSVC_RUNTIME_NOTICES.md")) -Force
Assert-ExportDocVisualCppFiles -Directory $destination -Release $release
Write-Host "Minimal Visual C++ runtime: 4 DLLs, $(($release.files | Measure-Object -Property bytes -Sum).Sum) bytes; no installer shipped."
