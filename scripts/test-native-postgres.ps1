[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PostgresBin,
    [ValidateRange(60, 7200)][int]$TimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repositoryRoot '.codex-runtime'
$testRoot = Join-Path $runtimeRoot ('native-postgres-tests/' + [Guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $testRoot 'data'
$PostgresBin = (Resolve-Path -LiteralPath $PostgresBin).Path
$extension = if ($IsWindows) { '.exe' } else { '' }
$pgCtl = Join-Path $PostgresBin "pg_ctl$extension"
$psql = Join-Path $PostgresBin "psql$extension"
$initdb = Join-Path $PostgresBin "initdb$extension"
$version = Invoke-ExportDocExternal -FilePath $pgCtl -Arguments @('--version') -CaptureOutput -TimeoutSeconds 30
if ($version.Output -notmatch 'PostgreSQL\) 18\.') { throw 'Native integration tests require PostgreSQL 18.' }
New-Item -ItemType Directory -Force -Path $testRoot, (Join-Path $runtimeRoot 'temp') | Out-Null
$password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$passwordFile = Join-Path $testRoot 'init-password.txt'
$sqlFile = Join-Path $testRoot 'initialize.sql'
[System.IO.File]::WriteAllText($passwordFile, $password, [System.Text.UTF8Encoding]::new($false))
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
try {
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
} finally { $listener.Stop() }
$environment = @{
    TEMP = Join-Path $runtimeRoot 'temp'
    TMP = Join-Path $runtimeRoot 'temp'
    PGPASSWORD = $password
    CARGO_HOME = $(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $runtimeRoot 'cargo-home' })
    CARGO_TARGET_DIR = $(if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $runtimeRoot 'cargo-target-native' })
}
$clusterStarted = $false
try {
    Invoke-ExportDocExternal -FilePath $initdb -Arguments @('-D', $dataRoot, '-U', 'native_bootstrap', '--auth=scram-sha-256', "--pwfile=$passwordFile", '--encoding=UTF8', '--locale=C') -Environment $environment -TimeoutSeconds 120 -DisplayName 'Initialize isolated native test cluster'
    Invoke-ExportDocExternal -FilePath $pgCtl -Arguments @('-D', $dataRoot, '-l', (Join-Path $testRoot 'postgres.log'), '-o', "-h 127.0.0.1 -p $port", '-w', '-t', '30', 'start') -Environment $environment -TimeoutSeconds 40 -DisplayName 'Start isolated native test cluster'
    $clusterStarted = $true
    $sql = @"
CREATE ROLE native_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE native_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '$password';
CREATE ROLE native_maintenance LOGIN NOSUPERUSER CREATEDB NOCREATEROLE PASSWORD '$password';
GRANT native_owner TO native_maintenance;
CREATE DATABASE native_storage OWNER native_owner;
CREATE DATABASE native_engine OWNER native_owner;
CREATE DATABASE native_api OWNER native_owner;
"@
    [System.IO.File]::WriteAllText($sqlFile, $sql, [System.Text.UTF8Encoding]::new($false))
    $common = @('-X', '-h', '127.0.0.1', '-p', "$port", '-U', 'native_bootstrap', '-v', 'ON_ERROR_STOP=1')
    Invoke-ExportDocExternal -FilePath $psql -Arguments ($common + @('-d', 'postgres', '-f', $sqlFile)) -Environment $environment -TimeoutSeconds 60 -DisplayName 'Create independent native database roles'
    $sql = @'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO native_app;
GRANT USAGE, CREATE ON SCHEMA public TO native_owner;
ALTER DEFAULT PRIVILEGES FOR ROLE native_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO native_app;
ALTER DEFAULT PRIVILEGES FOR ROLE native_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO native_app;
'@
    [System.IO.File]::WriteAllText($sqlFile, $sql, [System.Text.UTF8Encoding]::new($false))
    foreach ($database in @('native_storage', 'native_engine', 'native_api')) {
        Invoke-ExportDocExternal -FilePath $psql -Arguments ($common + @('-d', $database, '-f', $sqlFile)) -Environment $environment -TimeoutSeconds 30 -DisplayName "Set minimum privileges for $database"
    }
    foreach ($target in @(@('POSTGRES', 'native_storage'), @('ENGINE', 'native_engine'))) {
        foreach ($role in @(@('APP', 'native_app'), @('MAINTENANCE', 'native_maintenance'))) {
            $environment["EXPORTDOC_TEST_$($target[0])_$($role[0])"] = "host=127.0.0.1 port=$port user=$($role[1]) password=$password dbname=$($target[1]) sslmode=disable"
        }
    }
    Invoke-ExportDocExternal -FilePath 'cargo' -Arguments @('test', '--locked', '-p', 'export-doc-storage', '-p', 'export-doc-engine', '--features', 'postgres,excel', '--', '--ignored', '--test-threads=1') -WorkingDirectory $repositoryRoot -Environment $environment -TimeoutSeconds $TimeoutSeconds -DisplayName 'Native PostgreSQL storage, durable files and team workflows'
    [ordered]@{ schemaVersion = 1; database = 'PostgreSQL 18'; succeeded = $true; completedAt = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding utf8
    Write-Host "Native PostgreSQL evidence: $testRoot"
} finally {
    if ($clusterStarted -or (Test-Path -LiteralPath (Join-Path $dataRoot 'postmaster.pid'))) {
        Invoke-ExportDocExternal -FilePath $pgCtl -Arguments @('-D', $dataRoot, '-m', 'fast', '-w', '-t', '30', 'stop') -Environment $environment -TimeoutSeconds 40 -DisplayName 'Stop isolated native test cluster'
    }
    foreach ($file in @($passwordFile, $sqlFile)) {
        if (-not (Test-ExportDocPathUnderRoot -Path $file -Root $testRoot)) { throw 'Test secret cleanup escaped its workspace.' }
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
}
