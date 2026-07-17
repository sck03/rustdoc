[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$scriptFiles = @(Get-ChildItem -LiteralPath $scriptRoot -Recurse -File)
$powerShellScripts = @($scriptFiles | Where-Object Extension -eq ".ps1")
$commandScripts = @($scriptFiles | Where-Object Extension -eq ".cmd")
$moduleScripts = @($scriptFiles | Where-Object Extension -eq ".mjs")

$parseFailures = New-Object System.Collections.Generic.List[string]
foreach ($file in $powerShellScripts) {
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
        $parseFailures.Add("$($file.FullName):$($parseError.Extent.StartLineNumber) $($parseError.Message)")
    }
}
if ($parseFailures.Count -gt 0) {
    throw "PowerShell syntax validation failed:`n$($parseFailures -join "`n")"
}

foreach ($file in $moduleScripts) {
    Invoke-ExportDocExternal -FilePath "node" -Arguments @("--check", $file.FullName) -WorkingDirectory $repoRoot
}

$publicCommandScripts = @(
    Get-ChildItem -LiteralPath $scriptRoot -File -Filter "*.cmd" |
        Sort-Object Name
)
foreach ($file in $publicCommandScripts) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $targetMatch = [regex]::Match(
        $content,
        'set\s+"EXPORTDOCMANAGER_PS_SCRIPT=%~dp0(?<target>[^"\r\n]+\.ps1)"',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $targetMatch.Success) {
        throw "Public CMD entry does not declare EXPORTDOCMANAGER_PS_SCRIPT: $($file.FullName)"
    }

    $targetPath = Join-Path $scriptRoot $targetMatch.Groups["target"].Value
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        throw "Public CMD target PowerShell script was not found: $targetPath"
    }
    if ($content -notmatch '"%~dp0lib\\run-powershell-entry\.cmd"\s+%\*') {
        throw "Public CMD entry must delegate to the shared host and forward all arguments: $($file.FullName)"
    }
    foreach ($forbiddenPattern in @('where\s+pwsh', 'where\s+powershell', '\bpause\b', '%ERRORLEVEL%')) {
        if ($content -match $forbiddenPattern) {
            throw "Public CMD entry duplicates shared host logic ($forbiddenPattern): $($file.FullName)"
        }
    }
}

$hostPath = Join-Path $scriptRoot "lib\run-powershell-entry.cmd"
$hostContent = Get-Content -LiteralPath $hostPath -Raw -Encoding UTF8
foreach ($requiredPattern in @(
    'where pwsh\.exe',
    'where powershell\.exe',
    'set "EXIT_CODE=%ERRORLEVEL%"',
    'pause >nul',
    'endlocal & exit /b %EXIT_CODE%'
)) {
    if ($hostContent -notmatch $requiredPattern) {
        throw "Shared CMD host is missing required behavior '$requiredPattern': $hostPath"
    }
}

$forbiddenPowerShellPatterns = @(
    '(?im)^\s*Invoke-Expression\b',
    '(?im)^\s*Set-ExecutionPolicy\b',
    'Path\.GetTempPath',
    'Environment\.GetFolderPath',
    'SpecialFolder\.',
    '(?i)(?:^|["''])C:\\'
)
foreach ($file in $powerShellScripts) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($pattern in $forbiddenPowerShellPatterns) {
        if ($content -match $pattern) {
            throw "PowerShell script contains forbidden system-state or system-drive pattern '$pattern': $($file.FullName)"
        }
    }
}

$approvedDirectNativeCommands = @{
    "provision-tauri-nsis.ps1" = @("curl.exe")
    "smoke-tauri-desktop.ps1" = @("node", "npm")
}
$nativeCommandNames = @("dotnet", "node", "npm", "cargo", "rustc", "curl.exe", "cmd.exe", "pwsh.exe", "powershell.exe")
$directNativeCommands = New-Object System.Collections.Generic.List[object]
foreach ($file in $powerShellScripts) {
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$parseErrors)
    $commandAsts = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true)
    foreach ($commandAst in $commandAsts) {
        $commandName = $commandAst.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($commandName) -or $commandName.ToLowerInvariant() -notin $nativeCommandNames) {
            continue
        }

        $allowedNames = @($approvedDirectNativeCommands[$file.Name])
        if ($commandName.ToLowerInvariant() -notin $allowedNames) {
            throw "Native command '$commandName' must use Invoke-ExportDocExternal so its exit code is preserved: $($file.FullName):$($commandAst.Extent.StartLineNumber)"
        }
        $directNativeCommands.Add([pscustomobject]@{
            File = $file.Name
            Command = $commandName.ToLowerInvariant()
            Line = $commandAst.Extent.StartLineNumber
        })
    }
}

[pscustomobject]@{
    Success = $true
    PowerShellScriptCount = $powerShellScripts.Count
    CommandScriptCount = $commandScripts.Count
    ModuleScriptCount = $moduleScripts.Count
    PublicCommandEntryCount = $publicCommandScripts.Count
    ApprovedDirectNativeCommandCount = $directNativeCommands.Count
} | ConvertTo-Json -Depth 4
