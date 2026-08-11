function Resolve-ExportDocPowerShellExecutable {
    $currentProcess = Get-Process -Id $PID -ErrorAction SilentlyContinue
    if ($null -ne $currentProcess -and
        -not [string]::IsNullOrWhiteSpace($currentProcess.Path) -and
        (Test-Path -LiteralPath $currentProcess.Path -PathType Leaf)) {
        return $currentProcess.Path
    }

    foreach ($commandName in @("pwsh.exe", "powershell.exe")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command.Source
        }
    }

    throw "PowerShell executable was not found. Install PowerShell 7 or enable Windows PowerShell."
}

function Invoke-ExportDocExternal {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        & $FilePath @Arguments
    } else {
        Push-Location $WorkingDirectory
        try {
            & $FilePath @Arguments
        } finally {
            Pop-Location
        }
    }

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $exitCode."
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

    if (-not $fullPath.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated cleanup source must stay below '$fullAllowedRoot'. Resolved path: $fullPath"
    }
    if (-not $fullQuarantineRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated cleanup quarantine must stay below '$fullAllowedRoot'. Resolved path: $fullQuarantineRoot"
    }
    if ($fullPath.StartsWith($quarantinePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($fullPath, $fullQuarantineRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
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
