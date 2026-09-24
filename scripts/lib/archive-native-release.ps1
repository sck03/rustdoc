[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-script-support.ps1')
. (Join-Path $PSScriptRoot 'native-package-resources.ps1')
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
Assert-NativePackagePath -Path $sourcePath
Assert-NativePackagePath -Path $destinationPath
if (Test-ExportDocPathUnderRoot -Path $destinationPath -Root $sourcePath) { throw 'Release archive must be outside its source directory.' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
if ($destinationPath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    Compress-Archive -LiteralPath $sourcePath -DestinationPath $destinationPath -Force
} elseif ($destinationPath.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) {
    Invoke-ExportDocExternal -FilePath tar -Arguments @('-czf', $destinationPath, '-C', (Split-Path -Parent $sourcePath), (Split-Path -Leaf $sourcePath)) -TimeoutSeconds 600 -DisplayName 'Archive native release with executable modes and symlinks'
} else { throw 'Release archive must be zip or tar.gz.' }
$digest = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $([IO.Path]::GetFileName($destinationPath))" | Set-Content -LiteralPath "$destinationPath.sha256" -Encoding utf8
