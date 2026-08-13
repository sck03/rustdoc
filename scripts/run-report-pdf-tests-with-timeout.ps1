param(
    [ValidateRange(1, 900)]
    [int]$PerTestTimeoutSeconds = 180,

    [ValidateRange(1, 60)]
    [int]$HeartbeatSeconds = 15,

    [string[]]$Tests = @(
        "ExportDocManager.Infrastructure.Tests.ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplatesToPdf_ShouldUseProgramRootBrowserAndRuntimeDataRoot",
        "ExportDocManager.Infrastructure.Tests.ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf_ShouldPreservePaginationAndDomainIsolation"
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "tests/ExportDocManager.Infrastructure.Tests/ExportDocManager.Infrastructure.Tests.csproj"
$diagnosticRoot = Join-Path $repositoryRoot ".codex-runtime/report-pdf-test-watchdog"
$watchdogLogPath = Join-Path $diagnosticRoot "watchdog.log"

[System.IO.Directory]::CreateDirectory($diagnosticRoot) | Out-Null
[System.IO.File]::WriteAllText($watchdogLogPath, [string]::Empty)

function Write-WatchdogMessage {
    param([Parameter(Mandatory)][string]$Message)

    $line = "{0} {1}" -f [DateTimeOffset]::UtcNow.ToString("O"), $Message
    Write-Host $line
    Add-Content -LiteralPath $watchdogLogPath -Value $line
}

function Stop-TestProcessTree {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    try {
        if (-not $Process.HasExited) {
            $Process.Kill($true)
        }
    }
    catch {
        Write-WatchdogMessage "Process-tree termination failed for PID $($Process.Id): $($_.Exception.Message)"
        try {
            if (-not $Process.HasExited) {
                $Process.Kill()
            }
        }
        catch {
            Write-WatchdogMessage "Parent-process termination also failed for PID $($Process.Id): $($_.Exception.Message)"
        }
    }

    try {
        [void]$Process.WaitForExit(10000)
    }
    catch {
        Write-WatchdogMessage "PID $($Process.Id) did not confirm exit within the cleanup grace period."
    }
}

function Resolve-DotnetExecutable {
    $requiredSdk = (Get-Content -LiteralPath (Join-Path $repositoryRoot "global.json") -Raw |
        ConvertFrom-Json).sdk.version
    $candidates = Get-Command dotnet -CommandType Application -All -ErrorAction Stop |
        Where-Object {
            $_.Name -in @("dotnet", "dotnet.exe") -and
            [System.IO.Path]::GetExtension($_.Source) -notin @(".cmd", ".bat") -and
            (Test-Path -LiteralPath $_.Source -PathType Leaf)
        } |
        Select-Object -ExpandProperty Source -Unique

    foreach ($candidate in $candidates) {
        $probe = [System.Diagnostics.Process]::new()
        $probeStarted = $false
        $probe.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $probe.StartInfo.FileName = $candidate
        $probe.StartInfo.WorkingDirectory = $repositoryRoot
        $probe.StartInfo.UseShellExecute = $false
        $probe.StartInfo.CreateNoWindow = $true
        $probe.StartInfo.RedirectStandardOutput = $true
        $probe.StartInfo.RedirectStandardError = $true
        $probe.StartInfo.ArgumentList.Add("--version")

        try {
            if (-not $probe.Start()) {
                continue
            }
            $probeStarted = $true
            if (-not $probe.WaitForExit(10000)) {
                Stop-TestProcessTree -Process $probe
                continue
            }

            $version = $probe.StandardOutput.ReadToEnd().Trim()
            if ($probe.ExitCode -eq 0 -and $version -eq $requiredSdk) {
                Write-WatchdogMessage "Using .NET SDK $version from $candidate."
                return $candidate
            }
        }
        catch {
            Write-WatchdogMessage "Rejected dotnet candidate ${candidate}: $($_.Exception.Message)"
        }
        finally {
            if ($probeStarted -and -not $probe.HasExited) {
                Stop-TestProcessTree -Process $probe
            }
            $probe.Dispose()
        }
    }

    throw "No native dotnet executable on PATH can load the required SDK $requiredSdk."
}

$dotnetExecutable = Resolve-DotnetExecutable

if ($Tests.Count -eq 0) {
    throw "At least one report PDF test must be supplied."
}

foreach ($test in $Tests) {
    if ([string]::IsNullOrWhiteSpace($test)) {
        throw "Report PDF test names must not be empty."
    }

    $testSlug = ($test.Split('.')[-1] -replace '[^A-Za-z0-9_.-]', '_')
    $diagnosticLogPath = Join-Path $diagnosticRoot "$testSlug.diag.log"
    $trxFileName = "$testSlug.trx"
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $dotnetExecutable
    $process.StartInfo.WorkingDirectory = $repositoryRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $false

    $arguments = @(
        "test",
        $projectPath,
        "-c", "Release",
        "-m:1",
        "-p:BuildInParallel=false",
        "--no-build",
        "--no-restore",
        "--logger", "console;verbosity=normal",
        "--logger", "trx;LogFileName=$trxFileName",
        "--results-directory", $diagnosticRoot,
        "--diag", $diagnosticLogPath,
        "--filter", "FullyQualifiedName=$test"
    )
    foreach ($argument in $arguments) {
        $process.StartInfo.ArgumentList.Add($argument)
    }

    Write-WatchdogMessage "Starting $test with a hard timeout of $PerTestTimeoutSeconds seconds."
    if (-not $process.Start()) {
        $process.Dispose()
        throw "Failed to start dotnet test for $test."
    }

    $timedOut = $false
    $exitCode = -1
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        while (-not $process.HasExited) {
            $remaining = [TimeSpan]::FromSeconds($PerTestTimeoutSeconds) - $stopwatch.Elapsed
            if ($remaining -le [TimeSpan]::Zero) {
                $timedOut = $true
                Write-WatchdogMessage "$test exceeded its hard timeout; terminating the complete dotnet/testhost/Chrome process tree."
                Stop-TestProcessTree -Process $process
                break
            }

            $waitMilliseconds = [Math]::Max(
                1,
                [Math]::Min($HeartbeatSeconds * 1000, [Math]::Ceiling($remaining.TotalMilliseconds)))
            if ($process.WaitForExit($waitMilliseconds)) {
                break
            }

            $elapsedSeconds = [Math]::Floor($stopwatch.Elapsed.TotalSeconds)
            Write-WatchdogMessage "$test is still running after $elapsedSeconds seconds (PID $($process.Id))."
        }

        if (-not $timedOut) {
            $exitCode = $process.ExitCode
        }
    }
    finally {
        $stopwatch.Stop()
        if (-not $process.HasExited) {
            Stop-TestProcessTree -Process $process
        }
        $process.Dispose()
    }

    if ($timedOut) {
        Write-WatchdogMessage "$test failed because its process tree did not finish in time."
        exit 124
    }
    if ($exitCode -ne 0) {
        Write-WatchdogMessage "$test failed with exit code $exitCode."
        exit $exitCode
    }

    Write-WatchdogMessage "$test completed successfully in $([Math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds."
}

Write-WatchdogMessage "All long-running report PDF tests completed successfully."
