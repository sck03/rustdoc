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
$permissionPath = Join-Path $RepositoryRoot "apps\export-doc-tauri\src-tauri\permissions"
$mainSource = Get-Content -LiteralPath $mainSourcePath -Raw -Encoding UTF8
$permissionSource = (Get-ChildItem -LiteralPath $permissionPath -Filter '*.toml' -File | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
}) -join "`n"
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
$permissionAllowMatches = [regex]::Matches(
    $permissionSource,
    '(?s)commands\.allow\s*=\s*\[(?<commands>.*?)\]'
)
if ($permissionAllowMatches.Count -eq 0) {
    throw "无法在 '$permissionPath' 中找到 commands.allow 桌面权限白名单。"
}

$allowedCommands = @(
    $permissionAllowMatches | ForEach-Object {
        [regex]::Matches($_.Groups['commands'].Value, '"(?<command>[a-z_][a-z0-9_]*)"') |
            ForEach-Object { $_.Groups['command'].Value }
    } |
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
