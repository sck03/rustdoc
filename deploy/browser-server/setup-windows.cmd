@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo 未找到 PowerShell 7（pwsh.exe）。请先安装 PowerShell 7，再重新双击本文件。
  pause
  exit /b 1
)
pwsh.exe -NoProfile -NonInteractive -Command "if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }" >nul 2>nul
if errorlevel 1 (
  echo 当前 pwsh.exe 不是受支持的 PowerShell 7。请升级后重试。
  pause
  exit /b 1
)

echo 浏览器服务器默认监听本机所有网卡的 HTTP 5188 端口。
echo 只有服务器位于受防火墙保护的可信办公网或 VPN 时，才允许通过 HTTP 执行备份恢复与完整迁移。
choice /C YN /N /M "这是可信办公网/VPN，启用 HTTP 灾难恢复功能吗？[Y/N] "
if errorlevel 2 (
  pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0initialize-windows.ps1" -Start
) else (
  pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0initialize-windows.ps1" -AllowHttpDisasterRecovery -Start
)

if errorlevel 1 (
  echo 初始化或启动失败，请阅读上方错误信息。
  pause
  exit /b 1
)
exit /b 0
