@echo off
setlocal

set "PAUSE_AFTER_RUN=1"
if "%EXPORTDOCMANAGER_NO_PAUSE%"=="1" set "PAUSE_AFTER_RUN=0"
if /i "%CI%"=="true" set "PAUSE_AFTER_RUN=0"
if "%CI%"=="1" set "PAUSE_AFTER_RUN=0"
for %%A in (%*) do if /i "%%~A"=="-NoPause" set "PAUSE_AFTER_RUN=0"
set "EXPORTDOCMANAGER_NO_PAUSE=1"

if not defined EXPORTDOCMANAGER_PS_SCRIPT (
  echo Internal error: EXPORTDOCMANAGER_PS_SCRIPT is not set.
  set "EXIT_CODE=2"
  goto finish
)

if not exist "%EXPORTDOCMANAGER_PS_SCRIPT%" (
  echo PowerShell script was not found:
  echo   "%EXPORTDOCMANAGER_PS_SCRIPT%"
  set "EXIT_CODE=2"
  goto finish
)

where pwsh.exe >nul 2>nul
if not errorlevel 1 (
  set "PS_EXE=pwsh.exe"
  goto run_script
)

where powershell.exe >nul 2>nul
if not errorlevel 1 (
  set "PS_EXE=powershell.exe"
  goto run_script
)

echo PowerShell was not found. Install PowerShell 7 or enable Windows PowerShell.
set "EXIT_CODE=1"
goto finish

:run_script
"%PS_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%EXPORTDOCMANAGER_PS_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

:finish
echo.
if "%EXIT_CODE%"=="0" (
  echo Operation completed successfully.
) else (
  echo Operation failed with exit code %EXIT_CODE%.
)
if "%PAUSE_AFTER_RUN%"=="1" (
  echo Press any key to close this window . . .
  pause >nul
)
endlocal & exit /b %EXIT_CODE%
