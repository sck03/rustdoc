# Inspect Cargo's test fingerprints, never guess from a crate or executable name.
# The caller applies the shared workspace, data, link and Git protections.
function Get-ExportDocRustTestArtifactPaths {
    param([Parameter(Mandatory = $true)][string]$TargetRoot)
    if (-not (Test-Path -LiteralPath $TargetRoot -PathType Container)) { return }
    if ((Get-Item -LiteralPath $TargetRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return }
    if (-not (Test-ExportDocCargoOutputDirectory -Path $TargetRoot)) { return }
    $roots = @((Get-Item -LiteralPath $TargetRoot)) + @(Get-ChildItem -LiteralPath $TargetRoot -Directory -Force |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $_.Name -notin @('debug', 'release', '.git') })
    foreach ($root in $roots) {
        foreach ($profileName in @('debug', 'release')) {
            $profile = Join-Path $root.FullName $profileName
            $fingerprints = Join-Path $profile '.fingerprint'
            if (-not (Test-Path -LiteralPath $fingerprints -PathType Container)) { continue }
            if (((Get-Item -LiteralPath $profile -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                ((Get-Item -LiteralPath $fingerprints -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
            foreach ($fingerprint in Get-ChildItem -LiteralPath $fingerprints -Directory -Force) {
                if ($fingerprint.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                if ($fingerprint.Name -cnotmatch '-([a-f0-9]{16})$') { continue }
                $digest = $Matches[1]
                foreach ($marker in Get-ChildItem -LiteralPath $fingerprint.FullName -File -Filter 'test-*.json' -Force) {
                    if ($marker.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                    if ($marker.Name -cnotmatch '^test-(lib|bin|integration-test|example)-(.+)\.json$') { continue }
                    $folder = if ($Matches[1] -eq 'example') { 'examples' } else { 'deps' }
                    $stem = $Matches[2].Replace('-', '_') + '-' + $digest
                    $base = Join-Path $profile "$folder/$stem"
                    # Native Windows EXEs and Unix executables are both recognized.
                    foreach ($extension in @('.exe', '')) {
                        $executable = "$base$extension"
                        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { continue }
                        $executable
                        foreach ($sidecar in @('.d', '.pdb')) {
                            if (Test-Path -LiteralPath "$base$sidecar" -PathType Leaf) { "$base$sidecar" }
                        }
                    }
                }
            }
        }
    }
}

function Assert-ExportDocRustTestsIdle {
    param([Parameter(Mandatory = $true)][string]$TargetRoot)
    foreach ($process in Get-Process) {
        if ($process.ProcessName -in @('cargo', 'rustc', 'rustdoc')) {
            throw "Finish Cargo builds, tests and doc-tests before cleanup: $($process.ProcessName) ($($process.Id))"
        }
        if ($process.Path -and (Test-ExportDocPathUnderRoot -Path $process.Path -Root $TargetRoot)) {
            throw "A Cargo output is still running: $($process.Path) ($($process.Id))"
        }
    }
}
