$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib/generated-artifact-protection.ps1")
$fixtureRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ".codex-runtime"
$fixture = Join-Path $fixtureRoot ("cleanup-policy-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    if (Test-ExportDocProtectedArtifact $fixture) { throw "Empty generated directory should be cleanable" }
    foreach ($name in @("App_Data", "Backups", "Templates", "Resources", "Database")) {
        $child = Join-Path $fixture $name
        New-Item -ItemType Directory -Path $child | Out-Null
        if (-not (Test-ExportDocProtectedArtifact $fixture)) { throw "Failed to protect $name" }
        Remove-Item -LiteralPath $child
    }
    foreach ($name in @("business.db", "backup.edmrecovery", "server.dump")) {
        $child = Join-Path $fixture $name
        [System.IO.File]::WriteAllText($child, "test fixture")
        if (-not (Test-ExportDocProtectedArtifact $fixture)) { throw "Failed to protect $name" }
        Remove-Item -LiteralPath $child
    }
    Write-Host "Generated artifact cleanup protection passed."
} finally {
    $resolved = [System.IO.Path]::GetFullPath($fixture)
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne [System.IO.Path]::GetFullPath($fixtureRoot)) { throw "Unexpected fixture path" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
