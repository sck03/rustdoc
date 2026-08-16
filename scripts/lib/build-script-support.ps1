. (Join-Path $PSScriptRoot "platform-path-safety.ps1")

function Resolve-ExportDocPowerShellExecutable {
    $currentProcess = Get-Process -Id $PID -ErrorAction SilentlyContinue
    if ($null -ne $currentProcess -and
        -not [string]::IsNullOrWhiteSpace($currentProcess.Path) -and
        (Test-Path -LiteralPath $currentProcess.Path -PathType Leaf)) {
        return $currentProcess.Path
    }

    $command = Get-Command "pwsh.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "PowerShell 7 executable (pwsh) was not found."
}

function New-ExportDocProcessStartInfo {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [switch]$CaptureOutput
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    # Commands that are not captured must inherit the current console so CI and
    # local users see their native progress and failure output in real time.
    $startInfo.CreateNoWindow = $CaptureOutput
    $startInfo.RedirectStandardOutput = $CaptureOutput
    $startInfo.RedirectStandardError = $CaptureOutput
    $workingDirectoryPath = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        "."
    } else {
        $WorkingDirectory
    }
    # ProcessStartInfo otherwise inherits the process-wide native directory,
    # which PowerShell does not update when Push-Location changes its provider
    # location. Resolve through PowerShell so existing location scopes and
    # relative -WorkingDirectory values retain their expected semantics.
    $startInfo.WorkingDirectory = [System.IO.Path]::GetFullPath(
        $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($workingDirectoryPath))

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    foreach ($entry in $Environment.GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ($null -ne $startInfo.PSObject.Properties['Environment']) {
            $startInfo.Environment[$name] = $value
        } else {
            $startInfo.EnvironmentVariables[$name] = $value
        }
    }

    return $startInfo
}

function Stop-ExportDocProcessTree {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [ValidateRange(1, 60)][int]$TimeoutSeconds = 10
    )

    if ($Process.HasExited) {
        return $true
    }

    $killCommand = @'
& {
    try {
        $target = [System.Diagnostics.Process]::GetProcessById([int]$args[0])
        $target.Kill($true)
    } catch [System.ArgumentException] {
        exit 0
    }
}
'@
    $helper = [System.Diagnostics.Process]::new()
    $helper.StartInfo = New-ExportDocProcessStartInfo `
        -FilePath (Resolve-ExportDocPowerShellExecutable) `
        -Arguments @("-NoProfile", "-NonInteractive", "-Command", $killCommand, $Process.Id.ToString()) `
        -CaptureOutput

    try {
        if (-not $helper.Start()) {
            throw "Could not start the bounded process-tree terminator."
        }

        if (-not $helper.WaitForExit($TimeoutSeconds * 1000)) {
            $helper.Kill()
            [void]$helper.WaitForExit(5000)
            return $false
        }

        return $Process.WaitForExit(5000)
    } finally {
        if (-not $helper.HasExited) {
            $helper.Kill()
            [void]$helper.WaitForExit(5000)
        }
        $helper.Dispose()
    }
}

function Wait-ExportDocExternalProcess {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds,
        [ValidateRange(1, 3600)][int]$HeartbeatSeconds
    )

    $startedAt = [DateTimeOffset]::UtcNow
    $nextHeartbeatAt = $startedAt.AddSeconds($HeartbeatSeconds)
    while (-not $Process.WaitForExit(1000)) {
        $now = [DateTimeOffset]::UtcNow
        if ($now - $startedAt -ge [TimeSpan]::FromSeconds($TimeoutSeconds)) {
            if (-not (Stop-ExportDocProcessTree -Process $Process)) {
                Write-Warning "Timed-out process '$DisplayName' did not confirm termination within the cleanup deadline."
            }
            throw "External command timed out after $TimeoutSeconds seconds: $DisplayName"
        }

        if ($now -ge $nextHeartbeatAt) {
            $elapsed = [Math]::Floor(($now - $startedAt).TotalSeconds)
            Write-Host "External command is still running (${elapsed}s): $DisplayName"
            $nextHeartbeatAt = $now.AddSeconds($HeartbeatSeconds)
        }
    }
}

function Invoke-ExportDocExternal {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 7200,
        [ValidateRange(1, 3600)][int]$HeartbeatSeconds = 60,
        [hashtable]$Environment = @{},
        [switch]$CaptureOutput
    )

    $displayName = "$FilePath $($Arguments -join ' ')".Trim()
    $startInfo = New-ExportDocProcessStartInfo `
        -FilePath $FilePath `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -Environment $Environment `
        -CaptureOutput:$CaptureOutput
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start external command: $displayName"
    }

    try {
        if ($CaptureOutput) {
            $standardOutput = $process.StandardOutput.ReadToEndAsync()
            $standardError = $process.StandardError.ReadToEndAsync()
        }
        Wait-ExportDocExternalProcess `
            -Process $process `
            -DisplayName $displayName `
            -TimeoutSeconds $TimeoutSeconds `
            -HeartbeatSeconds $HeartbeatSeconds
        $output = if ($CaptureOutput) { $standardOutput.GetAwaiter().GetResult() } else { "" }
        $errorOutput = if ($CaptureOutput) { $standardError.GetAwaiter().GetResult() } else { "" }
        if ($process.ExitCode -ne 0) {
            $diagnostic = if ([string]::IsNullOrWhiteSpace($errorOutput)) { $output } else { $errorOutput }
            $suffix = if ([string]::IsNullOrWhiteSpace($diagnostic)) { "." } else { ": $($diagnostic.Trim())" }
            throw "$displayName failed with exit code $($process.ExitCode)$suffix"
        }
        if ($CaptureOutput) {
            return [pscustomobject]@{ Output = $output; Error = $errorOutput; ExitCode = $process.ExitCode }
        }
    } finally {
        $process.Dispose()
    }
}

