[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [switch]$IncludeNodeModules,
    [switch]$IncludePackageCaches,
    [switch]$IncludeCodexRuntime,
    [switch]$IncludeLegacyRuntimeAssets,
    [switch]$IncludeReleaseOutputs,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
$workspaceRootFullPath = [System.IO.Path]::GetFullPath($workspaceRoot)

function Assert-WorkspaceChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $workspacePrefix = $workspaceRootFullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean $Purpose outside workspace: $fullPath"
    }

    if ($fullPath -eq $workspaceRootFullPath) {
        throw "Refusing to clean workspace root."
    }

    return $fullPath
}

function Get-DirectorySizeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    $sum = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum

    if ($null -eq $sum.Sum) {
        return 0
    }

    return [long]$sum.Sum
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$RetryCount = 5
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                ForEach-Object {
                    if ($_.Attributes -band [System.IO.FileAttributes]::ReadOnly) {
                        $_.Attributes = $_.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                    }
                }

            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            try {
                [System.GC]::Collect()
                [System.GC]::WaitForPendingFinalizers()
                [System.IO.Directory]::Delete((ConvertTo-ExtendedLengthPath -Path $Path), $true)
                return
            }
            catch {
                if ($attempt -eq $RetryCount) {
                    throw
                }
            }

            if ($attempt -eq $RetryCount) {
                throw
            }

            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

function ConvertTo-ExtendedLengthPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not $IsWindows -and $PSVersionTable.Platform -ne "Win32NT") {
        return $Path
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith("\\?\", [System.StringComparison]::Ordinal)) {
        return $fullPath
    }

    if ($fullPath.StartsWith("\\", [System.StringComparison]::Ordinal)) {
        return "\\?\UNC\" + $fullPath.TrimStart("\")
    }

    return "\\?\$fullPath"
}

function New-CleanupTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $fullPath = Assert-WorkspaceChildPath -Path $Path -Purpose $Reason
    [PSCustomObject]@{
        Path = $fullPath
        Reason = $Reason
        SizeBytes = Get-DirectorySizeBytes -Path $fullPath
    }
}

function Add-Target {
    param(
        [System.Collections.Generic.List[object]]$Targets,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    $target = New-CleanupTarget -Path $Path -Reason $Reason
    if ($null -eq $target) {
        return
    }

    $alreadyAdded = $false
    foreach ($existingTarget in $Targets) {
        if ($existingTarget.Path.Equals($target.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
            $alreadyAdded = $true
            break
        }
    }

    if (-not $alreadyAdded) {
        [void]$Targets.Add($target)
    }
}

function Test-IsUnderPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)

    return $fullPath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith(
            $fullRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-GeneratedArtifactCleanupPlan {
    $targets = [System.Collections.Generic.List[object]]::new()
    $artifactsRoot = Join-Path $workspaceRoot "artifacts"
    $gitRoot = Join-Path $workspaceRoot ".git"
    $codexRuntimeRoot = Join-Path $workspaceRoot ".codex-runtime"
    $nodeModulesPattern = [System.IO.Path]::DirectorySeparatorChar + "node_modules" + [System.IO.Path]::DirectorySeparatorChar

    # Keep customer-facing packages by default. Only compiler/bundler caches and
    # explicitly disposable smoke outputs are cleaned in the safe default mode.
    foreach ($name in @(
        "cargo-target-exportdoc",
        "cargo-target-license-keygen",
        "cargo-target-excel-analyzer",
        "tauri-bundle",
        "dev-server",
        "login-visual-check",
        "windows-installer-config",
        "windows-installer-smoke"
    )) {
        Add-Target -Targets $targets -Path (Join-Path $artifactsRoot $name) -Reason "reproducible build or smoke output"
    }

    if ($IncludeReleaseOutputs) {
        foreach ($name in @("windows-desktop-run", "windows-installers")) {
            Add-Target -Targets $targets -Path (Join-Path $artifactsRoot $name) -Reason "explicitly requested release output cleanup"
        }
    }
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "TestResults") -Reason "test result output"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".vs") -Reason "local Visual Studio workspace cache"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".dotnet-cli") -Reason "repo-local dotnet CLI home cache"

    if ($IncludeCodexRuntime) {
        Add-Target -Targets $targets -Path $codexRuntimeRoot -Reason "local Codex/Playwright runtime cache"
    }

    Get-ChildItem -LiteralPath $workspaceRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -in @("bin", "obj", ".vite", "dist", "target") -and
            -not (Test-IsUnderPath -Path $_.FullName -Root $artifactsRoot) -and
            -not (Test-IsUnderPath -Path $_.FullName -Root $gitRoot) -and
            ($IncludeCodexRuntime -or -not (Test-IsUnderPath -Path $_.FullName -Root $codexRuntimeRoot)) -and
            ($IncludeNodeModules -or (-not ($_.FullName.Contains($nodeModulesPattern))))
        } |
        ForEach-Object {
            Add-Target -Targets $targets -Path $_.FullName -Reason "generated compiler or bundler output"
        }

    if ($IncludeNodeModules) {
        Get-ChildItem -LiteralPath $workspaceRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq "node_modules" } |
            ForEach-Object {
                Add-Target -Targets $targets -Path $_.FullName -Reason "npm dependency cache"
            }
    }

    if ($IncludePackageCaches) {
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".nuget") -Reason "repo-local NuGet cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".npm") -Reason "repo-local npm cache"
    }

    if ($IncludeLegacyRuntimeAssets) {
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "Browsers\ChromeForTesting") -Reason "optional browser renderer asset copy"
    }

    $targets |
        Sort-Object -Property Path -Unique |
        Sort-Object -Property SizeBytes -Descending
}

$plan = @(Get-GeneratedArtifactCleanupPlan)
$totalBytes = ($plan | Measure-Object -Property SizeBytes -Sum).Sum
if ($null -eq $totalBytes) {
    $totalBytes = 0
}

Write-Host "Generated artifact cleanup plan:"
Write-Host "  Workspace : $workspaceRootFullPath"
Write-Host ("  Targets   : {0}" -f $plan.Count)
Write-Host ("  Total     : {0:N1} MB" -f ($totalBytes / 1MB))

foreach ($target in $plan) {
    Write-Host ("  {0,10:N1} MB  {1}" -f ($target.SizeBytes / 1MB), $target.Path)
    Write-Host "              $($target.Reason)"
}

if ($ListOnly) {
    return
}

foreach ($target in $plan) {
    if ((Test-Path -LiteralPath $target.Path) -and
        $PSCmdlet.ShouldProcess($target.Path, "Remove generated artifact directory")) {
        Remove-DirectoryWithRetry -Path $target.Path
    }
}

Write-Host "Cleanup completed."
