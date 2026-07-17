[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
}

$mainSourcePath = Join-Path $RepositoryRoot "apps\export-doc-tauri\src-tauri\src\main.rs"
$permissionPath = Join-Path $RepositoryRoot "apps\export-doc-tauri\src-tauri\permissions\desktop-bridge.toml"
$mainSource = Get-Content -LiteralPath $mainSourcePath -Raw -Encoding UTF8
$permissionSource = Get-Content -LiteralPath $permissionPath -Raw -Encoding UTF8
$handlerMatch = [regex]::Match($mainSource, '(?s)invoke_handler\(tauri::generate_handler!\[(.*?)\]\)')
if (-not $handlerMatch.Success) {
    throw "无法在 '$mainSourcePath' 中找到 Tauri 命令注册表。"
}

$registeredCommands = @(
    [regex]::Matches(
        $handlerMatch.Groups[1].Value,
        '(?:desktop_commands|tauri_updater_commands|sidecar)::([a-z0-9_]+)'
    ) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
)
$allowedCommands = @(
    [regex]::Matches($permissionSource, '"([a-z0-9_]+)"') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
)
$missingPermissions = @($registeredCommands | Where-Object { $_ -notin $allowedCommands })
if ($missingPermissions.Count -gt 0) {
    throw "以下 Tauri 命令已在 Rust 主程序注册，但未加入桌面权限白名单：$($missingPermissions -join ', ')"
}

[pscustomobject]@{
    Success = $true
    RegisteredCommandCount = $registeredCommands.Count
    AllowedCommandCount = $allowedCommands.Count
    PermissionPath = $permissionPath
} | ConvertTo-Json -Depth 3
