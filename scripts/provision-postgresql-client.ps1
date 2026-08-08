[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet("windows", "linux")][string]$Platform,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")

$postgresVersion = "18.4"
$windowsArchiveName = "postgresql-18.4-1-windows-x64-binaries.zip"
$windowsArchiveUri = "https://get.enterprisedb.com/postgresql/$windowsArchiveName"
$windowsArchiveSha256 = "7effe34c0bf89027b3f171447d351cbc460f4566c8d0f643daec67f140787858"
$linuxImage = "postgres:18.4-trixie"

$destinationRoot = [System.IO.Path]::GetFullPath($Destination)
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

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    [void]$startInfo.ArgumentList.Add("--version")
    if (-not [string]::IsNullOrWhiteSpace($LibraryRoot)) {
        $existing = [Environment]::GetEnvironmentVariable("LD_LIBRARY_PATH")
        $startInfo.Environment["LD_LIBRARY_PATH"] = if ([string]::IsNullOrWhiteSpace($existing)) {
            $LibraryRoot
        } else {
            "$LibraryRoot`:$existing"
        }
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start PostgreSQL client executable: $Executable"
    }
    try {
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "PostgreSQL client version check failed: $errorOutput"
        }
        if ($output -notmatch '^\S+ \(PostgreSQL\) 18\.') {
            throw "Unexpected PostgreSQL client version: $output"
        }
    } finally {
        $process.Dispose()
    }
}

try {
    if ($Platform -eq "windows") {
        $archivePath = Join-Path $downloadRoot $windowsArchiveName
        Invoke-WebRequest -Uri $windowsArchiveUri -OutFile $archivePath
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
    } else {
        $containerScript = @'
set -eu
source_bin=/usr/lib/postgresql/18/bin
for tool in pg_dump pg_restore psql; do
  cp "$source_bin/$tool" "/out/bin/$tool"
done
for tool in pg_dump pg_restore psql; do
  ldd "$source_bin/$tool"
done | awk '/=> \/[^ ]+/ { print $3 } /^[[:space:]]*\/[^ ]+/ { print $1 }' | sort -u | while IFS= read -r library; do
  name=$(basename "$library")
  case "$name" in
    libc.so.*|libm.so.*|libpthread.so.*|libdl.so.*|librt.so.*|ld-linux*|ld-musl*) continue ;;
  esac
  cp -L "$library" "/out/lib/$name"
done
if [ -f /usr/share/doc/postgresql-18/copyright ]; then
  cp /usr/share/doc/postgresql-18/copyright /out/POSTGRESQL_LICENSE.txt
elif [ -f /usr/share/doc/postgresql-client-18/copyright ]; then
  cp /usr/share/doc/postgresql-client-18/copyright /out/POSTGRESQL_LICENSE.txt
else
  printf '%s\n' 'PostgreSQL is distributed under the PostgreSQL License: https://www.postgresql.org/about/licence/' > /out/POSTGRESQL_LICENSE.txt
fi
chmod 0755 /out/bin/pg_dump /out/bin/pg_restore /out/bin/psql
chmod 0644 /out/lib/* /out/POSTGRESQL_LICENSE.txt
'@
        Invoke-ExportDocExternal -FilePath "docker" -Arguments @(
            "run",
            "--rm",
            "--entrypoint", "sh",
            "--volume", "$($payloadRoot):/out",
            $linuxImage,
            "-eu",
            "-c", $containerScript)
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

    if (Test-Path -LiteralPath $destinationRoot) {
        Remove-Item -LiteralPath $destinationRoot -Recurse -Force
    }
    Move-Item -LiteralPath $payloadRoot -Destination $destinationRoot
    Write-Host "PostgreSQL $postgresVersion client tools prepared: $destinationRoot"
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
