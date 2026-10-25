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
    $server = Join-Path $release "export-doc-server$suffix"
    $serverReceipt = Join-Path $release 'server-build.json'
    $productVersion = (Get-Content -LiteralPath (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json).version
    foreach ($state in @('missing', 'stale', 'changed')) {
        if ($state -ne 'missing') { Write-ExportDocRustBuildReceipt -Binary $server -Receipt $serverReceipt -Version $(if ($state -eq 'stale') { '99.0.0' } else { $productVersion }) }
        if ($state -eq 'changed') { [IO.File]::AppendAllText($server, 'modified') }
        $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'package-native-web-server.ps1') -SkipBuild -WithoutOcr -NoPause -OutputRoot $output 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $message -notmatch 'receipt') { throw "Web package accepted $state binary identity: $message" }
        if (Test-Path -LiteralPath $output) { throw 'Invalid web build must not create a package' }
    }
    $good = Join-Path $fixture 'input.txt'
    [IO.File]::WriteAllText($good, 'new')
    New-Item -ItemType Directory -Path $output | Out-Null
    $existing = Join-Path $output 'ExportDocManager.exe'
    [IO.File]::WriteAllText($existing, 'old')
    $map = [ordered]@{ $good = 'ExportDocManager.exe'; (Join-Path $fixture 'missing.txt') = 'Web/missing.js' }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies $map -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected) { throw 'Incomplete package inputs were accepted' }
    Assert-Equal ([IO.File]::ReadAllText($existing)) 'old'
    $map = [ordered]@{ $good = '../escape.txt' }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies $map -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $fixture 'escape.txt'))) { throw 'Package output escaped its root' }
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'ExportDocManager.exe' }) -OutputRoot $output
    Assert-Equal ([IO.File]::ReadAllText($existing)) 'new'
    # Rebuilding a package retires only files owned by the package manifest.
    $data = Join-Path $output 'App_Data/Database/business.db'
    $customTemplate = Join-Path $output 'Templates/custom.dtpl'
    foreach ($file in @($data, $customTemplate)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
        [IO.File]::WriteAllText($file, 'keep')
    }
    $secondInput = Join-Path $fixture 'second.txt'
    [IO.File]::WriteAllText($secondInput, 'runtime')
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/old.js'; $secondInput = 'sidecar/ocr/exportdoc-ocr.exe' }) -OutputRoot $output
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/new.js' }) -OutputRoot $output
    foreach ($relative in @('ExportDocManager.exe', 'Web/assets/old.js', 'sidecar/ocr/exportdoc-ocr.exe')) {
        if (Test-Path -LiteralPath (Join-Path $output $relative)) { throw "Retired package file survived: $relative" }
    }
    foreach ($file in @($data, $customTemplate)) { Assert-Equal ([IO.File]::ReadAllText($file)) 'keep' }
    foreach ($relative in @('App_Data/Database/business.db', 'App_Data./Database/business.db', 'App_Data /Database/business.db', 'Web/assets/CON.js', '../escape.txt', '.git/config', 'private-patch.txt')) {
        $rejected = $false
        try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = $relative }) -OutputRoot $output } catch { $rejected = $true }
        if (-not $rejected) { throw "Protected package path accepted: $relative" }
    }
    $fileManifest = Join-Path $output 'exportdoc-package-files.json'
    $previousManifest = [IO.File]::ReadAllText($fileManifest)
    $privatePatch = Join-Path $output 'private-patch.txt'
    [IO.File]::WriteAllText($privatePatch, 'private patch')
    foreach ($broken in @('{', '{"schemaVersion":1,"files":["Web/assets/new.js"]}', (@{schemaVersion=1;files=@(@{path='private-patch.txt';sha256=(Get-FileHash -LiteralPath $privatePatch -Algorithm SHA256).Hash})} | ConvertTo-Json -Depth 4))) {
        [IO.File]::WriteAllText($fileManifest, $broken)
        $rejected = $false
        try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/replacement.js' }) -OutputRoot $output } catch { $rejected = $true }
        if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $output 'Web/assets/replacement.js'))) { throw 'Unsafe old manifest was accepted or partially copied' }
        Assert-Equal ([IO.File]::ReadAllText($data)) 'keep'
        Assert-Equal ([IO.File]::ReadAllText($privatePatch)) 'private patch'
    }
    [IO.File]::WriteAllText($fileManifest, $previousManifest)
    $savedManifest = "$fileManifest.saved"
    Move-Item -LiteralPath $fileManifest -Destination $savedManifest
    New-Item -ItemType Directory -Path $fileManifest | Out-Null
    try {
        $rejected = $false
        try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/replacement.js' }) -OutputRoot $output } catch { $rejected = $true }
        if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $output 'Web/assets/replacement.js'))) { throw 'Directory manifest was accepted or partially copied' }
    } finally {
        Remove-Item -LiteralPath $fileManifest
        Move-Item -LiteralPath $savedManifest -Destination $fileManifest
    }
    # A user edit to a resource owned by an earlier package is a conflict, not deletion.
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Templates/builtin.dtpl' }) -OutputRoot $output
    $builtin = Join-Path $output 'Templates/builtin.dtpl'
    [IO.File]::AppendAllText($builtin, ' user edit')
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/replacement.js' }) -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $output 'Web/assets/replacement.js'))) { throw 'Modified owned template was silently retired' }
    Assert-Equal ([IO.File]::ReadAllText($builtin)) 'new user edit'
    [IO.File]::WriteAllText($builtin, 'new')
    # An existing marked package gets an ownership list without sweeping custom files.
    Remove-Item -LiteralPath $fileManifest
    [IO.File]::WriteAllText((Join-Path $output 'exportdoc-native-package.json'), '{"schemaVersion":1,"purpose":"rust-native-application"}')
    $legacyOcr = Join-Path $output 'sidecar/ocr/onnxruntime.dll'
    [IO.File]::WriteAllText($legacyOcr, 'old OCR')
    $nestedGit = Join-Path $output 'Web/repository/.git'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $nestedGit) | Out-Null
    [IO.File]::WriteAllText($nestedGit, 'gitdir: protected')
    if ($IsWindows) { (Get-Item -LiteralPath $nestedGit -Force).Attributes = [IO.FileAttributes]::Hidden }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/current.js' }) -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $legacyOcr)) { throw 'Nested Git boundary was traversed or package partially changed' }
    Remove-Item -LiteralPath $nestedGit -Force
    $outside = Join-Path $fixture 'preserved-assets'
    New-Item -ItemType Directory -Path $outside | Out-Null
    $outsideFile = Join-Path $outside 'private-patch.txt'
    [IO.File]::WriteAllText($outsideFile, 'keep outside')
    $linkedDirectory = Join-Path $output 'Web/linked'
    New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $linkedDirectory -Target $outside | Out-Null
    try {
        $rejected = $false
        try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/current.js' }) -OutputRoot $output } catch { $rejected = $true }
        if (-not $rejected -or -not (Test-Path -LiteralPath $legacyOcr)) { throw 'Linked runtime subtree was traversed or package partially changed' }
        Assert-Equal ([IO.File]::ReadAllText($outsideFile)) 'keep outside'
    } finally { Remove-Item -LiteralPath $linkedDirectory -Force }
    foreach ($case in @(@('build-native.ps1', 'portable-runtime.json'), @('build-native.ps1', 'runtime-layout.json'), @('build-native.ps1', 'exportdoc-native-package.json'), @('package-native-web-server.ps1', 'exportdoc-native-web-server.json'))) {
        $descriptor = Join-Path $output $case[1]
        $savedDescriptor = "$descriptor.saved"
        $hadDescriptor = Test-Path -LiteralPath $descriptor -PathType Leaf
        if ($hadDescriptor) { Move-Item -LiteralPath $descriptor -Destination $savedDescriptor }
        New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $descriptor -Target $outside | Out-Null
        try {
            $message = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $case[0]) -SkipBuild -WithoutOcr -NoPause -OutputRoot $output 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -or $message -notmatch 'Linked paths are not allowed') { throw "Linked descriptor preflight failed: $($case[1]) $message" }
        } finally {
            Remove-Item -LiteralPath $descriptor -Force
            if ($hadDescriptor) { Move-Item -LiteralPath $savedDescriptor -Destination $descriptor }
        }
        Assert-Equal ([IO.File]::ReadAllText($outsideFile)) 'keep outside'
    }
    $rejected = $false
    try { Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Templates/custom.dtpl' }) -OutputRoot $output } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unowned modified template was overwritten on first package adoption' }
    Copy-ExportDocNativePackageFiles -Copies ([ordered]@{ $good = 'Web/assets/current.js' }) -OutputRoot $output
    if (Test-Path -LiteralPath $legacyOcr) { throw 'Unmanifested marked OCR resource survived lightweight rebuild' }
    foreach ($file in @($data, $customTemplate)) { Assert-Equal ([IO.File]::ReadAllText($file)) 'keep' }
    Write-Host 'Rust build paths and package preflight passed.'
} finally {
    $env:CARGO_TARGET_DIR = $previousTarget
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne (Join-Path $repo '.codex-runtime')) { throw 'Unsafe fixture cleanup path' }
    Remove-ExportDocDirectoryWithRetry -Path $resolved -MaximumAttempts 4
}
