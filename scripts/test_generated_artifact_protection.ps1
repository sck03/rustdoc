$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib/build-script-support.ps1")
. (Join-Path $PSScriptRoot "lib/generated-artifact-protection.ps1")
$fixtureRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ".codex-runtime"
$fixture = Join-Path $fixtureRoot ("cleanup-policy-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    if (Test-ExportDocProtectedArtifact $fixture) { throw "Empty generated directory should be cleanable" }
    foreach ($name in @("App_Data", "Backups", "Templates", "Resources", "Database", "KEY")) {
        $child = Join-Path $fixture $name
        New-Item -ItemType Directory -Path $child | Out-Null
        if (-not (Test-ExportDocProtectedArtifact $fixture)) { throw "Failed to protect $name" }
        if (-not (Test-ExportDocProtectedArtifact $child -IncludeReleaseOutputs)) { throw "Explicit release cleanup must still protect $name" }
        Remove-Item -LiteralPath $child
    }
    foreach ($name in @(".git", "business.db", "business.db-journal", "business.sqlite-wal", "backup.edmrecovery", "server.dump", "PG_VERSION")) {
        $child = Join-Path $fixture $name
        [System.IO.File]::WriteAllText($child, "test fixture")
        if (-not (Test-ExportDocProtectedArtifact $fixture)) { throw "Failed to protect $name" }
        if (-not (Test-ExportDocProtectedArtifact $fixture -IncludeReleaseOutputs)) { throw "Release cleanup must still protect $name" }
        Remove-Item -LiteralPath $child
    }
    $target = Join-Path $fixture 'target'
    $generated = Join-Path $target 'debug/build/example/out/permissions/resources'
    New-Item -ItemType Directory -Path $generated -Force | Out-Null
    $tag = Join-Path $target 'CACHEDIR.TAG'
    [IO.File]::WriteAllText($tag, "Signature: 8a477f597d28d172789f06886806bc55`n# This file is a cache directory tag created by cargo.`n")
    if (Test-ExportDocProtectedArtifact $target) { throw 'Cargo-generated resource metadata must be cleanable' }
    $database = Join-Path $generated 'business.db'
    [IO.File]::WriteAllText($database, 'protected')
    if (-not (Test-ExportDocProtectedArtifact $target)) { throw 'Cargo cache tags must not bypass database protection' }
    Remove-Item -LiteralPath $database
    foreach ($profile in @('release', 'x86_64-pc-windows-msvc/release')) {
        $bundle = Join-Path $target "$profile/bundle"
        New-Item -ItemType Directory -Path $bundle -Force | Out-Null
        if (-not (Test-ExportDocProtectedArtifact $target)) { throw 'Cargo installers must be preserved' }
        if (Test-ExportDocProtectedArtifact $target -IncludeReleaseOutputs) { throw 'Explicit release cleanup should allow a data-free bundle' }
        Remove-Item -LiteralPath $bundle
    }
    [IO.File]::WriteAllText($tag, 'not a cache directory tag')
    if (-not (Test-ExportDocProtectedArtifact $target)) { throw 'Unmarked resource directories must stay protected' }
    [IO.File]::WriteAllText($tag, "Signature: 8a477f597d28d172789f06886806bc55`n")
    New-Item -ItemType Directory -Path (Join-Path $target 'Resources') | Out-Null
    if (-not (Test-ExportDocProtectedArtifact $target)) { throw 'Resources outside Cargo build-script output must stay protected' }
    $archiveOnly = Join-Path $fixture 'review-package'
    New-Item -ItemType Directory -Path $archiveOnly | Out-Null
    [IO.File]::WriteAllText((Join-Path $archiveOnly 'exportdoc-web-test.zip'), 'archive fixture')
    if (-not (Test-ExportDocProtectedArtifact $archiveOnly)) { throw 'Release archives outside the default folder must be preserved' }
    if (Test-ExportDocProtectedArtifact $archiveOnly -IncludeReleaseOutputs) { throw 'Explicit release cleanup should allow a data-free archive' }
    $planRoot = Join-Path $fixture 'workspace'
    $planScripts = Join-Path $planRoot 'scripts'
    foreach ($directory in @('.git', 'scripts/lib', 'target/debug/build/example/out/permissions/resources', '.codex-runtime/native-runtime-packages', '.codex-runtime/pip-cache', 'artifacts/releases')) {
        New-Item -ItemType Directory -Path (Join-Path $planRoot $directory) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clean-generated-artifacts.ps1') -Destination $planScripts
    foreach ($name in @('platform-path-safety.ps1', 'generated-artifact-protection.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "lib/$name") -Destination (Join-Path $planScripts 'lib')
    }
    [IO.File]::WriteAllText((Join-Path $planRoot 'target/CACHEDIR.TAG'), "Signature: 8a477f597d28d172789f06886806bc55`n")
    $cache = Join-Path $planRoot '.codex-runtime/native-runtime-packages/archive.zip'
    $release = Join-Path $planRoot 'artifacts/releases/exportdoc.zip'
    $pipCache = Join-Path $planRoot '.codex-runtime/pip-cache/package.whl'
    [IO.File]::WriteAllText($pipCache, 'cached Python package')
    [IO.File]::WriteAllText($cache, 'cached archive fixture')
    [IO.File]::WriteAllText($release, 'release fixture')
    $protectedFiles = @($cache, $pipCache, $release)
    foreach ($path in @(
        'apps/web/node_modules/package/dist/index.js',
        'apps/web/App_Data/dist/user.txt',
        'tools/private/KEY/target/signing-material',
        'artifacts/review/KEY/private-material',
        'artifacts/worktree/.git',
        '.codex-runtime/worktree/.git',
        'apps/nested/.git',
        'apps/nested/dist/source.js',
        'crates/nested/.git/HEAD',
        'crates/nested/target/source.rs',
        '.codex-runtime/notes.txt',
        '.codex-runtime/pip-cache/install.log'
    )) {
        $file = Join-Path $planRoot $path
        New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($file, 'preserved fixture')
        $protectedFiles += $file
    }
    $generatedPaths = @('target', 'apps/web/dist', 'crates/example/target', 'artifacts/cargo-target-native', '.codex-runtime/cargo-target-native')
    foreach ($path in $generatedPaths | Select-Object -Skip 1) {
        $directory = Join-Path $planRoot $path
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $directory 'generated-output'), 'rebuildable')
    }
    $generatedPaths += '.codex-runtime/completed-test.log'
    [IO.File]::WriteAllText((Join-Path $planRoot '.codex-runtime/completed-test.log'), 'completed test log')
    & pwsh -NoProfile -File (Join-Path $planScripts 'clean-generated-artifacts.ps1') -IncludeCodexRuntimeWorkspaces -WhatIf | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Isolated cleanup preview failed' }
    foreach ($path in $generatedPaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $planRoot $path))) { throw "Preview removed $path" }
    }
    & pwsh -NoProfile -File (Join-Path $planScripts 'clean-generated-artifacts.ps1') -IncludeCodexRuntimeWorkspaces | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Isolated cleanup failed' }
    foreach ($path in $generatedPaths) {
        if (Test-Path -LiteralPath (Join-Path $planRoot $path)) { throw "Verified compiler output was not removed: $path" }
    }
    foreach ($preserved in $protectedFiles) {
        if (-not (Test-Path -LiteralPath $preserved)) { throw "Default cleanup removed a reusable cache or release: $preserved" }
    }
    Write-Host "Generated artifact cleanup protection and isolated deletion passed."
} finally {
    $resolved = [System.IO.Path]::GetFullPath($fixture)
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne [System.IO.Path]::GetFullPath($fixtureRoot)) { throw "Unexpected fixture path" }
    Remove-ExportDocDirectoryWithRetry -Path $resolved -MaximumAttempts 5
}
