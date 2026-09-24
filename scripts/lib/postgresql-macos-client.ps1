# Copy the native Homebrew PostgreSQL 18 client closure with its license texts.
function Copy-PostgreSqlMacClient {
    param([Parameter(Mandatory)][string]$PayloadRoot)
    $prefixResult = Invoke-ExportDocExternal -FilePath brew -Arguments @('--prefix', 'postgresql@18') -CaptureOutput -TimeoutSeconds 60
    $prefix = $prefixResult.Output.Trim()
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $copies = [ordered]@{}
    foreach ($tool in @('pg_dump', 'pg_restore', 'psql')) { $source = Join-Path $prefix "bin/$tool"; $copies[$source] = Join-Path $PayloadRoot "bin/$tool"; $queue.Enqueue($source) }
    $dependencies = @{}
    while ($queue.Count) {
        $source = $queue.Dequeue()
        $result = Invoke-ExportDocExternal -FilePath otool -Arguments @('-L', $source) -CaptureOutput -TimeoutSeconds 30
        $skip = if ($source.EndsWith('.dylib')) { 2 } else { 1 }
        $libraries = @($result.Output -split "`n" | Select-Object -Skip $skip | ForEach-Object { if ($_ -match '^\s+(\S+)\s+\(') { $Matches[1] } } | Where-Object { $_ -notlike '/usr/lib/*' -and $_ -notlike '/System/*' })
        $dependencies[$source] = $libraries
        foreach ($library in $libraries) {
            if (-not [IO.Path]::IsPathRooted($library) -or -not (Test-Path -LiteralPath $library -PathType Leaf)) { throw "Unresolved PostgreSQL dylib: $library" }
            if (-not $copies.Contains($library)) {
                $target = Join-Path $PayloadRoot "lib/$([IO.Path]::GetFileName($library))"
                if ($copies.Values -contains $target) { throw 'Conflicting bundled dylib names.' }
                $copies[$library] = $target
                $queue.Enqueue($library)
            }
        }
    }
    $licenses = Join-Path $PayloadRoot 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    foreach ($entry in $copies.GetEnumerator()) {
        $real = (Get-Item -LiteralPath $entry.Key).ResolveLinkTarget($true)
        $source = if ($real) { $real.FullName } else { $entry.Key }
        Copy-Item -LiteralPath $source -Destination $entry.Value -Force
        $formula = Split-Path -Parent (Split-Path -Parent $source)
        foreach ($license in Get-ChildItem -LiteralPath $formula -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|COPYRIGHT)' }) {
            Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenses ((Split-Path -Leaf (Split-Path -Parent $formula)) + '-' + $license.Name)) -Force
        }
    }
    foreach ($entry in $copies.GetEnumerator()) {
        foreach ($library in $dependencies[$entry.Key]) {
            $relative = [IO.Path]::GetRelativePath((Split-Path -Parent $entry.Value), $copies[$library])
            Invoke-ExportDocExternal -FilePath install_name_tool -Arguments @('-change', $library, "@loader_path/$relative", $entry.Value) -TimeoutSeconds 30
        }
        # Mach-O relocation on ARM64 needs an ad-hoc seal, never Developer ID or notarization.
        Invoke-ExportDocExternal -FilePath codesign -Arguments @('--force', '--sign', '-', $entry.Value) -TimeoutSeconds 60
    }
    $license = Join-Path $prefix 'COPYRIGHT'
    if (-not (Test-Path -LiteralPath $license)) { throw 'PostgreSQL COPYRIGHT is missing.' }
    Copy-Item -LiteralPath $license -Destination (Join-Path $PayloadRoot 'POSTGRESQL_LICENSE.txt')
}
