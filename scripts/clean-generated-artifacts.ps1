[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [switch]$IncludeNodeModules,
    [switch]$IncludePackageCaches,
    [switch]$IncludeCodexRuntimeWorkspaces,
    [switch]$IncludeCodexRuntime,
    [switch]$IncludeReleaseOutputs,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"
$cleanupCmdlet = $PSCmdlet

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
$workspaceRootFullPath = [System.IO.Path]::GetFullPath($workspaceRoot)
. (Join-Path $scriptRoot "lib/platform-path-safety.ps1")
. (Join-Path $scriptRoot "lib/generated-artifact-protection.ps1")

function Assert-WorkspaceChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-ExportDocPathUnderRoot -Path $fullPath -Root $workspaceRootFullPath)) {
        throw "Refusing to clean $Purpose outside workspace: $fullPath"
    }

    if ($fullPath -eq $workspaceRootFullPath) {
        throw "Refusing to clean workspace root."
    }

    return $fullPath
}

function Get-GeneratedArtifactSizeBytes {
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

function Grant-CurrentUserGeneratedPathAccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ((-not $IsWindows -and $PSVersionTable.Platform -ne "Win32NT") -or
        -not (Test-Path -LiteralPath $Path)) {
        return
    }

    $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $entries = @((Get-Item -LiteralPath $Path -Force)) +
        @(Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue)

    foreach ($entry in $entries) {
        try {
            $acl = Get-Acl -LiteralPath $entry.FullName
            $inheritanceFlags = if ($entry.PSIsContainer) {
                [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                    [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            }
            else {
                [System.Security.AccessControl.InheritanceFlags]::None
            }
            $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                $currentIdentity,
                [System.Security.AccessControl.FileSystemRights]::FullControl,
                $inheritanceFlags,
                [System.Security.AccessControl.PropagationFlags]::None,
                [System.Security.AccessControl.AccessControlType]::Allow)
            $acl.SetAccessRule($rule)
            Set-Acl -LiteralPath $entry.FullName -AclObject $acl
        }
        catch {
            Write-Verbose "Could not normalize generated-path ACL for '$($entry.FullName)': $($_.Exception.Message)"
        }
    }
}

function Remove-GeneratedArtifactWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$RetryCount = 5
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $Path = Assert-WorkspaceChildPath -Path $Path -Purpose "generated artifact"
    if (Test-ExportDocProtectedArtifact -Path $Path -IncludeReleaseOutputs:$IncludeReleaseOutputs) {
        throw "Refusing to remove protected data or linked content: $Path"
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
            if ($attempt -eq 1) {
                Grant-CurrentUserGeneratedPathAccess -Path $Path
            }

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
    if (Test-ExportDocProtectedArtifact -Path $fullPath -IncludeReleaseOutputs:$IncludeReleaseOutputs) {
        Write-Verbose "Preserving data or linked content: $fullPath"
        return $null
    }
    [PSCustomObject]@{
        Path = $fullPath
        Reason = $Reason
        SizeBytes = Get-GeneratedArtifactSizeBytes -Path $fullPath
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
        if (Test-ExportDocPathEqual -Left $existingTarget.Path -Right $target.Path) {
            $alreadyAdded = $true
            break
        }
    }

    if (-not $alreadyAdded) {
        [void]$Targets.Add($target)
    }
}

function Get-GeneratedArtifactCleanupPlan {
    $targets = [System.Collections.Generic.List[object]]::new()
    $artifactsRoot = Join-Path $workspaceRoot "artifacts"
    $codexRuntimeRoot = Join-Path $workspaceRoot ".codex-runtime"

    # artifacts/ is reserved for generated development output. Preserve only
    # named delivery outputs and reusable download caches unless their explicit
    # cleanup switches are supplied. This prevents newly introduced test or
    # screenshot directories from accumulating indefinitely.
    $releaseOutputNames = @(
        "releases",
        "native-desktop",
        "native-web-server",
        "windows-desktop-run",
        "windows-installers",
        "desktop-portable",
        "desktop-portable-final",
        "license-keygen"
    )
    $artifactCacheNames = @(
        "cargo-audit",
        "chrome-for-testing",
        "playwright-browsers",
        "rustsec-advisory-db",
        "tool-downloads"
    )

    if (Test-Path -LiteralPath $artifactsRoot) {
        foreach ($artifactDirectory in Get-ChildItem -LiteralPath $artifactsRoot -Directory -Force -ErrorAction SilentlyContinue) {
            if ($artifactDirectory.Name -in $releaseOutputNames) {
                if ($IncludeReleaseOutputs) {
                    Add-Target -Targets $targets -Path $artifactDirectory.FullName -Reason "explicitly requested release output cleanup"
                }
                continue
            }

            if ($artifactDirectory.Name -in $artifactCacheNames) {
                if ($IncludePackageCaches) {
                    Add-Target -Targets $targets -Path $artifactDirectory.FullName -Reason "explicitly requested reusable package or download cache cleanup"
                }
                continue
            }

            Add-Target -Targets $targets -Path $artifactDirectory.FullName -Reason "reproducible build, validation, screenshot, or test output"
        }
    }

    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "TestResults") -Reason "test result output"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "tmp") -Reason "repository-local temporary output"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "target") -Reason "root Rust workspace build output"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".vs") -Reason "local Visual Studio workspace cache"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "apps/.codex-runtime/cargo-target-tauri") -Reason "legacy Tauri Cargo build output"

    if ($IncludeCodexRuntime) {
        Add-Target -Targets $targets -Path $codexRuntimeRoot -Reason "local Codex/Playwright runtime cache"
    }
    elseif ($IncludeCodexRuntimeWorkspaces -and (Test-Path -LiteralPath $codexRuntimeRoot)) {
        foreach ($log in Get-ChildItem -LiteralPath $codexRuntimeRoot -File -Filter '*.log' -Force -ErrorAction Stop) {
            Add-Target -Targets $targets -Path $log.FullName -Reason 'explicitly requested local development and test log cleanup'
        }
        $persistentRuntimeNames = @(
            ".dotnet",
            "cargo-audit",
            "cargo-audit-tool",
            "cargo-home",
            "dotnet-cli",
            "gh-cli",
            "gh-config",
            "npm-cache",
            "pip-cache",
            "native-runtime-packages",
            "native-ocr-crt",
            "postgresql-client",
            "postgresql-review",
            "nuget-http-cache",
            "nuget-packages",
            "playwright-browsers",
            "rustup-home",
            "tools"
        )

        foreach ($runtimeDirectory in Get-ChildItem -LiteralPath $codexRuntimeRoot -Directory -Force -ErrorAction SilentlyContinue) {
            if ($runtimeDirectory.Name -in $persistentRuntimeNames -or
                $runtimeDirectory.Name -like "dotnet-sdk-*") {
                continue
            }

            Add-Target -Targets $targets -Path $runtimeDirectory.FullName -Reason "explicitly requested disposable local test workspace cleanup"
        }
    }

    # Prune dependency, data and output trees before traversal. In particular,
    # never discover a dependency's own dist/bin directories as separate targets.
    $pending = [System.Collections.Generic.Stack[string]]::new()
    foreach ($sourceTreeName in @("apps", "crates", "src", "tests", "tools")) {
        $sourceTreePath = Join-Path $workspaceRoot $sourceTreeName
        if (Test-Path -LiteralPath $sourceTreePath -PathType Container) {
            $pending.Push($sourceTreePath)
        }
    }
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
        if ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
        if (Test-Path -LiteralPath (Join-Path $directory.FullName '.git')) { continue }
        if ($directory.Name -in @('.git', '.codex-runtime', 'App_Data', 'Database', 'Backups', 'Security', 'KEY', 'Templates', 'OcrModels', 'Resources')) { continue }
        if ($directory.Name -eq 'node_modules') {
            if ($IncludeNodeModules) {
                Add-Target -Targets $targets -Path $directory.FullName -Reason 'explicitly requested npm dependency tree cleanup'
            }
            continue
        }
        if ($directory.Name -in @('bin', 'obj', '.vite', 'dist', 'target')) {
            Add-Target -Targets $targets -Path $directory.FullName -Reason 'generated compiler or bundler output'
            continue
        }
        foreach ($child in Get-ChildItem -LiteralPath $directory.FullName -Directory -Force -ErrorAction Stop) {
            $pending.Push($child.FullName)
        }
    }

    if ($IncludePackageCaches) {
        foreach ($name in @('.pnpm-store', '.dotnet-cli')) {
            Add-Target -Targets $targets -Path (Join-Path $workspaceRoot $name) -Reason 'explicitly requested legacy tool cache cleanup'
        }
        foreach ($name in @('native-runtime-packages', 'native-ocr-crt', 'postgresql-client', 'cargo-audit-tool')) {
            Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot $name) -Reason 'explicitly requested native resource or tool cache cleanup'
        }
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".nuget") -Reason "repo-local NuGet cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".npm") -Reason "repo-local npm cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "nuget-packages") -Reason "repo-local NuGet package cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "nuget-http-cache") -Reason "repo-local NuGet HTTP cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "npm-cache") -Reason "repo-local npm download cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "cargo-home") -Reason "repo-local Cargo download cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "cargo-audit") -Reason "repo-local cargo-audit tool cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "apps/.codex-runtime/cargo-home") -Reason "legacy Tauri Cargo download cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "apps/.codex-runtime/npm-cache") -Reason "legacy app npm download cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "apps/export-doc-tauri/.codex-runtime/npm-cache") -Reason "Tauri npm download cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "apps/export-doc-web/.codex-runtime/npm-cache") -Reason "Web npm download cache"
    }

    $topLevelTargets = [System.Collections.Generic.List[object]]::new()
    foreach ($candidate in $targets | Sort-Object -Property @{ Expression = { $_.Path.Length } }, Path) {
        $coveredByParent = $false
        foreach ($existingTarget in $topLevelTargets) {
            if (Test-ExportDocPathUnderRoot -Path $candidate.Path -Root $existingTarget.Path -AllowRoot) {
                $coveredByParent = $true
                break
            }
        }

        if (-not $coveredByParent) {
            [void]$topLevelTargets.Add($candidate)
        }
    }

    $topLevelTargets | Sort-Object -Property SizeBytes -Descending
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


$cleanupFailures = [System.Collections.Generic.List[object]]::new()
foreach ($target in $plan) {
    if ((Test-Path -LiteralPath $target.Path) -and
        $cleanupCmdlet.ShouldProcess($target.Path, "Remove generated artifact")) {
        try {
            Remove-GeneratedArtifactWithRetry -Path $target.Path
        }
        catch {
            [void]$cleanupFailures.Add([PSCustomObject]@{
                Path = $target.Path
                Message = $_.Exception.Message
            })
            Write-Warning "Could not completely remove generated artifact '$($target.Path)': $($_.Exception.Message)"
        }
    }
}

if ($cleanupFailures.Count -gt 0) {
    Write-Warning ("Cleanup completed with {0} target failure(s):" -f $cleanupFailures.Count)
    foreach ($failure in $cleanupFailures) {
        Write-Warning "  $($failure.Path): $($failure.Message)"
    }

    throw "Generated artifact cleanup left one or more targets incomplete. Review the warnings above."
}

Write-Host "Cleanup completed."
