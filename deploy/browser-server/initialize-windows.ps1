[CmdletBinding()]
param(
    [string]$PostgreSqlHost = "127.0.0.1",
    [int]$PostgreSqlPort = 5432,
    [string]$PostgreSqlDatabase = "exportdoc",
    [string]$PostgreSqlUsername = "exportdoc",
    [string]$PostgreSqlPassword = "",
    [string]$BootstrapToken = "",
    [string]$Urls = "http://0.0.0.0:5188",
    [string]$DataRoot = (Join-Path $PSScriptRoot "App_Data"),
    [string]$AllowedOrigins = "",
    [string]$TrustedProxies = "",
    [string]$MasterKey = "",
    [switch]$AllowHttpDisasterRecovery,
    [switch]$Force,
    [switch]$Start
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$generatedBootstrapToken = $false

function Read-RequiredSecret {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    $secureValue = Read-Host $Prompt -AsSecureString
    $pointer = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try {
        return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Assert-NoControlCharacters {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[\x00-\x1F\x7F]') {
        throw "$Name 不能为空或包含控制字符。"
    }
}

function Assert-NoOptionalControlCharacters {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowEmptyString()][string]$Value
    )

    if ($Value -match '[\x00-\x1F\x7F]') {
        throw "$Name 不能包含控制字符。"
    }
}

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

function Assert-SafeManagedFilePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "受管配置路径必须是普通文件且不能是符号链接或重解析点：$Path"
    }
}

function Protect-WindowsConfigurationFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $icacls = Get-Command icacls.exe -ErrorAction Stop
        $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        & $icacls.Source $Path /inheritance:r `
            /grant:r "$($currentIdentity):(M)" `
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

