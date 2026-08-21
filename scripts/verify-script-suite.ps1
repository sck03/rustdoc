[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
. (Join-Path $scriptRoot "lib\web-runtime-smoke-arguments.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
[void](Invoke-ExportDocExternal -FilePath "npm" -Arguments @("--version") -CaptureOutput)

$smokeArgumentDefaults = @{
    ScriptPath = "smoke.mjs"
    BrowserExecutable = "browser.exe"
    WebUrl = "http://127.0.0.1:5173/"
    ApiBaseUrl = "http://127.0.0.1:5000/"
    DesktopAccessToken = "desktop-token"
    Username = "admin"
    UserDataDirectory = "browser-profile"
    TimeoutMilliseconds = 45000
}
$passwordlessSmokeArguments = New-ExportDocWebRuntimeSmokeArguments @smokeArgumentDefaults
if ($passwordlessSmokeArguments.Contains("--password") -or $passwordlessSmokeArguments.Contains("")) {
    throw "Passwordless web smoke arguments must omit both --password and empty argument values."
}
$passwordSmokeArguments = New-ExportDocWebRuntimeSmokeArguments @smokeArgumentDefaults -Password "secret value"
$passwordIndex = $passwordSmokeArguments.IndexOf("--password")
if ($passwordIndex -lt 0 -or $passwordSmokeArguments[$passwordIndex + 1] -ne "secret value") {
    throw "Web smoke arguments must preserve a supplied password as one argument."
}

$scriptFiles = @(Get-ChildItem -LiteralPath $scriptRoot -Recurse -File)
$powerShellScripts = @($scriptFiles | Where-Object Extension -eq ".ps1")
$commandScripts = @($scriptFiles | Where-Object Extension -eq ".cmd")
$moduleScripts = @($scriptFiles | Where-Object Extension -eq ".mjs")
$shellScripts = @($scriptFiles | Where-Object Extension -eq ".sh")

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

$permissionVerifier = Join-Path $scriptRoot "assert-tauri-command-permissions.ps1"
[void](Invoke-ExportDocExternal -FilePath (Resolve-ExportDocPowerShellExecutable) -Arguments @(
    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-File", $permissionVerifier,
    "-RepositoryRoot", $repoRoot
) -CaptureOutput)

foreach ($file in $moduleScripts) {
    Invoke-ExportDocExternal -FilePath "node" -Arguments @("--check", $file.FullName) -WorkingDirectory $repoRoot
}

$dependencyPolicyScript = Join-Path $scriptRoot "verify-dependency-policy.mjs"
Invoke-ExportDocExternal -FilePath "node" -Arguments @($dependencyPolicyScript) -WorkingDirectory $repoRoot
$dependencyPolicyTestScript = Join-Path $scriptRoot "test_dependency_policy.mjs"
Invoke-ExportDocExternal -FilePath "node" -Arguments @($dependencyPolicyTestScript) -WorkingDirectory $repoRoot
$dotnetSdkCompatibilityTestScript = Join-Path $scriptRoot "test_dotnet_sdk_compatibility.mjs"
Invoke-ExportDocExternal -FilePath "node" -Arguments @($dotnetSdkCompatibilityTestScript) -WorkingDirectory $repoRoot

$bashPath = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty Source
if ([string]::IsNullOrWhiteSpace($bashPath)) {
    foreach ($gitPath in @(Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source)) {
        $gitRoot = Split-Path -Parent (Split-Path -Parent $gitPath)
        $gitBashCandidate = Join-Path $gitRoot "bin\bash.exe"
        if (Test-Path -LiteralPath $gitBashCandidate -PathType Leaf) {
            $bashPath = $gitBashCandidate
            break
        }
    }
}
foreach ($file in $shellScripts) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if (-not $content.StartsWith("#!", [System.StringComparison]::Ordinal)) {
        throw "Shell script must declare an interpreter: $($file.FullName)"
    }
    if ($content.Contains("`r", [System.StringComparison]::Ordinal)) {
        throw "Shell script must use LF line endings: $($file.FullName)"
    }
    if (-not [string]::IsNullOrWhiteSpace($bashPath)) {
        Invoke-ExportDocExternal -FilePath $bashPath -Arguments @("-n", $file.FullName) -WorkingDirectory $repoRoot
    }
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
    ShellScriptCount = $shellScripts.Count
    ShellSyntaxValidated = -not [string]::IsNullOrWhiteSpace($bashPath)
    PublicCommandEntryCount = $publicCommandScripts.Count
    ApprovedDirectNativeCommandCount = $directNativeCommands.Count
} | ConvertTo-Json -Depth 4
