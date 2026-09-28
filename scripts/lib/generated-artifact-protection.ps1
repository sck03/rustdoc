# Never traverse junctions/symlinks or remove business data with generated output.
function Test-ExportDocProtectedArtifact {
    param([Parameter(Mandatory = $true)][string]$Path)
    for ($ancestor = [System.IO.Path]::GetFullPath($Path); $ancestor; $ancestor = [System.IO.Path]::GetDirectoryName($ancestor)) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
        if ($item.Name -in @(".git", "App_Data", "Database", "Backups", "Templates", "OcrModels", "Resources", "Security")) { return $true }
    }
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push([System.IO.Path]::GetFullPath($Path))
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
        if ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
        if ($directory.Name -in @(".git", "App_Data", "Database", "Backups", "Templates", "OcrModels", "Resources", "Security")) { return $true }
        foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force -ErrorAction Stop) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $true }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            elseif ($entry.Name -match '\.(db(?:-wal|-shm)?|sqlite3?|edmrecovery|edmmigration|dump)$') { return $true }
        }
    }
    return $false
}
