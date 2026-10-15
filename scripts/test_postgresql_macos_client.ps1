$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/postgresql-macos-client.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repository ('.codex-runtime/postgresql-macos-client-tests/' + [Guid]::NewGuid().ToString('N'))
if (-not $fixture.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped workspace.' }
$script:original = Join-Path $fixture 'Cellar/client/18.6'
$script:alternate = Join-Path $fixture 'Cellar/other/1.0'
$script:alias = Join-Path $fixture 'opt/client'
$output = Join-Path $fixture 'payload'
$script:changes = [Collections.Generic.List[object]]::new()
$script:conflict = $false
function Invoke-ExportDocExternal {
    param($FilePath, $Arguments, [switch]$CaptureOutput, $TimeoutSeconds)
    if ($FilePath -eq 'brew') { return [pscustomobject]@{ Output = $script:alias } }
    if ($FilePath -eq 'node') {
        $value = & node @Arguments
        if ($LASTEXITCODE -ne 0) { throw 'Canonical path resolution failed.' }
        return [pscustomobject]@{ Output = "$value" }
    }
    if ($FilePath -eq 'otool') {
        $source = $Arguments[1]
        $name = [IO.Path]::GetFileName($source)
        $parent = if ($name -eq 'pg_dump') { $script:alias } elseif ($name -eq 'psql' -and $script:conflict) { $script:alternate } else { $script:original }
        $library = Join-Path $parent 'lib/libshared.1.dylib'
        $lines = if ($name.EndsWith('.dylib')) { "$source`:`n`t$source (compatibility version 1.0.0)`n`t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)" } else { "$source`:`n`t$library (compatibility version 1.0.0)" }
        return [pscustomobject]@{ Output = $lines }
    }
    if ($FilePath -eq 'install_name_tool') { $script:changes.Add($Arguments); return }
    if ($FilePath -ne 'codesign') { throw "Unexpected fixture tool: $FilePath" }
}
try {
    @("$script:original/bin", "$script:original/lib", "$script:alternate/lib", (Split-Path -Parent $script:alias), "$output/bin", "$output/lib") | ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
    foreach ($name in @('pg_dump', 'pg_restore', 'psql')) { [IO.File]::WriteAllText((Join-Path $script:original "bin/$name"), 'fixture') }
    [IO.File]::WriteAllText((Join-Path $script:original 'lib/libshared.1.dylib'), 'first library')
    [IO.File]::WriteAllText((Join-Path $script:alternate 'lib/libshared.1.dylib'), 'other library')
    [IO.File]::WriteAllText((Join-Path $script:original 'COPYRIGHT'), 'fixture license')
    New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $script:alias -Target $script:original | Out-Null
    Copy-PostgreSqlMacClient -PayloadRoot $output
    if (@(Get-ChildItem -LiteralPath "$output/lib" -File).Count -ne 1 -or $script:changes.Count -ne 3) { throw 'Aliases must copy one library and relocate all three original references.' }
    if (-not (Test-Path -LiteralPath "$output/licenses/client-COPYRIGHT")) { throw 'License must be read from the canonical formula directory.' }
    $script:conflict = $true
    $rejected = $false
    try { Copy-PostgreSqlMacClient -PayloadRoot $output } catch { $rejected = $_.Exception.Message -like 'Conflicting bundled dylib name:*' }
    if (-not $rejected) { throw 'Distinct same-named libraries must still be rejected.' }
    Write-Output 'macOS PostgreSQL source aliases, relocation and name conflicts passed.'
} finally {
    if (Test-Path -LiteralPath $script:alias) { Remove-Item -LiteralPath $script:alias -Force }
    if ($fixture.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        -not @(Get-ChildItem -LiteralPath $fixture -Force -Recurse | Where-Object { $_.Name -eq '.git' -or $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
