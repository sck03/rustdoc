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
        '(?m)^\s*(?:[a-z_][a-z0-9_]*::)+(?<command>[a-z_][a-z0-9_]*)\s*,?\s*$'
    ) | ForEach-Object { $_.Groups["command"].Value } | Sort-Object -Unique
)
$permissionAllowMatch = [regex]::Match(
    $permissionSource,
    '(?s)commands\.allow\s*=\s*\[(?<commands>.*?)\]'
)
if (-not $permissionAllowMatch.Success) {
    throw "无法在 '$permissionPath' 中找到 commands.allow 桌面权限白名单。"
}

$allowedCommands = @(
    [regex]::Matches(
        $permissionAllowMatch.Groups["commands"].Value,
        '"(?<command>[a-z_][a-z0-9_]*)"'
    ) | ForEach-Object { $_.Groups["command"].Value } |
        Sort-Object -Unique
)
$missingPermissions = @($registeredCommands | Where-Object { $_ -notin $allowedCommands })
if ($missingPermissions.Count -gt 0) {
    throw "以下 Tauri 命令已在 Rust 主程序注册，但未加入桌面权限白名单：$($missingPermissions -join ', ')"
}
$stalePermissions = @($allowedCommands | Where-Object { $_ -notin $registeredCommands })
if ($stalePermissions.Count -gt 0) {
    throw "以下桌面权限白名单命令未在 Rust 主程序注册：$($stalePermissions -join ', ')"
}

[pscustomobject]@{
    Success = $true
    RegisteredCommandCount = $registeredCommands.Count
    AllowedCommandCount = $allowedCommands.Count
    PermissionPath = $permissionPath
} | ConvertTo-Json -Depth 3
