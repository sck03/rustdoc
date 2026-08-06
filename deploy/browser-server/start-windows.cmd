@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo 未找到 PowerShell 7（pwsh.exe）。请先安装 PowerShell 7。
  pause
  exit /b 1
)
pwsh.exe -NoProfile -NonInteractive -Command "if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }" >nul 2>nul
if errorlevel 1 (
  echo 当前 pwsh.exe 不是受支持的 PowerShell 7。请升级后重试。
  pause
  exit /b 1
)

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-windows.ps1"
if errorlevel 1 (
  echo 启动失败，请阅读上方错误信息。
  pause
  exit /b 1
)
exit /b 0