function Move-ExportDocGeneratedDirectoryToQuarantine {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AllowedRoot,
        [Parameter(Mandatory = $true)][string]$QuarantineRoot,
        [string]$FailureMessage
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullAllowedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullQuarantineRoot = [System.IO.Path]::GetFullPath($QuarantineRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $allowedPrefix = $fullAllowedRoot + [System.IO.Path]::DirectorySeparatorChar
    $quarantinePrefix = $fullQuarantineRoot + [System.IO.Path]::DirectorySeparatorChar

    $pathComparison = Get-ExportDocPathComparison
    if (-not $fullPath.StartsWith($allowedPrefix, $pathComparison)) {
        throw "Generated cleanup source must stay below '$fullAllowedRoot'. Resolved path: $fullPath"
    }
    if (-not $fullQuarantineRoot.StartsWith($allowedPrefix, $pathComparison)) {
        throw "Generated cleanup quarantine must stay below '$fullAllowedRoot'. Resolved path: $fullQuarantineRoot"
    }
    if ($fullPath.StartsWith($quarantinePrefix, $pathComparison) -or
        [string]::Equals($fullPath, $fullQuarantineRoot, $pathComparison)) {
        throw "Generated cleanup source cannot already be inside the quarantine root: $fullPath"
    }

    New-Item -ItemType Directory -Path $fullQuarantineRoot -Force | Out-Null
    $leafName = [System.IO.Path]::GetFileName($fullPath)
    $quarantineName = "{0}-{1:yyyyMMdd-HHmmss-fff}-{2}-{3}" -f `
        $leafName,
        (Get-Date),
        $PID,
        ([Guid]::NewGuid().ToString("N"))
    $destination = Join-Path $fullQuarantineRoot $quarantineName
    Move-Item -LiteralPath $fullPath -Destination $destination -Force -ErrorAction Stop

    if (Test-Path -LiteralPath $fullPath) {
        throw "Generated directory remained at its original path after quarantine move: $fullPath"
    }

    $reason = if ([string]::IsNullOrWhiteSpace($FailureMessage)) {
        "the directory could not be removed"
    } else {
        $FailureMessage
    }
    Write-Warning "Generated directory was moved out of the package after cleanup failed ($reason). Quarantine: $destination"
}

function Remove-ExportDocDirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaximumAttempts = 12,
        [int]$InitialDelayMilliseconds = 250,
        [string]$AllowedRoot,
        [string]$QuarantineRoot
    )

    if ($MaximumAttempts -lt 1) {
        throw "MaximumAttempts must be at least 1."
    }
    if ($InitialDelayMilliseconds -lt 0) {
        throw "InitialDelayMilliseconds must not be negative."
    }
    $hasAllowedRoot = -not [string]::IsNullOrWhiteSpace($AllowedRoot)
    $hasQuarantineRoot = -not [string]::IsNullOrWhiteSpace($QuarantineRoot)
    if ($hasAllowedRoot -ne $hasQuarantineRoot) {
        throw "AllowedRoot and QuarantineRoot must be provided together."
    }

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -ge $MaximumAttempts) {
                if ($hasAllowedRoot) {
                    Move-ExportDocGeneratedDirectoryToQuarantine `
                        -Path $Path `
                        -AllowedRoot $AllowedRoot `
                        -QuarantineRoot $QuarantineRoot `
                        -FailureMessage $_.Exception.Message
                    return
                }
                throw "Directory could not be removed after $MaximumAttempts attempts: $Path. $($_.Exception.Message)"
            }

            # WebView2 can keep BrowserMetrics files open briefly after its
            # parent process has exited. Let the runtime finish shutting down
            # before treating portable smoke cleanup as a build failure.
            $delayMilliseconds = [Math]::Min(2000, $InitialDelayMilliseconds * $attempt)
            Start-Sleep -Milliseconds $delayMilliseconds
        }
    }
}

function Test-ExportDocPauseEnabled {
    param([bool]$NoPauseRequested = $false)

    if ($NoPauseRequested -or
        $env:EXPORTDOCMANAGER_NO_PAUSE -eq "1" -or
        $env:CI -eq "true" -or
        $env:CI -eq "1") {
        return $false
    }

    return $true
}

function Wait-ExportDocInteractiveExit {
    param(
        [Parameter(Mandatory = $true)][bool]$Enabled,
        [int]$ExitCode = 0
    )

    if (-not $Enabled -or $env:EXPORTDOCMANAGER_NO_PAUSE -eq "1") {
        return
    }

    $prompt = if ($ExitCode -eq 0) {
        "操作已完成。按任意键关闭窗口。"
    } else {
        "操作失败（退出码 $ExitCode）。请查看上方错误信息，按任意键关闭窗口。"
    }
    Write-Host ""
    Write-Host $prompt

    if ($env:OS -eq "Windows_NT" -and
        -not [string]::IsNullOrWhiteSpace($env:ComSpec) -and
        (Test-Path -LiteralPath $env:ComSpec -PathType Leaf)) {
        & $env:ComSpec /d /c "pause >nul"
        return
    }

    Read-Host | Out-Null
}

function Write-ExportDocScriptFailure {
    param([Parameter(Mandatory = $true)]$ErrorRecord)

    $message = if ($null -ne $ErrorRecord.Exception) {
        $ErrorRecord.Exception.Message
    } else {
        [string]$ErrorRecord
    }
    Write-Host ""
    Write-Host "操作失败：$message" -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($ErrorRecord.InvocationInfo.PositionMessage)) {
        Write-Host $ErrorRecord.InvocationInfo.PositionMessage -ForegroundColor DarkGray
    }
}
