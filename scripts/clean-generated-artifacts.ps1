[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [switch]$IncludeNodeModules,
    [switch]$IncludePackageCaches,
    [switch]$IncludeCodexRuntimeWorkspaces,
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

function Stop-RepositoryDotNetBuildServers {
    $runtimeRoot = Join-Path $workspaceRoot ".codex-runtime"
    if (-not (Test-Path -LiteralPath $runtimeRoot)) {
        return
    }

    $dotnetFileName = if ($IsWindows -or $PSVersionTable.Platform -eq "Win32NT") {
        "dotnet.exe"
    }
    else {
        "dotnet"
    }

    $localDotnet = Get-ChildItem -LiteralPath $runtimeRoot -Directory -Filter "dotnet-sdk-*" -ErrorAction SilentlyContinue |
        Sort-Object -Property LastWriteTime -Descending |
        ForEach-Object { Join-Path $_.FullName $dotnetFileName } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($localDotnet)) {
        return
    }

    Write-Host "Stopping repository-local .NET compiler build servers..."
    $previousDotNetCliHome = $env:DOTNET_CLI_HOME
    $previousDotNetTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    $previousDotNetSkipFirstTimeExperience = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    $shutdownExitCode = 0
    try {
        $env:DOTNET_CLI_HOME = Join-Path $runtimeRoot "dotnet-cli"
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

        & $localDotnet build-server shutdown
        $shutdownExitCode = $LASTEXITCODE
    }
    finally {
        $env:DOTNET_CLI_HOME = $previousDotNetCliHome
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousDotNetTelemetryOptOut
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousDotNetSkipFirstTimeExperience
    }

    if ($shutdownExitCode -ne 0) {
        Write-Warning "Repository-local .NET build-server shutdown returned exit code $shutdownExitCode; cleanup retries will still be attempted."
    }
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
    $codexRuntimeRoot = Join-Path $workspaceRoot ".codex-runtime"
    $nodeModulesPattern = [System.IO.Path]::DirectorySeparatorChar + "node_modules" + [System.IO.Path]::DirectorySeparatorChar

    # artifacts/ is reserved for generated development output. Preserve only
    # named delivery outputs and reusable download caches unless their explicit
    # cleanup switches are supplied. This prevents newly introduced test or
    # screenshot directories from accumulating indefinitely.
    $releaseOutputNames = @("windows-desktop-run", "windows-installers", "desktop-portable", "license-keygen")
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
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".vs") -Reason "local Visual Studio workspace cache"
    Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".dotnet-cli") -Reason "repo-local dotnet CLI home cache"

    if ($IncludeCodexRuntime) {
        Add-Target -Targets $targets -Path $codexRuntimeRoot -Reason "local Codex/Playwright runtime cache"
    }
    elseif ($IncludeCodexRuntimeWorkspaces -and (Test-Path -LiteralPath $codexRuntimeRoot)) {
        $persistentRuntimeNames = @(
            ".dotnet",
            "cargo-audit",
            "dotnet-cli",
            "gh-cli",
            "gh-config",
            "npm-cache",
            "nuget-http-cache",
            "nuget-packages",
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

    # Restrict recursive compiler-output discovery to source trees. Runtime
    # data, release outputs, downloaded tools, stable resources and .git are
    # never traversed by this generic rule.
    $sourceTreeDirectories = @()
    foreach ($sourceTreeName in @("apps", "src", "tests", "tools")) {
        $sourceTreePath = Join-Path $workspaceRoot $sourceTreeName
        if (Test-Path -LiteralPath $sourceTreePath) {
            $sourceTreeDirectories += Get-ChildItem -LiteralPath $sourceTreePath -Recurse -Directory -Force -ErrorAction SilentlyContinue
        }
    }

    $sourceTreeDirectories |
        Where-Object {
            $_.Name -in @("bin", "obj", ".vite", "dist", "target") -and
            ($IncludeNodeModules -or -not $_.FullName.Contains($nodeModulesPattern))
        } |
        ForEach-Object {
            Add-Target -Targets $targets -Path $_.FullName -Reason "generated compiler or bundler output"
        }

    if ($IncludeNodeModules) {
        $sourceTreeDirectories |
            Where-Object { $_.Name -eq "node_modules" } |
            ForEach-Object {
                Add-Target -Targets $targets -Path $_.FullName -Reason "npm dependency cache"
            }
    }

    if ($IncludePackageCaches) {
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".nuget") -Reason "repo-local NuGet cache"
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot ".npm") -Reason "repo-local npm cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "nuget-packages") -Reason "repo-local NuGet package cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "nuget-http-cache") -Reason "repo-local NuGet HTTP cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "npm-cache") -Reason "repo-local npm download cache"
        Add-Target -Targets $targets -Path (Join-Path $codexRuntimeRoot "cargo-audit") -Reason "repo-local cargo-audit tool cache"
    }

    if ($IncludeLegacyRuntimeAssets) {
        Add-Target -Targets $targets -Path (Join-Path $workspaceRoot "Browsers\ChromeForTesting") -Reason "optional browser renderer asset copy"
    }

    $topLevelTargets = [System.Collections.Generic.List[object]]::new()
    foreach ($candidate in $targets | Sort-Object -Property @{ Expression = { $_.Path.Length } }, Path) {
        $coveredByParent = $false
        foreach ($existingTarget in $topLevelTargets) {
            if (Test-IsUnderPath -Path $candidate.Path -Root $existingTarget.Path) {
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

if ($plan.Count -gt 0) {
    Stop-RepositoryDotNetBuildServers
}

foreach ($target in $plan) {
    if ((Test-Path -LiteralPath $target.Path) -and
        $PSCmdlet.ShouldProcess($target.Path, "Remove generated artifact directory")) {
        Remove-DirectoryWithRetry -Path $target.Path
    }
}

Write-Host "Cleanup completed."
