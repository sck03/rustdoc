[CmdletBinding()]
param(
    [string]$PostgreSqlHost = "127.0.0.1",
    [int]$PostgreSqlPort = 5432,
    [string]$PostgreSqlDatabase = "exportdoc",
    [string]$PostgreSqlUsername = "exportdoc",
    [Parameter(Mandatory = $true)]
    [string]$PostgreSqlPassword,
    [Parameter(Mandatory = $true)]
    [string]$BootstrapToken,
    [string]$Urls = "http://0.0.0.0:5188",
    [string]$DataRoot = (Join-Path $PSScriptRoot "App_Data"),
    [string]$AllowedOrigins = "",
    [string]$TrustedProxies = "",
    [string]$MasterKey = "",
    [switch]$Force,
    [switch]$Start
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($PSScriptRoot)

function Assert-SafeValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch $Pattern) {
        throw "$Name 包含不支持的字符或为空。"
    }
}

Assert-SafeValue "PostgreSQL 主机" $PostgreSqlHost '^[A-Za-z0-9._:-]+$'
Assert-SafeValue "PostgreSQL 数据库名" $PostgreSqlDatabase '^[A-Za-z0-9_.-]+$'
Assert-SafeValue "PostgreSQL 用户名" $PostgreSqlUsername '^[A-Za-z0-9_.-]+$'
Assert-SafeValue "PostgreSQL 密码" $PostgreSqlPassword '^[A-Za-z0-9._~!@%+=:-]+$'
Assert-SafeValue "首次部署令牌" $BootstrapToken '^[A-Za-z0-9._~!@%+=:-]{24,512}$'
if ($PostgreSqlPassword.Length -lt 12) {
    throw "PostgreSQL 密码至少需要 12 位。"
}
if ($PostgreSqlPort -lt 1 -or $PostgreSqlPort -gt 65535) {
    throw "PostgreSQL 端口必须在 1-65535 之间。"
}
if ([string]::IsNullOrWhiteSpace($Urls) -or $Urls.Contains("`n") -or $Urls.Contains("`r")) {
    throw "监听地址不能为空或包含换行。"
}
if ($AllowedOrigins.Contains("`n") -or $AllowedOrigins.Contains("`r") -or
    $TrustedProxies.Contains("`n") -or $TrustedProxies.Contains("`r") -or
    $MasterKey.Contains("`n") -or $MasterKey.Contains("`r") -or
    $DataRoot.Contains("`n") -or $DataRoot.Contains("`r")) {
    throw "来源、代理或主密钥配置不能包含换行。"
}
if (-not [string]::IsNullOrWhiteSpace($TrustedProxies) -and
    $TrustedProxies -notmatch '^[0-9A-Fa-f:.,; ]+$') {
    throw "可信代理只能填写 IP 地址，并用逗号或分号分隔。"
}
if (-not [string]::IsNullOrWhiteSpace($MasterKey)) {
    try {
        $masterKeyBytes = if ($MasterKey.Trim().Length -eq 64 -and $MasterKey.Trim() -match '^[0-9A-Fa-f]{64}$') {
            [Convert]::FromHexString($MasterKey.Trim())
        } else {
            [Convert]::FromBase64String($MasterKey.Trim())
        }
    }
    catch {
        throw "主密钥必须是 32 字节 Base64 或 64 位十六进制文本。"
    }
    if ($masterKeyBytes.Length -ne 32) {
        throw "主密钥解码后必须恰好为 32 字节。"
    }
}

$resolvedDataRoot = [System.IO.Path]::GetFullPath($DataRoot)
$securityRoot = Join-Path $resolvedDataRoot "Security"
$configRoot = Join-Path $resolvedDataRoot "Config"
New-Item -ItemType Directory -Force -Path $securityRoot | Out-Null
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
$configPath = Join-Path $configRoot "appsettings.json"
$environmentPath = Join-Path $securityRoot "browser-server.env"
if (-not $Force -and (Test-Path -LiteralPath $configPath -PathType Leaf) -and
    -not (Get-Content -LiteralPath $configPath -Raw).Contains("CHANGE_ME_BEFORE_START")) {
    throw "已存在有效 appsettings.json。若确认覆盖配置，请增加 -Force。"
}
if (-not $Force -and (Test-Path -LiteralPath $environmentPath -PathType Leaf)) {
    throw "已存在 browser-server.env。若确认覆盖令牌/运行配置，请增加 -Force。"
}

$settings = [ordered]@{
    System = [ordered]@{
        DatabaseProvider = "PostgreSQL"
        SqliteDatabaseFileName = "data.db"
        PostgreSqlHost = $PostgreSqlHost
        PostgreSqlPort = $PostgreSqlPort
        PostgreSqlDatabase = $PostgreSqlDatabase
        PostgreSqlUsername = $PostgreSqlUsername
        PostgreSqlPassword = ""
        PostgreSqlAdditionalOptions = "Pooling=true;Maximum Pool Size=100;Timeout=15;Command Timeout=60"
    }
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding UTF8

$environmentLines = @(
    "EXPORTDOCMANAGER_POSTGRES_PASSWORD=$PostgreSqlPassword",
    "EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=$BootstrapToken",
    "EXPORTDOCMANAGER_URLS=$Urls",
    "EXPORTDOCMANAGER_DATA_ROOT=$resolvedDataRoot",
    "EXPORTDOCMANAGER_ALLOWED_ORIGINS=$AllowedOrigins",
    "EXPORTDOCMANAGER_TRUSTED_PROXIES=$TrustedProxies",
    "EXPORTDOCMANAGER_NETWORK_MODE=true",
    "EXPORTDOCMANAGER_PRODUCT_EDITION=Full"
)
if (-not [string]::IsNullOrWhiteSpace($MasterKey)) {
    $environmentLines += "EXPORTDOCMANAGER_MASTER_KEY=$MasterKey"
}
$environmentLines | Set-Content -LiteralPath $environmentPath -Encoding UTF8
Set-Content -LiteralPath (Join-Path $root "browser-server.env.path") -Value $environmentPath -Encoding UTF8

function Protect-WindowsConfigurationFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $icacls = Get-Command icacls.exe -ErrorAction Stop
        $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        & $icacls.Source $Path /inheritance:r `
            /grant:r "$($currentIdentity):(R,W)" `
            /grant:r "*S-1-5-18:(F)" `
            /grant:r "*S-1-5-32-544:(F)" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "无法完全收紧配置文件 ACL，请手工限制该文件访问：$Path"
        }
    }
    catch {
        Write-Warning "未能自动收紧配置文件 ACL，请手工限制该文件访问：$Path"
    }
}

foreach ($configurationFile in @($configPath, $environmentPath)) {
    Protect-WindowsConfigurationFile $configurationFile
}

Write-Host "浏览器服务器配置已完成。"
Write-Host "数据库配置: $configPath"
Write-Host "运行环境（含数据库密码和首次部署令牌）: $environmentPath"
Write-Host "数据根: $resolvedDataRoot"
Write-Host "监听: $Urls"
Write-Host "该脚本不会安装 PostgreSQL、修改防火墙或注册 Windows 服务。"

if ($Start) {
    & (Join-Path $root "start-windows.ps1") -Urls $Urls
    exit $LASTEXITCODE
}
