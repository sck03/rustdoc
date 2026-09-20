@echo off
setlocal
set "EXPORTDOCMANAGER_PS_SCRIPT=%~dp0build-windows-desktop-run.ps1"
"%~dp0lib\run-powershell-entry.cmd" %*
