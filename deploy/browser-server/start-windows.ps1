[CmdletBinding()]
param(
    [string]$Urls = "http://0.0.0.0:5188"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$configPath = Join-Path $root "appsettings.json"
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

$env:EXPORTDOCMANAGER_NETWORK_MODE = "true"
$env:EXPORTDOCMANAGER_PRODUCT_EDITION = "Full"
$env:EXPORTDOCMANAGER_CHROMIUM_EXECUTABLE = $browser.FullName
$dataRoot = Join-Path $root "App_Data"
& (Join-Path $root "ExportDocManager.Api.exe") `
    --app-root $root `
    --data-root $dataRoot `
    --urls $Urls `
    --network-mode true
exit $LASTEXITCODE
