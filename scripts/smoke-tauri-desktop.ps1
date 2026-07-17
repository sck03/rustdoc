[CmdletBinding()]
param(
    [string]$CargoTargetDir,

    [string]$AppRoot,

    [string]$DataRoot,

    [string]$Username = "admin",

    [string]$Password = "",

    [int]$TimeoutSeconds = 60,

    [switch]$VerifyWebDiagnostics,

    [switch]$VerifyBackupRestore,

    [switch]$VerifyWebReports,

    [switch]$VerifyInvoiceItems,

    [switch]$VerifyContainerPacking,

    [switch]$VerifySalesWorkspace,

    [string]$BrowserExecutablePath,

    [int]$WebSmokeTimeoutSeconds = 45,

    [switch]$UseRuntimePathsConfig,

    [switch]$UseExistingRuntimePathsConfig,

    [switch]$UseDefaultAppRoot,

    [switch]$SkipVite,

    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ConvertFrom-WindowsExtendedPath -Path ([System.IO.Path]::GetFullPath((ConvertFrom-WindowsExtendedPath -Path $Path)))
}

function ConvertFrom-WindowsExtendedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($Path.StartsWith("\\?\UNC\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "\\" + $Path.Substring(8)
    }

    if ($Path.StartsWith("\\?\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring(4)
    }

    return $Path
}

function Test-SystemDrivePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($env:SystemDrive)) {
        return $false
    }

    return (Get-FullPath -Path $Path).StartsWith($env:SystemDrive, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NonSystemDrivePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (Test-SystemDrivePath -Path $Path) {
        throw "$Purpose resolved to the system drive: $Path"
    }
}

function Assert-SamePath {
    param(
        [Parameter(Mandatory = $true)][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $actualFullPath = Get-FullPath -Path $Actual
    $expectedFullPath = Get-FullPath -Path $Expected
    if (-not [string]::Equals($actualFullPath, $expectedFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose mismatch. Expected '$expectedFullPath', got '$actualFullPath'."
    }
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    $fullPath = Get-FullPath -Path $Path
    $fullRoot = Get-FullPath -Path $Root
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullRoot = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must be under '$fullRoot', got '$fullPath'."
    }
}

function Join-LayoutRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "$Purpose has an empty relative path."
    }

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Purpose must be relative, got '$RelativePath'."
    }

    $segments = $RelativePath -split '[\\/]'
    if ($segments -contains '..') {
        throw "$Purpose must not escape its root, got '$RelativePath'."
    }

    return Get-FullPath -Path (Join-Path $Root $RelativePath)
}

function Assert-RuntimeLayoutManifest {
    param(
        [Parameter(Mandatory = $true)][string]$AppRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot,
        [Parameter(Mandatory = $true)][object]$Health
    )

    $manifestPath = Join-Path $AppRoot "runtime-layout.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Tauri runtime layout manifest was not found: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Unsupported Tauri runtime layout manifest schema: $($manifest.schemaVersion)"
    }

    foreach ($resource in @($manifest.programRootResources)) {
        if ($resource.storage -ne "program-root") {
            throw "Runtime layout resource '$($resource.name)' must use program-root storage."
        }

        $resourcePath = Join-LayoutRelativePath `
            -Root $AppRoot `
            -RelativePath ([string]$resource.relativePath) `
            -Purpose "Runtime layout resource '$($resource.name)'"
        Assert-PathUnderRoot -Path $resourcePath -Root $AppRoot -Purpose "Runtime layout resource '$($resource.name)'"

        if ($resource.required) {
            $pathType = if ($resource.kind -eq "file") { "Leaf" } else { "Container" }
            if (-not (Test-Path -LiteralPath $resourcePath -PathType $pathType)) {
                throw "Required runtime layout resource '$($resource.name)' was not found at '$resourcePath'."
            }
        }
    }

    foreach ($directory in @($manifest.runtimeDataDirectories)) {
        if ($directory.storage -ne "runtime-data-root") {
            throw "Runtime data directory '$($directory.name)' must use runtime-data-root storage."
        }

        $directoryPath = Join-LayoutRelativePath `
            -Root $DataRoot `
            -RelativePath ([string]$directory.relativePath) `
            -Purpose "Runtime data directory '$($directory.name)'"
        Assert-PathUnderRoot -Path $directoryPath -Root $DataRoot -Purpose "Runtime data directory '$($directory.name)'"

        if ($directory.required -and -not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
            throw "Required runtime data directory '$($directory.name)' was not created at '$directoryPath'."
        }
    }

    Assert-SamePath -Actual $Health.templateRoot -Expected (Join-Path $AppRoot "Templates") -Purpose "Health template root"
    Assert-SamePath -Actual $Health.ocrModelRoot -Expected (Join-Path $AppRoot "OcrModels") -Purpose "Health OCR model root"
    Assert-SamePath -Actual $Health.logRoot -Expected (Join-Path $DataRoot "Logs") -Purpose "Health log root"
    Assert-SamePath -Actual $Health.singleWindowRoot -Expected (Join-Path $DataRoot "SingleWindow") -Purpose "Health Single Window root"

    return [pscustomobject]@{
        ManifestPath = $manifestPath
        SchemaVersion = $manifest.schemaVersion
        RuntimeIdentifier = $manifest.runtimeIdentifier
        ProgramRootResourceCount = @($manifest.programRootResources).Count
        RuntimeDataDirectoryCount = @($manifest.runtimeDataDirectories).Count
    }
}

