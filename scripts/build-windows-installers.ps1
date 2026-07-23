[CmdletBinding()]
param(
    [string[]]$Editions = @("Document", "Sales", "Full"),
    [string]$CargoTargetDir,
    [switch]$AllowSystemDrive,
    [switch]$PreflightOnly,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}

$Editions = @($Editions |
    ForEach-Object { $_ -split "," } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique)
$invalidEditions = @($Editions | Where-Object { $_ -notin @("Document", "Sales", "Full") })
if ($Editions.Count -eq 0 -or $invalidEditions.Count -gt 0) {
    throw "Editions must contain one or more of: Document, Sales, Full. Invalid values: $($invalidEditions -join ', ')"
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-Inside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = Resolve-FullPath -Path $Path
    $fullRoot = Resolve-FullPath -Path $Root
    if (-not $fullPath.StartsWith($fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($fullPath, $fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay inside $fullRoot. Resolved path: $fullPath"
    }

    return $fullPath
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
. (Join-Path $scriptRoot "lib\initialize-local-build-environment.ps1") -RepositoryRoot $repoRoot
$powerShellExecutable = Resolve-ExportDocPowerShellExecutable
$outputRoot = Assert-Inside -Path (Join-Path $artifactsRoot "windows-installers") -Root $artifactsRoot -Purpose "Windows installer output"
$configRoot = Assert-Inside -Path (Join-Path $artifactsRoot "windows-installer-config") -Root $artifactsRoot -Purpose "Windows installer config output"
$runner = Join-Path $scriptRoot "run-tauri-local.ps1"
$nsisProvisioner = Join-Path $scriptRoot "provision-tauri-nsis.ps1"

if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    $CargoTargetDir = Join-Path $artifactsRoot "cargo-target-exportdoc"
}
$resolvedCargoTargetDir = Resolve-FullPath -Path $CargoTargetDir
$bundleRoot = Assert-Inside -Path (Join-Path $resolvedCargoTargetDir "release\bundle\nsis") -Root $resolvedCargoTargetDir -Purpose "Tauri NSIS bundle output"

if (-not $AllowSystemDrive -and
    -not [string]::IsNullOrWhiteSpace($env:SystemDrive) -and
    $resolvedCargoTargetDir.StartsWith($env:SystemDrive, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Windows installer Cargo target directory resolved to the system drive: $resolvedCargoTargetDir"
}

if ($PreflightOnly) {
    $commands = [ordered]@{}
    foreach ($commandName in @("node", "npm", "dotnet", "cargo", "curl.exe")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        $commands[$commandName] = if ($null -eq $command) { $null } else { $command.Source }
    }
    $missingCommands = @($commands.GetEnumerator() |
        Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Value) } |
        ForEach-Object Key)
    if ($missingCommands.Count -gt 0) {
        throw "Windows installer build dependencies were not found: $($missingCommands -join ', ')"
    }

    [pscustomobject]@{
        Success = $true
        Editions = $Editions
        PowerShell = $powerShellExecutable
        CargoTargetDir = $resolvedCargoTargetDir
        BundleRoot = $bundleRoot
        OutputRoot = $outputRoot
        ConfigRoot = $configRoot
        Commands = $commands
        ExistingInstallerManifest = Test-Path -LiteralPath (Join-Path $outputRoot "installers-manifest.json") -PathType Leaf
    } | ConvertTo-Json -Depth 5
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
    return
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
$provisionArguments = @("-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $nsisProvisioner, "-CargoTargetDir", $resolvedCargoTargetDir)
if ($AllowSystemDrive) {
    $provisionArguments += "-AllowSystemDrive"
}
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $provisionArguments
$version = [string]((Get-Content -LiteralPath (Join-Path $repoRoot "version.json") -Raw -Encoding UTF8 | ConvertFrom-Json).version)
$manifestPath = Join-Path $outputRoot "installers-manifest.json"
$results = New-Object System.Collections.Generic.List[object]
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $existingManifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($existingResult in @($existingManifest.installers)) {
        if ($existingResult.Edition -notin $Editions -and (Test-Path -LiteralPath $existingResult.Installer -PathType Leaf)) {
            $results.Add($existingResult)
        }
    }
}

foreach ($edition in $Editions) {
    $metadata = switch ($edition) {
        "Document" { @{ displayName = "外贸业务综合管理系统（单证员版）"; identifier = "com.exportdocmanager.desktop.document" } }
        "Sales" { @{ displayName = "外贸业务综合管理系统（业务员版）"; identifier = "com.exportdocmanager.desktop.sales" } }
        default { @{ displayName = "外贸业务综合管理系统（全功能版）"; identifier = "com.exportdocmanager.desktop.full" } }
    }
    $configPath = Join-Path $configRoot ("tauri.{0}.conf.json" -f $edition.ToLowerInvariant())

    # A requested edition replaces all of its previous versioned installers.
    # Editions that were not requested remain available and stay in the merged
    # manifest above.
    foreach ($staleInstaller in @(Get-ChildItem -LiteralPath $outputRoot -File -Filter "ExportDocManager-$edition-*-win-x64-setup.exe" -ErrorAction SilentlyContinue)) {
        $verifiedInstallerPath = Assert-Inside -Path $staleInstaller.FullName -Root $outputRoot -Purpose "stale $edition installer"
        Remove-Item -LiteralPath $verifiedInstallerPath -Force
    }
    $staleEditionManifestPath = Join-Path $outputRoot "product-edition-$edition.json"
    if (Test-Path -LiteralPath $staleEditionManifestPath) {
        $verifiedEditionManifestPath = Assert-Inside -Path $staleEditionManifestPath -Root $outputRoot -Purpose "stale $edition manifest"
        Remove-Item -LiteralPath $verifiedEditionManifestPath -Force
    }

    [ordered]@{
        productName = $metadata.displayName
        identifier = $metadata.identifier
        bundle = [ordered]@{
            windows = [ordered]@{
                nsis = [ordered]@{
                    compression = "zlib"
                }
            }
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configPath -Encoding UTF8

    if (Test-Path -LiteralPath $bundleRoot) {
        $verifiedBundleRoot = Assert-Inside -Path $bundleRoot -Root $resolvedCargoTargetDir -Purpose "stale Tauri NSIS bundle output"
        Remove-Item -LiteralPath $verifiedBundleRoot -Recurse -Force
    }

    $arguments = @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", $runner,
        "build",
        "-Bundles", "nsis",
        "-NoSign",
        "-CargoTargetDir", $resolvedCargoTargetDir,
        "-ProductEdition", $edition,
        "-Config", $configPath
    )
    if ($AllowSystemDrive) {
        $arguments += "-AllowSystemDrive"
    }

    $startedAt = Get-Date
    Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $arguments

    Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $scriptRoot "verify-package-payload.ps1"),
        "-PackageRoot", (Join-Path $artifactsRoot "tauri-bundle\resources"),
        "-Profile", "Desktop",
        "-RuntimeIdentifier", "win-x64"
    )

    $installers = @(Get-ChildItem -LiteralPath $bundleRoot -Filter "*-setup.exe" -File)
    if ($installers.Count -ne 1) {
        throw "Expected exactly one NSIS installer for $edition under $bundleRoot, found $($installers.Count)."
    }

    $editionManifestPath = Join-Path $artifactsRoot "tauri-bundle\resources\product-edition.json"
    if (-not (Test-Path -LiteralPath $editionManifestPath -PathType Leaf)) {
        throw "Bundled product edition manifest was not found: $editionManifestPath"
    }
    $editionManifest = Get-Content -LiteralPath $editionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($editionManifest.edition -ne $edition) {
        throw "Bundled product edition mismatch. Expected '$edition', got '$($editionManifest.edition)'."
    }

    $destinationName = "ExportDocManager-$edition-$version-win-x64-setup.exe"
    $destinationPath = Join-Path $outputRoot $destinationName
    Copy-Item -LiteralPath $installers[0].FullName -Destination $destinationPath -Force
    Copy-Item -LiteralPath $editionManifestPath -Destination (Join-Path $outputRoot "product-edition-$edition.json") -Force

    $results.Add([pscustomobject]@{
        Edition = $edition
        DisplayName = $editionManifest.displayName
        ProductVersion = $version
        Identifier = $metadata.identifier
        Installer = $destinationPath
        Sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
        Bytes = (Get-Item -LiteralPath $destinationPath).Length
        ElapsedSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 2)
    })
}

[ordered]@{
    schemaVersion = 1
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    platform = "windows"
    architecture = "x64"
    installers = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$verifyArguments = @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $scriptRoot "verify-windows-installers.ps1"),
    "-ExpectedEditions", ($Editions -join ",")
)
Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments $verifyArguments
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch
