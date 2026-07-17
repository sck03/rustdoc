param(
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot "runtime"),
    [string]$PostgreSqlDatabase = "exportdoc",
    [string]$PostgreSqlUsername = "exportdoc",
    [Parameter(Mandatory = $true)]
    [string]$PostgreSqlPassword,
    [int]$WebPort = 8080
)

$ErrorActionPreference = "Stop"
if ($PostgreSqlPassword.Length -lt 12 -or $PostgreSqlPassword -notmatch '^[A-Za-z0-9._~!@%+=:-]+$') {
    throw "PostgreSQL 密码至少 12 位，且只能使用字母、数字和 . _ ~ ! @ % + = : -，避免 .env 转义歧义。"
}
$resolvedRuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
$configRoot = Join-Path $resolvedRuntimeRoot "config"
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRuntimeRoot "api-data") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRuntimeRoot "postgres") | Out-Null

$settings = [ordered]@{
    System = [ordered]@{
        DatabaseProvider = "PostgreSQL"
        SqliteDatabaseFileName = "data.db"
        PostgreSqlHost = "postgres"
        PostgreSqlPort = 5432
        PostgreSqlDatabase = $PostgreSqlDatabase
        PostgreSqlUsername = $PostgreSqlUsername
        PostgreSqlPassword = $PostgreSqlPassword
        PostgreSqlAdditionalOptions = "Pooling=true;Maximum Pool Size=100;Timeout=15;Command Timeout=60"
    }
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $configRoot "appsettings.json") -Encoding UTF8

$relativeRuntimeRoot = [System.IO.Path]::GetRelativePath($PSScriptRoot, $resolvedRuntimeRoot).Replace("\", "/")
$envLines = @(
    "POSTGRES_DB=$PostgreSqlDatabase",
    "POSTGRES_USER=$PostgreSqlUsername",
    "POSTGRES_PASSWORD=$PostgreSqlPassword",
    "EXPORTDOCMANAGER_WEB_PORT=$WebPort",
    "EXPORTDOCMANAGER_RUNTIME_ROOT=$relativeRuntimeRoot",
    "EXPORTDOCMANAGER_ALLOWED_ORIGINS=",
    "TZ=Asia/Shanghai"
)
$envLines | Set-Content -LiteralPath (Join-Path $PSScriptRoot ".env") -Encoding UTF8

Write-Host "容器运行目录已初始化: $resolvedRuntimeRoot"
Write-Host "配置文件: $(Join-Path $configRoot 'appsettings.json')"
Write-Host "下一步: docker compose up -d --build"
