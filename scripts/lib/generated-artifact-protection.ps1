# Never traverse junctions/symlinks or remove business data with generated output.
function Test-ExportDocProtectedArtifact {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$IncludeReleaseOutputs)
    $root = [System.IO.Path]::GetFullPath($Path)
    for ($ancestor = [System.IO.Path]::GetFullPath($Path); $ancestor; $ancestor = [System.IO.Path]::GetDirectoryName($ancestor)) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
        if ($item.Name -in @(".git", "App_Data", "Database", "Backups", "Templates", "OcrModels", "Resources", "Security")) { return $true }
    }
    $cacheTag = Join-Path $root 'CACHEDIR.TAG'
    $cargoOutput = (Test-Path -LiteralPath $cacheTag -PathType Leaf) -and
        -not ((Get-Item -LiteralPath $cacheTag -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -and
        ((Get-Item -LiteralPath $cacheTag).Length -le 4096) -and
        ([System.IO.File]::ReadAllText($cacheTag).StartsWith('Signature: 8a477f597d28d172789f06886806bc55'))
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push([System.IO.Path]::GetFullPath($Path))
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
        if ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
        if ($directory.Name -in @(".git", "App_Data", "Database", "Backups", "Security")) { return $true }
        $relative = [System.IO.Path]::GetRelativePath($root, $directory.FullName).Replace('\', '/')
        if ($cargoOutput -and -not $IncludeReleaseOutputs -and $relative -cmatch '^(?:[^/]+/)?(?:debug|release)/bundle(?:/|$)') { return $true }
        if ($directory.Name -in @('Templates', 'OcrModels', 'Resources')) {
            # Cargo build scripts emit disposable resources under profile/build/*/out.
            # The cache tag and this structural boundary distinguish those files
            # from application resources; data files and links remain protected.
            if (-not $cargoOutput -or $relative -cnotmatch '^(?:[^/]+/)?(?:debug|release)/build/[^/]+/out(?:/|$)') { return $true }
        }
        foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force -ErrorAction Stop) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            elseif (-not $IncludeReleaseOutputs -and $entry.Name -match '^exportdoc-(?:desktop|web|container)-.+\.(?:zip|tar\.gz)$') { return $true }
            elseif ($entry.Name -eq 'PG_VERSION' -or $entry.Name -match '\.((?:db|sqlite3?)(?:-wal|-shm|-journal)?|edmrecovery|edmmigration|dump)$') { return $true }
        }
    }
    return $false
}
