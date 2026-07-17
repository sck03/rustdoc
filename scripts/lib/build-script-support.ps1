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