function Wait-TcpPort {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][datetime]$Deadline
    )

    while ((Get-Date) -lt $Deadline) {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync("127.0.0.1", $Port)
            if ($task.Wait(500) -and $client.Connected) {
                return
            }
        } catch {
        } finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for 127.0.0.1:$Port."
}

function Start-LoggedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [string]$Arguments = "",
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath,
        [hashtable]$Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    if ($ArgumentList.Count -gt 0) {
        foreach ($argument in $ArgumentList) {
            $startInfo.ArgumentList.Add($argument)
        }
    } else {
        $startInfo.Arguments = $Arguments
    }
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    foreach ($key in $Environment.Keys) {
        $startInfo.Environment[$key] = [string]$Environment[$key]
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    $started = $process.Start()
    if (-not $started) {
        throw "Failed to start $FileName $Arguments"
    }

    $displayArguments = if ($ArgumentList.Count -gt 0) { $ArgumentList -join " " } else { $Arguments }
    Add-Content -LiteralPath $StdoutPath -Value "Started $FileName $displayArguments at $(Get-Date -Format o)"
    Add-Content -LiteralPath $StderrPath -Value "Started $FileName $displayArguments at $(Get-Date -Format o)"

    return $process
}

function Stop-StartedProcess {
    param([AllowNull()][System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        if ($Process.CloseMainWindow()) {
            if ($Process.WaitForExit(5000)) {
                return
            }
        }
    } catch {
    }

    try {
        $null = $Process.Kill($true)
        $null = $Process.WaitForExit(5000)
    } catch {
    }
}

function Invoke-WebProductionBuild {
    Write-Host "Building web app for Tauri smoke preview."
    Push-Location -LiteralPath $repoRoot
    try {
        & npm --prefix apps/export-doc-web run build
        if ($LASTEXITCODE -ne 0) {
            throw "Web production build failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

function Get-SmokeViteProcesses {
    $normalizedRepoRoot = (Get-FullPath -Path $repoRoot).Replace('/', '\')
    return Get-CimInstance Win32_Process |
        Where-Object {
            $commandLine = if ($null -ne $_.CommandLine) { $_.CommandLine.Replace('/', '\') } else { '' }
            $_.Name -eq "node.exe" -and
            $commandLine -like "*vite*" -and
            $commandLine -like "*5173*" -and
            $commandLine -like "*$normalizedRepoRoot*"
        }
}

function Stop-SmokeViteProcesses {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Get-SmokeViteProcesses | ForEach-Object {
        try {
            Write-Host "Stopping stale Vite process $($_.ProcessId) for Tauri smoke ($Reason)."
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        } catch {
        }
    }
}

function Stop-ApiSidecarsForDataRoot {
    param([Parameter(Mandatory = $true)][string]$DataRoot)

    $escapedDataRoot = [Regex]::Escape((Get-FullPath -Path $DataRoot).Replace('/', '\'))
    Get-CimInstance Win32_Process |
        Where-Object {
            $normalizedCommandLine = if ($null -ne $_.CommandLine) { $_.CommandLine.Replace('/', '\') } else { '' }
            $_.Name -eq "ExportDocManager.Api.exe" -and
            $normalizedCommandLine -match $escapedDataRoot
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

function Assert-SmokeVitePortAvailable {
    Stop-SmokeViteProcesses -Reason "pre-start port cleanup"
    Start-Sleep -Milliseconds 300

    $listeners = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue
    if ($listeners) {
        $owners = ($listeners | Select-Object -ExpandProperty OwningProcess -Unique) -join ", "
        throw "127.0.0.1:5173 is already in use by process id(s): $owners"
    }
}

function Read-LatestSidecarUrl {
    param([Parameter(Mandatory = $true)][string]$LogPath)

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return $null
    }

    $matches = Select-String -LiteralPath $LogPath -Pattern "Starting API sidecar at (http://127\.0\.0\.1:\d+)" -AllMatches
    $last = $matches | Select-Object -Last 1
    if ($null -eq $last) {
        return $null
    }

    return $last.Matches[$last.Matches.Count - 1].Groups[1].Value
}

function Wait-SidecarHealth {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][datetime]$Deadline,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken
    )

    $lastUrl = $null
    while ((Get-Date) -lt $Deadline) {
        $lastUrl = Read-LatestSidecarUrl -LogPath $LogPath
        if (-not [string]::IsNullOrWhiteSpace($lastUrl)) {
            try {
                return [pscustomobject]@{
                    BaseUrl = $lastUrl
                    Health = Invoke-RestMethod `
                        -Uri "$lastUrl/healthz" `
                        -Headers @{ "X-ExportDocManager-Desktop-Token" = $DesktopAccessToken } `
                        -TimeoutSec 2
                }
            } catch {
            }
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Timed out waiting for API sidecar health. Last URL: $lastUrl"
}

function Resolve-BrowserExecutablePath {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $resolvedPath = Get-FullPath -Path $Path
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
            throw "Browser executable was not found: $resolvedPath"
        }

        return $resolvedPath
    }

    $browserRoot = Join-Path $repoRoot "Browsers\ChromeForTesting"
    if (-not (Test-Path -LiteralPath $browserRoot)) {
        throw "Chrome for Testing was not found under '$browserRoot'. Run scripts\provision-chrome-for-testing.ps1 -Product ChromeHeadlessShell first."
    }

    $manifest = Get-ChildItem -LiteralPath $browserRoot -Recurse -Filter "chrome-for-testing.manifest.json" -File |
        ForEach-Object {
            try {
                $entry = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                $entry | Add-Member -NotePropertyName ManifestPath -NotePropertyValue $_.FullName -Force
                $entry
            } catch {
                $null
            }
        } |
        Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.executablePath) } |
        Sort-Object @{ Expression = { if ([string]$_.product -eq "ChromeHeadlessShell") { 0 } else { 1 } } }, "executablePath" |
        Select-Object -First 1

    if ($null -eq $manifest) {
        throw "Chrome for Testing manifest did not contain an executablePath under '$browserRoot'. Run scripts\provision-chrome-for-testing.ps1 -Product ChromeHeadlessShell first."
    }

    $resolvedPath = Get-FullPath -Path ([string]$manifest.executablePath)
    if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
        return $resolvedPath
    }

    $manifestDirectory = Split-Path -Parent ([string]$manifest.ManifestPath)
    $fallbackExecutable = Get-ChildItem -LiteralPath $manifestDirectory -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome.exe") } |
        Sort-Object @{ Expression = { if ($_.Name -eq "chrome-headless-shell.exe") { 0 } else { 1 } } }, "FullName" |
        Select-Object -First 1

    if ($null -eq $fallbackExecutable) {
        $fallbackExecutable = Get-ChildItem -LiteralPath $browserRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome.exe") } |
            Sort-Object @{ Expression = { if ($_.Name -eq "chrome-headless-shell.exe") { 0 } else { 1 } } }, "FullName" |
            Select-Object -First 1
    }

    if ($null -ne $fallbackExecutable) {
        return $fallbackExecutable.FullName
    }

    throw "Chrome for Testing executable from manifest was not found: $resolvedPath"
}

function Invoke-WebDiagnosticsSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][object]$Health,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web runtime diagnostics smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Web smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Web smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $webSmokeJsonPath = Join-Path $LogRoot "web-runtime-diagnostics-smoke.json"
    $webSmokeScreenshotPath = Join-Path $LogRoot "web-runtime-diagnostics-smoke.png"
    $webUrl = "http://127.0.0.1:5173/#/settings?section=diagnostics"

    $expectedText = @(
        "账号与权限",
        "团队库",
        "数据归属",
        "运行检查",
        "问题诊断",
        "运行诊断",
        "API 状态",
        "ok",
        [string]$Health.appRoot,
        [string]$Health.dataRoot,
        [string]$Health.databaseRoot,
        [string]$Health.sqliteDatabasePath
    )

    $expectedOpenPaths = @(
        [string]$Health.appRoot,
        [string]$Health.dataRoot,
        [string]$Health.databaseRoot,
        [string]$Health.sqliteDatabasePath,
        [string]$Health.logRoot,
        [string]$Health.templateRoot,
        [string]$Health.ocrModelRoot,
        [string]$Health.singleWindowRoot
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $nodeArguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", $webUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--runtime-path-actions-check",
        "--backup-create-check",
        "--audit-log-check",
        "--audit-log-export-check",
        "--user-management-crud-check",
        "--screenshot-path", $webSmokeScreenshotPath
    )) {
        $nodeArguments.Add([string]$argument)
    }

    foreach ($value in $expectedText) {
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $nodeArguments.Add("--expected-text")
            $nodeArguments.Add([string]$value)
        }
    }
    foreach ($value in $expectedOpenPaths) {
        $nodeArguments.Add("--expected-open-path")
        $nodeArguments.Add([string]$value)
    }
    $nodeArguments.Add("--expected-user-row")
    $nodeArguments.Add("admin")
    $nodeArguments.Add("Admin")

    $nodeArgumentsArray = $nodeArguments.ToArray()
    $nodeOutput = & node @nodeArgumentsArray
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web runtime diagnostics smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $webSmokeJsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

function Invoke-WebSalesWorkspaceSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web runtime diagnostics smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserSalesWorkspaceSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Sales workspace smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Sales workspace smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $jsonPath = Join-Path $LogRoot "web-sales-workspace-smoke.json"
    $screenshotPath = Join-Path $LogRoot "web-sales-workspace-smoke.png"
    $nodeArguments = @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", "http://127.0.0.1:5173/#/crm/dashboard",
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--expected-text", "销售概览",
        "--sales-workspace-check",
        "--screenshot-path", $screenshotPath
    )

    $nodeOutput = & node @nodeArguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web sales workspace smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $jsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

function Invoke-WebBackupRestoreSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web runtime diagnostics smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserBackupRestoreSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Web backup restore smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Web backup restore smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $webSmokeJsonPath = Join-Path $LogRoot "web-backup-restore-smoke.json"
    $webSmokeScreenshotPath = Join-Path $LogRoot "web-backup-restore-smoke.png"
    $webUrl = "http://127.0.0.1:5173/#/settings"
    $expectedText = @(
        "设置",
        "数据备份与还原",
        "备份目录",
        "创建备份",
        "还原数据库"
    )

    $nodeArguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", $webUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--backup-restore-check",
        "--screenshot-path", $webSmokeScreenshotPath
    )) {
        $nodeArguments.Add([string]$argument)
    }

    foreach ($value in $expectedText) {
        $nodeArguments.Add("--expected-text")
        $nodeArguments.Add([string]$value)
    }

    $nodeArgumentsArray = $nodeArguments.ToArray()
    $nodeOutput = & node @nodeArgumentsArray
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web backup restore smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $webSmokeJsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

function Invoke-WebReportsSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserReportsSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Web reports smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Web reports smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $webSmokeJsonPath = Join-Path $LogRoot "web-report-templates-smoke.json"
    $webSmokeScreenshotPath = Join-Path $LogRoot "web-report-templates-smoke.png"
    $webUrl = "http://127.0.0.1:5173/#/reports/templates"

    $expectedText = @(
        "报表设计",
        "新版设计器",
        "源码",
        "预览",
        "保存",
        "设计",
        "类型",
        "模板",
        "模板操作",
        "模板包",
        "导入 / 导出",
        "组件库",
        "字段目录",
        "组件属性"
    )
    $reportTemplateChecks = @(
        @("ExportDocument", "invoice_template.html", "Exporter.ExporterNameEN"),
        @("ExportDocument", "packing_list_template.html", "PACKING LIST"),
        @("ExportDocument", "contract_template.html", "Invoice.ShipmentDate"),
        @("ExportDocument", "customs_declaration_template.html", "中华人民共和国海关出口货物报关单"),
        @("PaymentVoucher", "payment_voucher_template.html", "Payment.Project"),
        @("PaymentVoucher", "expense_reimbursement_template.html", "费用报销明细单")
    )

    $nodeArguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", $webUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--screenshot-path", $webSmokeScreenshotPath
    )) {
        $nodeArguments.Add([string]$argument)
    }

    foreach ($value in $expectedText) {
        $nodeArguments.Add("--expected-text")
        $nodeArguments.Add([string]$value)
    }
    foreach ($check in $reportTemplateChecks) {
        $nodeArguments.Add("--report-template-check")
        $nodeArguments.Add([string]$check[0])
        $nodeArguments.Add([string]$check[1])
        $nodeArguments.Add([string]$check[2])
    }
    $nodeArguments.Add("--invoice-report-check")
    $nodeArguments.Add("--invoice-letter-of-credit-check")
    $nodeArguments.Add("--invoice-delete-check")
    $nodeArguments.Add("--invoice-list-desktop-workflow-check")
    $nodeArguments.Add("--query-keyboard-check")
    $nodeArguments.Add("--single-window-editor-tools-check")
    $nodeArguments.Add("--single-window-operation-center-check")
    $nodeArguments.Add("--payment-report-check")
    $nodeArguments.Add("--payment-delete-check")
    $nodeArguments.Add("--master-data-delete-check")
    $nodeArguments.Add("--job-center-check")
    $nodeArguments.Add("--dashboard-check")
    $nodeArguments.Add("--backup-check")
    $nodeArguments.Add("--backup-create-check")
    $nodeArguments.Add("--update-check")
    $nodeArguments.Add("--update-stage-check")
    $nodeArguments.Add("--update-mandatory-check")
    $nodeArguments.Add("--smart-ocr-check")
    $nodeArguments.Add("--smart-ocr-real-sample-check")
    $nodeArguments.Add("--exchange-rate-check")
    $nodeArguments.Add("--email-check")
    $nodeArguments.Add("--audit-log-check")
    $nodeArguments.Add("--audit-log-export-check")
    $nodeArguments.Add("--license-check")

    $nodeArgumentsArray = $nodeArguments.ToArray()
    $nodeOutput = & node @nodeArgumentsArray
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web report template smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $webSmokeJsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

function Invoke-WebInvoiceItemsSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserInvoiceItemsSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Web invoice items smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Web invoice items smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $webSmokeJsonPath = Join-Path $LogRoot "web-invoice-items-smoke.json"
    $webSmokeScreenshotPath = Join-Path $LogRoot "web-invoice-items-smoke.png"
    $webUrl = "http://127.0.0.1:5173/#/invoices"
    $nodeArguments = @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", $webUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--expected-text", "发票管理",
        "--invoice-items-check",
        "--screenshot-path", $webSmokeScreenshotPath
    )

    $nodeOutput = & node @nodeArguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web invoice items smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $webSmokeJsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

function Invoke-WebContainerPackingSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][string]$DataRoot
    )

    $browserExecutable = Resolve-BrowserExecutablePath -Path $BrowserExecutablePath
    Assert-NonSystemDrivePath -Path $browserExecutable -Purpose "Chrome for Testing executable"

    $scriptPath = Join-Path $scriptRoot "smoke-web-runtime-diagnostics.mjs"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Web smoke script was not found: $scriptPath"
    }

    $browserProfileRoot = Join-Path $DataRoot ("BrowserContainerPackingSmokeProfile-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID)
    Assert-PathUnderRoot -Path $browserProfileRoot -Root $DataRoot -Purpose "Web container packing smoke browser profile"
    Assert-NonSystemDrivePath -Path $browserProfileRoot -Purpose "Web container packing smoke browser profile"
    New-Item -ItemType Directory -Path $browserProfileRoot -Force | Out-Null

    $webSmokeJsonPath = Join-Path $LogRoot "web-container-packing-smoke.json"
    $webSmokeScreenshotPath = Join-Path $LogRoot "web-container-packing-smoke.png"
    $webUrl = "http://127.0.0.1:5173/#/jobs"

    $nodeArguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @(
        $scriptPath,
        "--browser-executable", $browserExecutable,
        "--web-url", $webUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username,
        "--password", $Password,
        "--user-data-dir", $browserProfileRoot,
        "--timeout-ms", ([string]($WebSmokeTimeoutSeconds * 1000)),
        "--expected-text", "任务中心",
        "--job-center-check",
        "--screenshot-path", $webSmokeScreenshotPath
    )) {
        $nodeArguments.Add([string]$argument)
    }

    $nodeArgumentsArray = $nodeArguments.ToArray()
    $nodeOutput = & node @nodeArgumentsArray
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Web container packing smoke failed with exit code $exitCode."
    }

    $nodeOutputText = $nodeOutput -join [Environment]::NewLine
    Set-Content -LiteralPath $webSmokeJsonPath -Value $nodeOutputText -Encoding UTF8
    return $nodeOutputText | ConvertFrom-Json
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path

if ($UseRuntimePathsConfig -and $UseExistingRuntimePathsConfig) {
    throw "Use either -UseRuntimePathsConfig or -UseExistingRuntimePathsConfig, not both."
}

if (($VerifyWebDiagnostics -or $VerifyWebReports -or $VerifyInvoiceItems -or $VerifyBackupRestore -or $VerifyContainerPacking -or $VerifySalesWorkspace) -and $SkipVite) {
    throw "Web smoke verification requires Vite; do not combine web verification switches with -SkipVite."
}

if ([string]::IsNullOrWhiteSpace($CargoTargetDir)) {
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
        $CargoTargetDir = $env:CARGO_TARGET_DIR
    } else {
        $CargoTargetDir = Join-Path $repoRoot "artifacts\cargo-target-exportdoc"
    }
}

$resolvedCargoTargetDir = Get-FullPath -Path $CargoTargetDir
if ([string]::IsNullOrWhiteSpace($AppRoot)) {
    $AppRoot = Join-Path $resolvedCargoTargetDir "debug"
}

$resolvedAppRoot = Get-FullPath -Path $AppRoot
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = if ($VerifySalesWorkspace) {
        Join-Path $resolvedAppRoot ("App_Data\SalesWorkspaceSmoke-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
    } else {
        Join-Path $resolvedAppRoot "App_Data\Smoke"
    }
}

$resolvedDataRoot = Get-FullPath -Path $DataRoot
Assert-NonSystemDrivePath -Path $resolvedAppRoot -Purpose "Tauri smoke app root"
Assert-NonSystemDrivePath -Path $resolvedDataRoot -Purpose "Tauri smoke data root"

$tauriExe = Join-Path $resolvedAppRoot "export-doc-tauri.exe"
if (-not (Test-Path -LiteralPath $tauriExe)) {
    throw "Tauri debug executable was not found: $tauriExe. Run npm --prefix apps/export-doc-tauri run tauri:compile:local first."
}

$logRoot = Join-Path $resolvedDataRoot "Logs"
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedDataRoot -Force | Out-Null

$viteStdout = Join-Path $logRoot "tauri-smoke-vite.stdout.log"
$viteStderr = Join-Path $logRoot "tauri-smoke-vite.stderr.log"
$tauriStdout = Join-Path $logRoot "tauri-smoke-shell.stdout.log"
$tauriStderr = Join-Path $logRoot "tauri-smoke-shell.stderr.log"
$sidecarStdout = Join-Path $logRoot "api-sidecar.stdout.log"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$vite = $null
$tauri = $null
$desktopAccessToken = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$runtimePathsConfigPath = Join-Path $resolvedAppRoot "runtime-paths.json"
$runtimePathsConfigBackupPath = Join-Path $logRoot ("runtime-paths.json.smoke-backup-{0}" -f $PID)
$runtimePathsConfigExisted = $false
$usesRuntimePathsConfig = $UseRuntimePathsConfig -or $UseExistingRuntimePathsConfig

try {
    if ($UseRuntimePathsConfig) {
        if (Test-Path -LiteralPath $runtimePathsConfigBackupPath -PathType Leaf) {
            Remove-Item -LiteralPath $runtimePathsConfigBackupPath -Force
        }

        if (Test-Path -LiteralPath $runtimePathsConfigPath -PathType Leaf) {
            Move-Item -LiteralPath $runtimePathsConfigPath -Destination $runtimePathsConfigBackupPath -Force
            $runtimePathsConfigExisted = $true
        }

        [ordered]@{
            schemaVersion = 1
            dataRoot = $resolvedDataRoot
            source = "tauri-smoke-runtime-config"
        } |
            ConvertTo-Json -Depth 3 |
            Set-Content -LiteralPath $runtimePathsConfigPath -Encoding UTF8
    }

    if ($UseExistingRuntimePathsConfig -and -not (Test-Path -LiteralPath $runtimePathsConfigPath -PathType Leaf)) {
        throw "Existing runtime paths config was not found: $runtimePathsConfigPath"
    }

    if (-not $SkipVite) {
        Assert-SmokeVitePortAvailable
        Invoke-WebProductionBuild
        $vite = Start-LoggedProcess `
            -FileName "cmd.exe" `
            -Arguments "/c npm --prefix apps/export-doc-web run preview -- --port 5173 --strictPort" `
            -WorkingDirectory $repoRoot `
            -StdoutPath $viteStdout `
            -StderrPath $viteStderr
        Wait-TcpPort -Port 5173 -Deadline $deadline
    }

    $tauriArgumentList = @()
    $tauriEnvironment = @{
        EXPORTDOCMANAGER_DESKTOP_TOKEN = $desktopAccessToken
    }
    if (-not $UseDefaultAppRoot) {
        $tauriArgumentList += @("--app-root", $resolvedAppRoot)
        $tauriEnvironment["EXPORTDOCMANAGER_APP_ROOT"] = $resolvedAppRoot
    }
    if (-not $usesRuntimePathsConfig) {
        $tauriArgumentList += @("--data-root", $resolvedDataRoot)
        $tauriEnvironment["EXPORTDOCMANAGER_DATA_ROOT"] = $resolvedDataRoot
    }

    $tauri = Start-LoggedProcess `
        -FileName $tauriExe `
        -ArgumentList $tauriArgumentList `
        -WorkingDirectory $resolvedAppRoot `
        -StdoutPath $tauriStdout `
        -StderrPath $tauriStderr `
        -Environment $tauriEnvironment

    $sidecar = Wait-SidecarHealth -LogPath $sidecarStdout -Deadline $deadline -DesktopAccessToken $desktopAccessToken
    $apiBaseUrl = $sidecar.BaseUrl
    $health = $sidecar.Health

    if ($health.status -ne "ok") {
        throw "Unexpected health status: $($health.status)"
    }

    Assert-SamePath -Actual $health.appRoot -Expected $resolvedAppRoot -Purpose "Health app root"
    Assert-SamePath -Actual $health.dataRoot -Expected $resolvedDataRoot -Purpose "Health data root"
    Assert-SamePath -Actual $health.databaseRoot -Expected (Join-Path $resolvedDataRoot "Database") -Purpose "Health database root"
    if ([string]::IsNullOrWhiteSpace($health.sqliteDatabasePath)) {
        throw "Health check did not return a SQLite database path."
    }
    Assert-PathUnderRoot -Path $health.sqliteDatabasePath -Root $health.databaseRoot -Purpose "SQLite database path"
    $runtimeLayout = Assert-RuntimeLayoutManifest -AppRoot $resolvedAppRoot -DataRoot $resolvedDataRoot -Health $health

    foreach ($pathCheck in @(
        @{ Purpose = "Health app root"; Path = $health.appRoot },
        @{ Purpose = "Health data root"; Path = $health.dataRoot },
        @{ Purpose = "Health database root"; Path = $health.databaseRoot },
        @{ Purpose = "Health SQLite database path"; Path = $health.sqliteDatabasePath },
        @{ Purpose = "Health template root"; Path = $health.templateRoot },
        @{ Purpose = "Health OCR model root"; Path = $health.ocrModelRoot },
        @{ Purpose = "Health log root"; Path = $health.logRoot },
        @{ Purpose = "Health Single Window root"; Path = $health.singleWindowRoot }
    )) {
        Assert-NonSystemDrivePath -Path $pathCheck.Path -Purpose $pathCheck.Purpose
    }

    $loginBody = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json -Compress
    $desktopHeaders = @{
        "X-ExportDocManager-Desktop-Token" = $desktopAccessToken
    }
    $login = Invoke-RestMethod `
        -Uri "$apiBaseUrl/api/auth/login" `
        -Method Post `
        -Headers $desktopHeaders `
        -ContentType "application/json" `
        -Body $loginBody `
        -TimeoutSec 10
    if ([string]::IsNullOrWhiteSpace($login.accessToken)) {
        throw "Login did not return an access token."
    }

    $headers = @{
        "X-ExportDocManager-Desktop-Token" = $desktopAccessToken
        Authorization = "$($login.tokenType) $($login.accessToken)"
    }
    $currentUser = Invoke-RestMethod `
        -Uri "$apiBaseUrl/api/auth/me" `
        -Headers $headers `
        -TimeoutSec 10
    if ($currentUser.username -ne $Username) {
        throw "Unexpected current user: $($currentUser.username)"
    }

    $invoices = Invoke-RestMethod `
        -Uri "$apiBaseUrl/api/invoices?pageNumber=1&pageSize=5" `
        -Headers $headers `
        -TimeoutSec 10
    if ($null -eq $invoices.items -or $null -eq $invoices.totalCount) {
        throw "Invoice list response did not include paging fields."
    }

    if (-not (Test-Path -LiteralPath $health.sqliteDatabasePath -PathType Leaf)) {
        throw "SQLite database file was not created under the Tauri smoke data root: $($health.sqliteDatabasePath)"
    }
    $sqliteDatabase = Get-Item -LiteralPath $health.sqliteDatabasePath
    $webDiagnostics = $null
    if ($VerifyWebDiagnostics) {
        $webDiagnostics = Invoke-WebDiagnosticsSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -Health $health `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot
    }
    $webReports = $null
    if ($VerifyWebReports) {
        $webReports = Invoke-WebReportsSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot
    }
    $webInvoiceItems = $null
    if ($VerifyInvoiceItems) {
        $webInvoiceItems = Invoke-WebInvoiceItemsSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot
    }
    $webContainerPacking = $null
    if ($VerifyContainerPacking) {
        $webContainerPacking = Invoke-WebContainerPackingSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot
    }
    $webSalesWorkspace = $null
    if ($VerifySalesWorkspace) {
        $webSalesWorkspace = Invoke-WebSalesWorkspaceSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot
    }
    $webBackupRestore = $null
    $backupRestoreVerification = $null
    if ($VerifyBackupRestore) {
        $webBackupRestore = Invoke-WebBackupRestoreSmoke `
            -ApiBaseUrl $apiBaseUrl `
            -DesktopAccessToken $desktopAccessToken `
            -LogRoot $logRoot `
            -DataRoot $resolvedDataRoot

        $backupRestoreCheck = $webBackupRestore.backupRestoreCheck
        if ($null -eq $backupRestoreCheck) {
            throw "Backup restore smoke did not return backupRestoreCheck."
        }
        if ($backupRestoreCheck.createdUserBeforeRestore -ne $true) {
            throw "Backup restore smoke did not prove the transient user existed before restore."
        }
        if ($backupRestoreCheck.restoreRequiresRestart -ne $true) {
            throw "Backup restore smoke did not report restart guidance after restore."
        }

        $transientUsername = [string]$backupRestoreCheck.transientUser.username
        $backupFileName = [string]$backupRestoreCheck.backupFile.fileName
        $backupFullPath = [string]$backupRestoreCheck.backupFile.fullPath
        $backupRoot = [string]$backupRestoreCheck.backupFile.backupRoot
        $expectedBackupRoot = Join-Path $resolvedDataRoot "Backups"
        if ([string]::IsNullOrWhiteSpace($transientUsername) -or [string]::IsNullOrWhiteSpace($backupFileName) -or [string]::IsNullOrWhiteSpace($backupFullPath)) {
            throw "Backup restore smoke returned incomplete transient user or backup file details."
        }
        Assert-SamePath -Actual $backupRoot -Expected $expectedBackupRoot -Purpose "Backup restore smoke backup root"
        Assert-PathUnderRoot -Path $backupFullPath -Root $expectedBackupRoot -Purpose "Backup restore smoke backup file"
        Assert-NonSystemDrivePath -Path $backupFullPath -Purpose "Backup restore smoke backup file"
        if (-not (Test-Path -LiteralPath $backupFullPath -PathType Leaf)) {
            throw "Backup restore smoke backup file was not found before restart: $backupFullPath"
        }

        Stop-StartedProcess -Process $tauri
        $tauri = $null
        Start-Sleep -Milliseconds 500
        Stop-ApiSidecarsForDataRoot -DataRoot $resolvedDataRoot

        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $tauri = Start-LoggedProcess `
            -FileName $tauriExe `
            -ArgumentList $tauriArgumentList `
            -WorkingDirectory $resolvedAppRoot `
            -StdoutPath $tauriStdout `
            -StderrPath $tauriStderr `
            -Environment $tauriEnvironment

        $sidecar = Wait-SidecarHealth -LogPath $sidecarStdout -Deadline $deadline -DesktopAccessToken $desktopAccessToken
        $apiBaseUrl = $sidecar.BaseUrl
        $health = $sidecar.Health
        if ($health.status -ne "ok") {
            throw "Unexpected health status after backup restore restart: $($health.status)"
        }
        Assert-SamePath -Actual $health.appRoot -Expected $resolvedAppRoot -Purpose "Restarted health app root"
        Assert-SamePath -Actual $health.dataRoot -Expected $resolvedDataRoot -Purpose "Restarted health data root"
        Assert-SamePath -Actual $health.databaseRoot -Expected (Join-Path $resolvedDataRoot "Database") -Purpose "Restarted health database root"
        Assert-PathUnderRoot -Path $health.sqliteDatabasePath -Root $health.databaseRoot -Purpose "Restarted SQLite database path"
        $runtimeLayout = Assert-RuntimeLayoutManifest -AppRoot $resolvedAppRoot -DataRoot $resolvedDataRoot -Health $health

        $login = Invoke-RestMethod `
            -Uri "$apiBaseUrl/api/auth/login" `
            -Method Post `
            -Headers $desktopHeaders `
            -ContentType "application/json" `
            -Body $loginBody `
            -TimeoutSec 10
        if ([string]::IsNullOrWhiteSpace($login.accessToken)) {
            throw "Restarted login did not return an access token after backup restore."
        }

        $headers = @{
            "X-ExportDocManager-Desktop-Token" = $desktopAccessToken
            Authorization = "$($login.tokenType) $($login.accessToken)"
        }
        $currentUser = Invoke-RestMethod `
            -Uri "$apiBaseUrl/api/auth/me" `
            -Headers $headers `
            -TimeoutSec 10
        if ($currentUser.username -ne $Username) {
            throw "Unexpected current user after backup restore restart: $($currentUser.username)"
        }

        $usersAfterRestore = Invoke-RestMethod `
            -Uri "$apiBaseUrl/api/users" `
            -Headers $headers `
            -TimeoutSec 10
        if (-not ($usersAfterRestore.users | Where-Object { $_.username -eq "admin" })) {
            throw "Admin user was not readable after backup restore restart."
        }
        if ($usersAfterRestore.users | Where-Object { $_.username -eq $transientUsername }) {
            throw "Transient user '$transientUsername' still exists after backup restore restart."
        }

        $backupsAfterRestore = Invoke-RestMethod `
            -Uri "$apiBaseUrl/api/backup" `
            -Headers $headers `
            -TimeoutSec 10
        if (-not ($backupsAfterRestore.backups | Where-Object { $_.fileName -eq $backupFileName })) {
            throw "Backup file '$backupFileName' was not visible after backup restore restart."
        }

        $cleanedBackupFile = $false
        if (Test-Path -LiteralPath $backupFullPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupFullPath -Force
            $cleanedBackupFile = -not (Test-Path -LiteralPath $backupFullPath -PathType Leaf)
        }

        $invoices = Invoke-RestMethod `
            -Uri "$apiBaseUrl/api/invoices?pageNumber=1&pageSize=5" `
            -Headers $headers `
            -TimeoutSec 10
        if ($null -eq $invoices.items -or $null -eq $invoices.totalCount) {
            throw "Invoice list response after backup restore restart did not include paging fields."
        }

        $sqliteDatabase = Get-Item -LiteralPath $health.sqliteDatabasePath
        $backupRestoreVerification = [pscustomobject]@{
            RestartVerified = $true
            TransientUsername = $transientUsername
            TransientUserRemovedAfterRestart = $true
            BackupFileName = $backupFileName
            BackupFilePath = $backupFullPath
            BackupRoot = $backupRoot
            CleanedBackupFile = $cleanedBackupFile
            RestartedApiBaseUrl = $apiBaseUrl
        }
    }

    $runtimePathsConfigOutput = $null
    if ($usesRuntimePathsConfig) {
        $runtimePathsConfigOutput = $runtimePathsConfigPath
    }

    $viteProcessIdOutput = $null
    if ($null -ne $vite) {
        $viteProcessIdOutput = $vite.Id
    }

    Invoke-RestMethod `
        -Uri "$apiBaseUrl/api/auth/logout" `
        -Method Post `
        -Headers $headers `
        -TimeoutSec 10 | Out-Null

    [pscustomobject]@{
        Success = $true
        ApiStatus = $health.status
        ApiBaseUrl = $apiBaseUrl
        CurrentUser = $currentUser.username
        CurrentUserRole = $currentUser.role
        InvoicePageNumber = $invoices.pageNumber
        InvoiceTotalCount = $invoices.totalCount
        SQLiteDatabasePath = $sqliteDatabase.FullName
        SQLiteDatabaseLength = $sqliteDatabase.Length
        AppRoot = $health.appRoot
        DataRoot = $health.dataRoot
        DatabaseRoot = $health.databaseRoot
        LogRoot = $health.logRoot
        TemplateRoot = $health.templateRoot
        OcrModelRoot = $health.ocrModelRoot
        SingleWindowRoot = $health.singleWindowRoot
        RuntimeLayout = $runtimeLayout
        RuntimePathsConfig = $runtimePathsConfigOutput
        DesktopAccessTokenEnabled = $true
        WebDiagnostics = $webDiagnostics
        WebReports = $webReports
        WebInvoiceItems = $webInvoiceItems
        WebContainerPacking = $webContainerPacking
        WebSalesWorkspace = $webSalesWorkspace
        WebBackupRestore = $webBackupRestore
        BackupRestore = $backupRestoreVerification
        TauriProcessId = $tauri.Id
        ViteProcessId = $viteProcessIdOutput
    } | ConvertTo-Json -Depth 4

    if ($KeepRunning) {
        Write-Host "KeepRunning was set; leaving Tauri and Vite processes running."
        $tauri = $null
        $vite = $null
    }
} finally {
    $cleanupViteChildren = $null -ne $vite
    Stop-StartedProcess -Process $tauri
    Stop-StartedProcess -Process $vite
    if ($cleanupViteChildren) {
        Stop-SmokeViteProcesses -Reason "script shutdown"
    }

    Stop-ApiSidecarsForDataRoot -DataRoot $resolvedDataRoot

    if ($UseRuntimePathsConfig) {
        if (Test-Path -LiteralPath $runtimePathsConfigPath -PathType Leaf) {
            Remove-Item -LiteralPath $runtimePathsConfigPath -Force
        }

        if ($runtimePathsConfigExisted -and (Test-Path -LiteralPath $runtimePathsConfigBackupPath -PathType Leaf)) {
            Move-Item -LiteralPath $runtimePathsConfigBackupPath -Destination $runtimePathsConfigPath -Force
        }
    }
}
