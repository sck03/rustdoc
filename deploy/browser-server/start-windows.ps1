[CmdletBinding()]
param(
    [string]$Urls
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Assert-SafeDataRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        [System.IO.Path]::TrimEndingDirectorySeparator($fullPath),
        [System.IO.Path]::TrimEndingDirectorySeparator($volumeRoot),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "数据根不能直接使用磁盘、卷或共享根目录：$fullPath"
    }

    $candidate = $fullPath
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (-not $item.PSIsContainer) {
                throw "数据目录路径不能经过文件：$candidate"
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "数据目录路径不能经过符号链接、联接点或其它重解析点：$candidate"
            }
        }

        if ([string]::Equals(
            [System.IO.Path]::TrimEndingDirectorySeparator($candidate),
            [System.IO.Path]::TrimEndingDirectorySeparator($volumeRoot),
            [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = [System.IO.Directory]::GetParent($candidate)
        if ($null -eq $parent) { break }
        $candidate = $parent.FullName
    }

    return $fullPath
}

function Assert-SafeManagedFilePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "受管配置路径必须是普通文件且不能是符号链接或重解析点：$Path"
    }
}

$runtimeEnvPath = Join-Path $root "App_Data\Security\browser-server.env"
$runtimeEnvPointer = Join-Path $root "browser-server.env.path"
Assert-SafeManagedFilePath $runtimeEnvPointer
if (Test-Path -LiteralPath $runtimeEnvPointer -PathType Leaf) {
    $configuredPath = (Get-Content -LiteralPath $runtimeEnvPointer -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        if ($configuredPath -match '[\x00-\x1F\x7F]') {
            throw "browser-server.env.path 不能包含控制字符。"
        }
        $runtimeEnvPath = if ([System.IO.Path]::IsPathRooted($configuredPath)) {
            [System.IO.Path]::GetFullPath($configuredPath)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $root $configuredPath))
        }
    }
}
Assert-SafeManagedFilePath $runtimeEnvPath
$runtimeEnvDirectory = [System.IO.Path]::GetDirectoryName($runtimeEnvPath)
[void](Assert-SafeDataRoot $runtimeEnvDirectory)
if (Test-Path -LiteralPath $runtimeEnvPath -PathType Leaf) {
    foreach ($line in Get-Content -LiteralPath $runtimeEnvPath) {
        if ($line -match '[\x00-\x1F\x7F]') {
            throw "browser-server.env 不能包含控制字符。"
        }
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) {
            continue
        }
        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }
        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1)
        if ($name -match '^(?:EXPORTDOCMANAGER_|POSTGRES_)[A-Za-z0-9_]*$') {
            [Environment]::SetEnvironmentVariable($name, $value, [EnvironmentVariableTarget]::Process)
        }
    }
}

$dataRootCandidate = if (-not [string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_DATA_ROOT)) {
    if ([System.IO.Path]::IsPathRooted($env:EXPORTDOCMANAGER_DATA_ROOT)) {
        [System.IO.Path]::GetFullPath($env:EXPORTDOCMANAGER_DATA_ROOT)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $root $env:EXPORTDOCMANAGER_DATA_ROOT))
    }
} else {
    Join-Path $root "App_Data"
}
$dataRoot = Assert-SafeDataRoot $dataRootCandidate
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
[void](Assert-SafeDataRoot $dataRoot)

$configRoot = Assert-SafeDataRoot (Join-Path $dataRoot "Config")
$configPath = Join-Path $configRoot "appsettings.json"
Assert-SafeManagedFilePath $configPath
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "appsettings.json was not found: $configPath"
}
if ((Get-Content -LiteralPath $configPath -Raw).Contains("CHANGE_ME_BEFORE_START")) {
    throw "请先运行 initialize-windows.ps1 生成 PostgreSQL 连接配置。"
}
if ([string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_POSTGRES_PASSWORD) -and
    [string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE)) {
    throw "请在受限的 browser-server.env 中设置 EXPORTDOCMANAGER_POSTGRES_PASSWORD 或 EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE。"
}
if ([string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_BOOTSTRAP_TOKEN) -or $env:EXPORTDOCMANAGER_BOOTSTRAP_TOKEN.Length -lt 24) {
    throw "请先设置至少 24 个字符的 EXPORTDOCMANAGER_BOOTSTRAP_TOKEN，用于首次 PostgreSQL 管理员初始化。"
}

$browser = Get-ChildItem -LiteralPath (Join-Path $root "Browsers") -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("chrome-headless-shell.exe", "chrome.exe") } |
    Select-Object -First 1
if ($null -eq $browser) {
    throw "内置 Chrome Headless Shell 不存在。"
}

$effectiveUrls = if ($PSBoundParameters.ContainsKey('Urls')) {
    $Urls
} elseif (-not [string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_URLS)) {
    $env:EXPORTDOCMANAGER_URLS
} else {
    "http://0.0.0.0:5188"
}
if ([string]::IsNullOrWhiteSpace($effectiveUrls) -or $effectiveUrls -match '[\x00-\x1F\x7F]') {
    throw "监听地址不能为空或包含控制字符。"
}

$env:EXPORTDOCMANAGER_NETWORK_MODE = "true"
$env:EXPORTDOCMANAGER_PRODUCT_EDITION = "Full"
$env:EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE = $browser.FullName
$postgresBin = Join-Path $root "Tools\PostgreSQL\bin"
if (Test-Path -LiteralPath (Join-Path $postgresBin "pg_dump.exe") -PathType Leaf) {
    $env:EXPORTDOCMANAGER_POSTGRES_BIN = $postgresBin
}
& (Join-Path $root "ExportDocManager.Api.exe") `
    --app-root $root `
    --data-root $dataRoot `
    --urls $effectiveUrls `
    --network-mode true
exit $LASTEXITCODE
