[CmdletBinding()]
param(
    [string]$Urls
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$runtimeEnvPath = Join-Path $root "App_Data\Security\browser-server.env"
$runtimeEnvPointer = Join-Path $root "browser-server.env.path"
if (Test-Path -LiteralPath $runtimeEnvPointer -PathType Leaf) {
    $configuredPath = (Get-Content -LiteralPath $runtimeEnvPointer -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        $runtimeEnvPath = if ([System.IO.Path]::IsPathRooted($configuredPath)) {
            [System.IO.Path]::GetFullPath($configuredPath)
        } else {
            [System.IO.Path]::GetFullPath((Join-Path $root $configuredPath))
        }
    }
}
if (Test-Path -LiteralPath $runtimeEnvPath -PathType Leaf) {
    foreach ($line in Get-Content -LiteralPath $runtimeEnvPath) {
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

$dataRoot = if (-not [string]::IsNullOrWhiteSpace($env:EXPORTDOCMANAGER_DATA_ROOT)) {
    if ([System.IO.Path]::IsPathRooted($env:EXPORTDOCMANAGER_DATA_ROOT)) {
        [System.IO.Path]::GetFullPath($env:EXPORTDOCMANAGER_DATA_ROOT)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $root $env:EXPORTDOCMANAGER_DATA_ROOT))
    }
} else {
    Join-Path $root "App_Data"
}
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

$configPath = Join-Path $dataRoot "Config\appsettings.json"
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "appsettings.json was not found: $configPath"
}
if ((Get-Content -LiteralPath $configPath -Raw).Contains("CHANGE_ME_BEFORE_START")) {
    throw "请先编辑 appsettings.json，填写 PostgreSQL 地址、账号和密码。"
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

$env:EXPORTDOCMANAGER_NETWORK_MODE = "true"
$env:EXPORTDOCMANAGER_PRODUCT_EDITION = "Full"
$env:EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE = $browser.FullName
& (Join-Path $root "ExportDocManager.Api.exe") `
    --app-root $root `
    --data-root $dataRoot `
    --urls $effectiveUrls `
    --network-mode true
exit $LASTEXITCODE
