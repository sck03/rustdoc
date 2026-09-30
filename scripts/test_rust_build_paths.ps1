$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
. (Join-Path $PSScriptRoot 'lib/native-package-resources.ps1')
$repo = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repo ('.codex-runtime/build-path-tests-' + [Guid]::NewGuid().ToString('N'))
$previousTarget = $env:CARGO_TARGET_DIR
function Assert-Equal($actual, $expected) { if ($actual -ne $expected) { throw "Expected '$expected'; got '$actual'" } }
try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Assert-Equal ((Get-ExportDocProductEditionNames) -join ',') 'Document,Sales'
    Assert-Equal ((Get-ExportDocProductEditionNames -IncludeLocalTests) -join ',') 'Document,Sales,Full'
    Assert-Equal (Resolve-ExportDocProductEdition -Edition 'full' -IncludeLocalTests) 'Full'
    if ($IsWindows) {
        $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-windows-desktop-run.ps1') -Edition Full -PreflightOnly -NoPause 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $message -notmatch 'Desktop/backend build uses Rust') { throw "Single-edition Full dispatch failed: $message" }
    }
    foreach ($edition in @('Administration')) {
        foreach ($script in @('build-native.ps1', 'build-windows-desktop-run.ps1', 'build-windows-installers.ps1', 'run-native.ps1')) {
            $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script) -Edition $edition -NoPause 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -or $message -notmatch 'Edition') { throw "Retired desktop edition accepted: $script $edition" }
        }
    }
    $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-windows-installers.ps1') -Edition Full -NoPause 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or $message -notmatch 'Edition') { throw 'Local Full testing must not enter installer releases.' }
    $env:CARGO_TARGET_DIR = ''
    Assert-Equal (Get-ExportDocCargoTargetDirectory $repo) (Join-Path $repo 'target')
    $env:CARGO_TARGET_DIR = '.codex-runtime/custom build'
    Assert-Equal (Get-ExportDocCargoArtifactDirectory -RepositoryRoot $repo -Configuration Release -RustTarget 'test-target') (Join-Path $repo '.codex-runtime/custom build/test-target/release')
    $env:CARGO_TARGET_DIR = Join-Path $fixture 'outputs'
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    $output = Join-Path $fixture 'package'
    foreach ($script in @('build-native.ps1', 'package-native-web-server.ps1')) {
        $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script) -SkipBuild -NoPause -OutputRoot $output 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $message -notmatch 'SkipBuild requires existing binaries') { throw "Missing-binary preflight failed: $script`n$message" }
        if (Test-Path -LiteralPath $output) { throw 'Preflight must not create a partial package' }
    }
    $release = Join-Path $env:CARGO_TARGET_DIR 'release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    foreach ($name in @('export-doc-tauri', 'export-doc-server')) { [IO.File]::WriteAllText((Join-Path $release "$name$suffix"), 'fixture') }
    $editionRoot = Join-Path $release 'editions/Document'
    New-Item -ItemType Directory -Force -Path $editionRoot | Out-Null
    $binary = Join-Path $editionRoot "export-doc-tauri$suffix"
    Copy-Item -LiteralPath (Join-Path $release "export-doc-tauri$suffix") -Destination $binary
    @{edition='Document';version=(Get-Content (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json).version;sha256=(Get-FileHash $binary -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content (Join-Path $editionRoot 'build.json')
    foreach ($script in @('build-native.ps1', 'package-native-web-server.ps1')) {
        $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script) -SkipBuild -NoPause -OutputRoot $output 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $message -notmatch 'exportdoc-ocr') { throw "Missing OCR preflight failed: $script" }
        if (Test-Path -LiteralPath $output) { throw 'Missing OCR must not create a partial package' }
    }
    [IO.File]::AppendAllText($binary, 'changed')
    $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-native.ps1') -SkipBuild -WithoutOcr -NoPause -OutputRoot $output 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or $message -notmatch 'does not match its build receipt') { throw 'Modified edition binary was accepted' }
    foreach ($edition in @('Sales', 'Full')) {
        $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-native.ps1') -Edition $edition -SkipBuild -NoPause -OutputRoot $output 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $message -notmatch 'SkipBuild requires existing binaries') { throw "Document build must not be reused as $edition" }
    }
    if (Test-Path -LiteralPath $output) { throw 'Invalid edition must not create a package' }
    $good = Join-Path $fixture 'input.txt'
    [IO.File]::WriteAllText($good, 'new')
    New-Item -ItemType Directory -Path $output | Out-Null
    $existing = Join-Path $output 'existing.txt'
    [IO.File]::WriteAllText($existing, 'old')
    $map = [ordered]@{ $good = 'existing.txt'; (Join-Path $fixture 'missing.txt') = 'missing.txt' }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies $map -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected) { throw 'Incomplete package inputs were accepted' }
    Assert-Equal ([IO.File]::ReadAllText($existing)) 'old'
    $map = [ordered]@{ $good = '../escape.txt' }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies $map -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $fixture 'escape.txt'))) { throw 'Package output escaped its root' }
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'existing.txt' }) -OutputRoot $output
    Assert-Equal ([IO.File]::ReadAllText($existing)) 'new'
    Write-Host 'Rust build paths and package preflight passed.'
} finally {
    $env:CARGO_TARGET_DIR = $previousTarget
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne (Join-Path $repo '.codex-runtime')) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