function Protect-WindowsDataDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $icacls = Get-Command icacls.exe -ErrorAction Stop
        $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        & $icacls.Source $Path /inheritance:r `
            /grant:r "$($currentIdentity):(OI)(CI)(M)" `
            /grant:r "*S-1-5-18:(OI)(CI)(F)" `
            /grant:r "*S-1-5-32-544:(OI)(CI)(F)" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "无法完全收紧数据目录 ACL，请手工限制该目录访问：$Path"
        }
    }
    catch {
        Write-Warning "未能自动收紧数据目录 ACL，请手工限制该目录访问：$Path"
    }
}

function Write-Utf8FileAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    $temporaryPath = Join-Path $directory ".$([System.IO.Path]::GetFileName($Path)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $Content,
            [System.Text.UTF8Encoding]::new($false))
        Protect-WindowsConfigurationFile $temporaryPath
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

Assert-SafeValue "PostgreSQL 主机" $PostgreSqlHost '^[A-Za-z0-9._:-]+$'
Assert-SafeValue "PostgreSQL 数据库名" $PostgreSqlDatabase '^[A-Za-z0-9_.-]+$'
Assert-SafeValue "PostgreSQL 用户名" $PostgreSqlUsername '^[A-Za-z0-9_.-]+$'
if ([string]::IsNullOrWhiteSpace($PostgreSqlPassword)) {
    $PostgreSqlPassword = Read-RequiredSecret "请输入 PostgreSQL 密码（输入不会回显）"
}
if ([string]::IsNullOrWhiteSpace($BootstrapToken)) {
    $randomToken = [Convert]::ToBase64String(
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $BootstrapToken = $randomToken.TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $generatedBootstrapToken = $true
}
Assert-NoControlCharacters "PostgreSQL 密码" $PostgreSqlPassword
Assert-NoControlCharacters "首次部署令牌" $BootstrapToken
if ($PostgreSqlPassword.Length -lt 12 -or $PostgreSqlPassword.Length -gt 1024) {
    throw "PostgreSQL 密码长度必须为 12-1024 位。"
}
if ($BootstrapToken.Length -lt 24 -or $BootstrapToken.Length -gt 512) {
    throw "首次部署令牌长度必须为 24-512 位。"
}
if ($PostgreSqlPort -lt 1 -or $PostgreSqlPort -gt 65535) {
    throw "PostgreSQL 端口必须在 1-65535 之间。"
}
Assert-NoControlCharacters "监听地址" $Urls
Assert-NoControlCharacters "数据根" $DataRoot
Assert-NoOptionalControlCharacters "允许来源" $AllowedOrigins
Assert-NoOptionalControlCharacters "可信代理" $TrustedProxies
Assert-NoOptionalControlCharacters "主密钥" $MasterKey
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

$resolvedDataRoot = Assert-SafeDataRoot $DataRoot
$securityRoot = Assert-SafeDataRoot (Join-Path $resolvedDataRoot "Security")
$configRoot = Assert-SafeDataRoot (Join-Path $resolvedDataRoot "Config")
New-Item -ItemType Directory -Force -Path $securityRoot | Out-Null
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
[void](Assert-SafeDataRoot $resolvedDataRoot)
[void](Assert-SafeDataRoot $securityRoot)
[void](Assert-SafeDataRoot $configRoot)
foreach ($dataDirectory in @($resolvedDataRoot, $securityRoot, $configRoot)) {
    Protect-WindowsDataDirectory $dataDirectory
}
$configPath = Join-Path $configRoot "appsettings.json"
$environmentPath = Join-Path $securityRoot "browser-server.env"
$environmentPointerPath = Join-Path $root "browser-server.env.path"
foreach ($managedPath in @($configPath, $environmentPath, $environmentPointerPath)) {
    Assert-SafeManagedFilePath $managedPath
}
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
$settingsJson = ($settings | ConvertTo-Json -Depth 5) + [Environment]::NewLine
Write-Utf8FileAtomically $configPath $settingsJson

$environmentLines = @(
    "EXPORTDOCMANAGER_POSTGRES_PASSWORD=$PostgreSqlPassword",
    "EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=$BootstrapToken",
    "EXPORTDOCMANAGER_URLS=$Urls",
    "EXPORTDOCMANAGER_DATA_ROOT=$resolvedDataRoot",
    "EXPORTDOCMANAGER_ALLOWED_ORIGINS=$AllowedOrigins",
    "EXPORTDOCMANAGER_TRUSTED_PROXIES=$TrustedProxies",
    "EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=$($AllowHttpDisasterRecovery.IsPresent.ToString().ToLowerInvariant())",
    "EXPORTDOCMANAGER_NETWORK_MODE=true",
    "EXPORTDOCMANAGER_PRODUCT_EDITION=Full"
)
if (-not [string]::IsNullOrWhiteSpace($MasterKey)) {
    $environmentLines += "EXPORTDOCMANAGER_MASTER_KEY=$MasterKey"
}
$environmentText = [string]::Join([Environment]::NewLine, $environmentLines) + [Environment]::NewLine
Write-Utf8FileAtomically $environmentPath $environmentText
Write-Utf8FileAtomically $environmentPointerPath ($environmentPath + [Environment]::NewLine)

Write-Host "浏览器服务器配置已完成。"
Write-Host "数据库配置: $configPath"
Write-Host "运行环境（含数据库密码和首次部署令牌）: $environmentPath"
Write-Host "数据根: $resolvedDataRoot"
Write-Host "监听: $Urls"
if ($AllowHttpDisasterRecovery) {
    Write-Warning "已允许通过纯 HTTP 执行网页备份恢复和完整迁移；只应在受防火墙保护的可信办公网/VPN 使用。"
}
if ($generatedBootstrapToken) {
    Write-Host "首次部署令牌（仅显示这一次）: $BootstrapToken"
}
Write-Host "该脚本不会安装 PostgreSQL、修改防火墙或注册 Windows 服务。"

if ($Start) {
    & (Join-Path $root "start-windows.ps1") -Urls $Urls
    exit $LASTEXITCODE
}
