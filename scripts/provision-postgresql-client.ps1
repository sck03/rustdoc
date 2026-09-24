[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet("windows", "linux", "macos")][string]$Platform,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")

$clientManifest = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $scriptRoot) 'eng/native-runtime-packages.json') -Raw | ConvertFrom-Json).postgresqlClient
$postgresVersion = $clientManifest.version
$windowsArchiveUri = $clientManifest.windowsArchive
$windowsArchiveName = [IO.Path]::GetFileName(([Uri]$windowsArchiveUri).AbsolutePath)
$windowsArchiveSha256 = $clientManifest.windowsSha256
$linuxImage = $clientManifest.linuxImage

$destinationRoot = [System.IO.Path]::GetFullPath($Destination)
$repositoryRoot = Split-Path -Parent $scriptRoot
. (Join-Path $scriptRoot 'lib/native-package-resources.ps1')
Assert-NativePackagePath -Path $destinationRoot
if (-not (Test-ExportDocPathUnderRoot -Path $destinationRoot -Root $repositoryRoot) -or (Test-ExportDocPathEqual -Left $destinationRoot -Right $repositoryRoot)) { throw 'Client staging must be a dedicated workspace child.' }
if ((Test-Path -LiteralPath $destinationRoot) -and -not (Test-Path -LiteralPath (Join-Path $destinationRoot 'postgresql-client.json'))) { throw 'Refusing to replace an unmarked client directory.' }
$destinationParent = Split-Path -Parent $destinationRoot
$destinationName = Split-Path -Leaf $destinationRoot
if ([string]::IsNullOrWhiteSpace($destinationParent) -or [string]::IsNullOrWhiteSpace($destinationName)) {
    throw "PostgreSQL client destination is invalid: $Destination"
}

New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
$stagingRoot = Join-Path $destinationParent ".$destinationName.staging-$([Guid]::NewGuid().ToString('N'))"
$downloadRoot = Join-Path $stagingRoot "download"
$payloadRoot = Join-Path $stagingRoot "payload"
$binRoot = Join-Path $payloadRoot "bin"
$libRoot = Join-Path $payloadRoot "lib"
New-Item -ItemType Directory -Path $downloadRoot, $binRoot, $libRoot -Force | Out-Null

function Assert-PostgreSqlClientVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [string]$LibraryRoot = ""
    )

    $environment = @{}
    if (-not [string]::IsNullOrWhiteSpace($LibraryRoot)) {
        $existing = [Environment]::GetEnvironmentVariable("LD_LIBRARY_PATH")
        $environment["LD_LIBRARY_PATH"] = if ([string]::IsNullOrWhiteSpace($existing)) {
            $LibraryRoot
        } else {
            "$LibraryRoot`:$existing"
        }
    }

    $result = Invoke-ExportDocExternal `
        -FilePath $Executable `
        -Arguments @("--version") `
        -Environment $environment `
        -TimeoutSeconds 60 `
        -CaptureOutput
    if ($result.Output.Trim() -notmatch ('^\S+ \(PostgreSQL\) ' + [regex]::Escape($postgresVersion) + '(?:\s|$)')) {
        throw "Unexpected PostgreSQL client version: $($result.Output)"
    }
}

try {
    if ($Platform -eq "windows") {
        $archivePath = Join-Path $downloadRoot $windowsArchiveName
        Invoke-WebRequest -Uri $windowsArchiveUri -OutFile $archivePath -TimeoutSec 900
        $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -ne $windowsArchiveSha256) {
            throw "EnterpriseDB PostgreSQL archive checksum mismatch. Expected $windowsArchiveSha256, received $actualSha256."
        }

        $expandedRoot = Join-Path $downloadRoot "expanded"
        Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedRoot
        $sourceRoot = Join-Path $expandedRoot "pgsql"
        $sourceBin = Join-Path $sourceRoot "bin"
        foreach ($name in @("pg_dump.exe", "pg_restore.exe", "psql.exe")) {
            Copy-Item -LiteralPath (Join-Path $sourceBin $name) -Destination $binRoot
        }
        Get-ChildItem -LiteralPath $sourceBin -Filter "*.dll" -File |
            Copy-Item -Destination $binRoot
        Copy-Item -LiteralPath (Join-Path $sourceRoot "server_license.txt") `
            -Destination (Join-Path $payloadRoot "POSTGRESQL_LICENSE.txt")
        Copy-Item -LiteralPath (Join-Path $sourceRoot "commandlinetools_3rd_party_licenses.txt") `
            -Destination (Join-Path $payloadRoot "POSTGRESQL_THIRD_PARTY_LICENSES.txt")
    } elseif ($Platform -eq 'macos') {
        . (Join-Path $scriptRoot 'lib/postgresql-macos-client.ps1')
        Copy-PostgreSqlMacClient -PayloadRoot $payloadRoot
    } else {
        $copyScript = Join-Path $scriptRoot 'lib/copy-postgresql-client.sh'
        Invoke-ExportDocExternal -FilePath "docker" -TimeoutSeconds 1200 -Arguments @(
            'run', '--rm', '--entrypoint', 'sh',
            '--volume', "$($payloadRoot):/out",
            '--volume', "$($copyScript):/copy-client.sh:ro",
            $linuxImage, '/copy-client.sh', '/out')
    }

    foreach ($name in @("pg_dump", "pg_restore", "psql")) {
        $fileName = if ($Platform -eq "windows") { "$name.exe" } else { $name }
        $toolPath = Join-Path $binRoot $fileName
        if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
            throw "PostgreSQL client package is missing $fileName."
        }
        Assert-PostgreSqlClientVersion -Executable $toolPath -LibraryRoot $(if ($Platform -eq "linux") { $libRoot } else { "" })
    }

    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot "POSTGRESQL_LICENSE.txt") -PathType Leaf)) {
        throw "PostgreSQL client package is missing its license file."
    }
    @{ schemaVersion = 1; purpose = 'postgresql-client'; version = $postgresVersion; platform = $Platform; architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadRoot 'postgresql-client.json') -Encoding utf8

    if (Test-Path -LiteralPath $destinationRoot) {
        Assert-NativePackagePath -Path $destinationRoot
        Remove-Item -LiteralPath $destinationRoot -Recurse -Force
    }
    Move-Item -LiteralPath $payloadRoot -Destination $destinationRoot
    Write-Host "PostgreSQL $postgresVersion client tools prepared: $destinationRoot"
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Assert-NativePackagePath -Path $stagingRoot
        if (-not (Test-ExportDocPathUnderRoot -Path $stagingRoot -Root $destinationParent)) { throw 'Staging cleanup escaped its parent.' }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
