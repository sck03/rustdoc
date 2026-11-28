[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The pinned Homebrew client installer requires macOS.' }
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$client = (Get-Content -LiteralPath (Join-Path $repository 'eng/native-runtime-packages.json') -Raw | ConvertFrom-Json).postgresqlClient
$parts = $client.macosFormula.Split('/')
if ($parts.Count -ne 3 -or $client.macosFormulaSource -notmatch '^https://raw\.githubusercontent\.com/Homebrew/homebrew-core/[a-f0-9]{40}/Formula/p/postgresql@18\.rb$') { throw 'Invalid pinned PostgreSQL Homebrew source.' }
$tap = $parts[0] + '/' + $parts[1]
$cache = Join-Path $repository '.codex-runtime/homebrew-cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$env:HOMEBREW_CACHE = $cache
$env:HOMEBREW_NO_AUTO_UPDATE = '1'
$available = Invoke-ExportDocExternal -FilePath brew -Arguments @('tap') -CaptureOutput -TimeoutSeconds 60
if ($tap -notin ($available.Output -split '\r?\n')) {
    Invoke-ExportDocExternal -FilePath brew -Arguments @('tap-new', '--no-git', $tap) -TimeoutSeconds 60
}
$tapRoot = (Invoke-ExportDocExternal -FilePath brew -Arguments @('--repository', $tap) -CaptureOutput -TimeoutSeconds 60).Output.Trim()
$download = Join-Path $cache ('postgresql-' + $client.version + '.rb')
Invoke-WebRequest -Uri $client.macosFormulaSource -OutFile $download -TimeoutSec 120
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $client.macosFormulaSha256) { throw 'Pinned Homebrew formula checksum mismatch.' }
$source = [IO.File]::ReadAllText($download)
if (-not $source.Contains('/v' + $client.version + '/postgresql-' + $client.version + '.tar.bz2')) { throw 'Homebrew source does not match the declared client version.' }
# Reuse official checksummed bottles from their original registry namespace.
$source = $source.Replace("  bottle do", "  bottle do" + [Environment]::NewLine + '    root_url "https://ghcr.io/v2/homebrew/core"')
$directory = Join-Path $tapRoot 'Formula'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $directory ($parts[2] + '.rb')), $source, [Text.UTF8Encoding]::new($false))
Invoke-ExportDocExternal -FilePath brew -Arguments @('install', $client.macosFormula) -TimeoutSeconds 1800
